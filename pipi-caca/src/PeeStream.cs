using System.Collections.Generic;
using UnityEngine;

namespace Caca
{
    /// <summary>
    /// The stream, simulated: drops leave the tip ~70 times a second with the aim direction, the strength and the
    /// player's own velocity, then fly under gravity (a little drag) until a raycast meets the ground, a building, a
    /// creature, or the water surface. The young drops are drawn as one continuous jet (a line through them, so a
    /// turn or a step bends it like a real hose); older ones break up into separate stretched drops. Impacts splash,
    /// darken the ground with a fading wet patch, and drive a looped sound (ground or water) placed where they land.
    /// One per player: the local one reads Pee directly, remote ones read the player's ZDO.
    /// </summary>
    internal class PeeStream : MonoBehaviour
    {
        private struct Drop
        {
            public Vector3 Pos, Vel;
            public float Age, Size;
        }

        private static readonly Dictionary<Character, PeeStream> s_of = new Dictionary<Character, PeeStream>();
        public static PeeStream Of(Character c) => c != null && s_of.TryGetValue(c, out var s) ? s : null;

        private const float Rate = 70f, MaxAge = 3f, JetMin = 0.3f;
        private static int s_mask;
        private static Material s_lineMat, s_dropMat, s_wetMat;
        private static readonly Color PeeColor = new Color(1f, 0.84f, 0.18f, 0.85f);

        public float Power, HandWeight;
        private Vector3 m_handTip;
        private int m_handTipFrame;
        private bool m_hasHandTip;

        /// <summary>Called by the arm IK once the fist is placed.</summary>
        public void SetHandTip(Vector3 tip)
        {
            m_handTip = tip;
            m_handTipFrame = Time.frameCount;
            m_hasHandTip = true;
        }
        public Vector3 Dir = Vector3.forward;
        private Player m_player;
        private ZNetView m_nview;
        private readonly List<Drop> m_drops = new List<Drop>(400);       // oldest first
        private float m_emitAcc, m_time;
        private LineRenderer m_line;
        private ParticleSystem m_dropsPs, m_splashPs;
        private ParticleSystem.Particle[] m_buf = new ParticleSystem.Particle[400];
        private AudioSource m_ground, m_water;
        private Vector3 m_soundAt;
        private float m_hitRate, m_waterShare;
        private bool m_built;

        private void Awake()
        {
            m_player = GetComponent<Player>();
            m_nview = GetComponent<ZNetView>();
            if (m_player != null) s_of[m_player] = this;
        }

        private void OnDestroy()
        {
            if (m_player != null) s_of.Remove(m_player);
            if (m_line != null) Destroy(m_line.gameObject);
            if (m_dropsPs != null) Destroy(m_dropsPs.gameObject);
            if (m_splashPs != null) Destroy(m_splashPs.gameObject);
            if (m_ground != null) Destroy(m_ground.gameObject);
            if (m_water != null) Destroy(m_water.gameObject);
        }

        private bool m_squat;

        private void ReadState(out float power, out bool hold, out Vector3 aimLocal)
        {
            if (m_player == Player.m_localPlayer)
            {
                power = Pee.Power;
                hold = Pee.Active;
                aimLocal = Pee.AimLocal;
                m_squat = Pee.Squat;
                return;
            }
            power = 0f; hold = false; aimLocal = Vector3.forward;
            if (m_nview == null || !m_nview.IsValid()) return;
            var zdo = m_nview.GetZDO();
            power = zdo.GetFloat(Pee.ZdoPower);
            hold = zdo.GetFloat(Pee.ZdoHold) > 0.5f;
            aimLocal = zdo.GetVec3(Pee.ZdoAim, Vector3.forward);
            m_squat = zdo.GetFloat(Pee.ZdoSquat) > 0.5f;
        }

        private void LateUpdate()
        {
            if (m_player == null) return;
            ReadState(out float target, out bool hold, out var aimLocal);
            if (m_drops.Count == 0 && !hold && HandWeight <= 0f && Power <= 0f)
            {
                if (m_built) Idle();
                return;
            }
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            m_time += dt;
            // standing: the right hand holds the tip; squatting: hands stay where the crouch puts them
            HandWeight = Mathf.MoveTowards(HandWeight, !m_squat && (hold || target > 0f) ? 1f : 0f, dt * 4f);
            Power = target > Power ? Mathf.MoveTowards(Power, target, dt * 5f) : target;
            Dir = (m_player.transform.rotation * aimLocal).normalized;
            if (!m_built) Build();
            HideWeapon(HandWeight > 0f);

            // the stream leaves the fist where the arm IK put it last frame; until then, a point from the hips
            Vector3 tip;
            if (m_squat) Body.SquatTip(m_player, out tip);
            else if (Time.frameCount - m_handTipFrame > 2 || !m_hasHandTip) Body.Tip(m_player, out tip);
            else tip = m_handTip;
            Emit(tip, dt);
            Simulate(dt, tip.y);
            Draw(tip);
            Sound(dt);
        }

        // ------------------------------------------------------------------ emission and flight
        private void Emit(Vector3 tip, float dt)
        {
            if (Power <= 0.01f)
            {
                m_emitAcc = 0f;
                return;
            }
            m_emitAcc += dt * Rate;
            var bodyVel = m_player.GetVelocity();
            float speed = Mathf.Lerp(Plugin.PeeMinSpeed.Value, Plugin.PeeMaxSpeed.Value, Power);
            if (m_squat) speed *= Plugin.SquatSpeed.Value;
            // the flow is never perfectly steady: a slow surge and a faster flutter
            speed *= 1f + 0.05f * Mathf.Sin(m_time * 2.3f) + 0.03f * Mathf.Sin(m_time * 13.7f + 1.3f);
            float spread = Mathf.Lerp(0.18f, m_squat ? 0.05f : 0.015f, Power);   // weak flow wobbles and splits
            int n = 0;
            while (m_emitAcc >= 1f)
            {
                m_emitAcc -= 1f;
                float lag = m_emitAcc / Rate;                             // sub-frame: spaced like a steady flow
                var v = (Dir + Random.insideUnitSphere * spread).normalized * speed + bodyVel;
                v += Physics.gravity * lag;
                var d = new Drop { Pos = tip + v * lag, Vel = v, Age = lag, Size = Random.Range(0.8f, 1.2f) };
                m_drops.Add(d);
                if (++n > 8) { m_emitAcc = 0f; break; }
            }
        }

        private void Simulate(float dt, float tipY)
        {
            if (s_mask == 0)
                s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle",
                    "character", "character_net", "character_noenv", "hitbox");
            var g = Physics.gravity;
            int hits = 0, waterHits = 0;
            Vector3 hitSum = Vector3.zero;
            for (int i = m_drops.Count - 1; i >= 0; i--)
            {
                var d = m_drops[i];
                var prev = d.Pos;
                d.Vel += g * dt;
                d.Vel *= 1f - 0.12f * dt;
                d.Pos += d.Vel * dt;
                d.Age += dt;
                var step = d.Pos - prev;
                bool dead = d.Age > MaxAge;
                if (!dead && FirstHit(prev, step, out var hit))
                {
                    Splash(hit.point, hit.normal, d.Vel, false);
                    Wet(hit.point, hit.normal, hit.collider);
                    hitSum += hit.point; hits++;
                    dead = true;
                }
                else if (!dead && d.Vel.y < 0f && d.Pos.y < tipY - 0.4f)
                {
                    float level = Floating.GetLiquidLevel(d.Pos);
                    if (level > -1000f && d.Pos.y < level)
                    {
                        var at = new Vector3(d.Pos.x, level, d.Pos.z);
                        Splash(at, Vector3.up, d.Vel, true);
                        hitSum += at; hits++; waterHits++;
                        dead = true;
                    }
                }
                if (dead) m_drops.RemoveAt(i);
                else m_drops[i] = d;
            }
            float rate = hits / Mathf.Max(dt, 1e-3f);
            m_hitRate = Mathf.Lerp(m_hitRate, rate, 1f - Mathf.Exp(-dt * 6f));
            if (hits > 0)
            {
                var c = hitSum / hits;
                m_soundAt = m_soundAt == Vector3.zero ? c : Vector3.Lerp(m_soundAt, c, 1f - Mathf.Exp(-dt * 8f));
                m_waterShare = Mathf.Lerp(m_waterShare, (float)waterHits / hits, 1f - Mathf.Exp(-dt * 4f));
            }
        }

        private static readonly RaycastHit[] s_hits = new RaycastHit[8];

        /// <summary>The nearest hit along the step that isn't the peeing player's own body.</summary>
        private bool FirstHit(Vector3 from, Vector3 step, out RaycastHit best)
        {
            best = default;
            float len = step.magnitude;
            if (len < 1e-5f) return false;
            int n = Physics.RaycastNonAlloc(from, step / len, s_hits, len, s_mask, QueryTriggerInteraction.Ignore);
            float bestDist = float.MaxValue;
            for (int i = 0; i < n; i++)
            {
                var h = s_hits[i];
                if (h.collider == null || h.collider.transform.root == m_player.transform) continue;
                if (h.distance < bestDist) { bestDist = h.distance; best = h; }
            }
            return bestDist < float.MaxValue;
        }

        // ------------------------------------------------------------------ looks
        private float BreakAge => Mathf.Lerp(0.06f, 0.24f, Power);    // how long the jet stays one piece

        private void Draw(Vector3 tip)
        {
            float light = Light();
            var col = PeeColor;
            col.r *= light; col.g *= light; col.b *= light;

            // the coherent jet: newest drops, from the tip, while they are young and the flow is on
            int count = 0;
            if (Power > JetMin)                       // a weak flow is only drops (a line through them zigzags)
            {
                var pts = s_pts;
                pts.Clear();
                pts.Add(tip);
                for (int i = m_drops.Count - 1; i >= 0 && m_drops[i].Age < BreakAge; i--)
                    pts.Add(m_drops[i].Pos);
                count = pts.Count;
                if (count >= 2)
                {
                    m_line.positionCount = count;
                    for (int i = 0; i < count; i++) m_line.SetPosition(i, pts[i]);
                    float w = Mathf.Lerp(0.011f, 0.02f, Power);
                    m_line.widthCurve = new AnimationCurve(new Keyframe(0f, w * 0.8f), new Keyframe(0.6f, w), new Keyframe(1f, w * 0.7f));
                    m_line.startColor = col;
                    var end = col; end.a *= 0.85f;
                    m_line.endColor = end;
                }
            }
            m_line.enabled = count >= 2;

            // the rest: separate drops, stretched along their velocity
            int k = 0;
            float size = Mathf.Lerp(0.016f, 0.026f, Power);
            for (int i = 0; i < m_drops.Count && k < m_buf.Length; i++)
            {
                var d = m_drops[i];
                if (d.Age < BreakAge && Power > JetMin) continue;
                m_buf[k].position = d.Pos;
                m_buf[k].velocity = d.Vel;
                m_buf[k].startSize = size * d.Size;
                m_buf[k].startColor = col;
                m_buf[k].remainingLifetime = 1f;
                m_buf[k].startLifetime = 2f;
                k++;
            }
            m_dropsPs.SetParticles(m_buf, k);
            var main = m_splashPs.main;
            var sc = col; sc.a = 0.7f;
            main.startColor = sc;
        }

        private static readonly List<Vector3> s_pts = new List<Vector3>(64);

        /// <summary>The materials are unlit: follow the scene's light so the stream doesn't glow at night.</summary>
        private static float Light()
        {
            var a = RenderSettings.ambientLight;
            float amb = 0.2126f * a.r + 0.7152f * a.g + 0.0722f * a.b;
            float sun = 0f;
            var s = RenderSettings.sun;
            if (s != null && s.enabled) sun = s.intensity * Mathf.Clamp01(-s.transform.forward.y * 2f + 0.2f);
            return Mathf.Clamp(0.25f + amb * 1.1f + sun * 0.45f, 0.25f, 0.9f);   // below 1: no bloom glare
        }

        private void Splash(Vector3 at, Vector3 normal, Vector3 vel, bool water)
        {
            int n = Random.value < 0.6f ? 1 : 2;
            float speed = vel.magnitude;
            var reflect = Vector3.Reflect(vel, normal).normalized;
            for (int i = 0; i < n; i++)
            {
                var dir = (normal * 1.2f + reflect * 0.6f + Random.insideUnitSphere * 0.9f).normalized;
                var ep = new ParticleSystem.EmitParams
                {
                    position = at + normal * 0.02f,
                    velocity = dir * Random.Range(0.12f, 0.3f) * Mathf.Min(speed, 7f),
                    startSize = Random.Range(0.008f, water ? 0.02f : 0.016f),
                    startLifetime = Random.Range(0.25f, 0.5f),
                };
                if (water) ep.startColor = new Color(0.95f, 0.95f, 0.85f, 0.6f);
                m_splashPs.Emit(ep, 1);
            }
        }

        // ------------------------------------------------------------------ wet patches
        private class Patch
        {
            public Transform T;
            public Material M;
            public float Size, Born, Fed;
        }

        private static readonly List<Patch> s_patches = new List<Patch>();
        private const int MaxPatches = 24;

        private static void Wet(Vector3 at, Vector3 normal, Collider col)
        {
            if (!Plugin.WetPatches.Value || normal.y < 0.5f || col == null || col.GetComponentInParent<Character>() != null) return;
            float now = Time.time;
            Patch near = null;
            float best = 0.25f;
            foreach (var p in s_patches)
            {
                if (p.T == null) continue;
                float d = Vector3.Distance(p.T.position, at);
                if (d < best + p.Size * 0.25f) { near = p; best = d; }
            }
            if (near == null)
            {
                s_patches.RemoveAll(p => p.T == null);
                if (s_patches.Count >= MaxPatches)
                {
                    Destroy(s_patches[0].T.gameObject);
                    s_patches.RemoveAt(0);
                }
                var go = new GameObject("caca_wet");
                go.transform.position = at + normal * 0.012f;
                go.transform.rotation = Quaternion.FromToRotation(Vector3.up, normal) * Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
                go.AddComponent<MeshFilter>().sharedMesh = PeeArt.Quad();
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                mr.receiveShadows = false;
                var m = new Material(s_wetMat) { color = new Color(0.22f, 0.18f, 0.06f, 0f) };   // invisible until WetFade sets it
                mr.sharedMaterial = m;
                go.transform.localScale = Vector3.one * 0.05f;      // (a fresh object is 1 m: that flashed white for a frame)
                near = new Patch { T = go.transform, M = m, Size = 0.12f, Born = now };
                go.AddComponent<WetFade>().Patch = near;
                s_patches.Add(near);
            }
            near.Size = Mathf.Min(0.75f, near.Size + 0.004f);
            near.Fed = now;
        }

        private class WetFade : MonoBehaviour
        {
            public Patch Patch;

            private void Update()
            {
                float since = Time.time - Patch.Fed;
                float a = 0.55f * Mathf.Clamp01(1f - (since - 20f) / 40f);    // dries up after a minute
                if (a <= 0f)
                {
                    s_patches.Remove(Patch);
                    Destroy(gameObject);
                    return;
                }
                float grow = Mathf.Clamp01((Time.time - Patch.Born) / 1.5f);
                transform.localScale = Vector3.one * Patch.Size * (0.4f + 0.6f * grow);
                Patch.M.color = new Color(0.22f, 0.18f, 0.06f, a);
            }
        }

        // ------------------------------------------------------------------ sound
        private void Sound(float dt)
        {
            float flow = Mathf.Clamp01(m_hitRate / 45f);
            float vol = flow * (0.4f + 0.6f * Mathf.Max(Power, 0.3f)) * Plugin.PeeVolume.Value;
            Feed(m_ground, vol * (1f - m_waterShare), dt);
            Feed(m_water, vol * m_waterShare, dt);
            if (m_soundAt != Vector3.zero)
            {
                m_ground.transform.position = m_soundAt;
                m_water.transform.position = m_soundAt;
            }
            float pitch = 0.92f + 0.16f * Power;
            m_ground.pitch = pitch;
            m_water.pitch = pitch;
        }

        private static void Feed(AudioSource src, float target, float dt)
        {
            if (src == null || src.clip == null) return;
            src.volume = Mathf.MoveTowards(src.volume, target, dt * 2.5f);
            if (src.volume > 0.001f && !src.isPlaying)
            {
                src.time = Random.Range(0f, src.clip.length * 0.9f);
                src.Play();
            }
            else if (src.volume <= 0.001f && src.isPlaying) src.Stop();
        }

        // ------------------------------------------------------------------ setup
        private void Build()
        {
            m_built = true;
            if (s_lineMat == null)
            {
                var sh = Shader.Find("Sprites/Default");
                s_lineMat = new Material(sh) { name = "caca_pee_line", mainTexture = PeeArt.JetTexture() };
                s_dropMat = new Material(sh) { name = "caca_pee_drop", mainTexture = PeeArt.DropTexture() };
                s_wetMat = new Material(sh) { name = "caca_pee_wet", mainTexture = PeeArt.WetTexture() };
            }
            var lineGo = new GameObject("caca_pee_jet");
            m_line = lineGo.AddComponent<LineRenderer>();
            m_line.useWorldSpace = true;
            m_line.sharedMaterial = s_lineMat;
            m_line.textureMode = LineTextureMode.Stretch;
            m_line.numCapVertices = 2;
            m_line.alignment = LineAlignment.View;
            m_line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            m_line.receiveShadows = false;
            m_line.enabled = false;

            m_dropsPs = MakePs("caca_pee_drops", 400, false);
            var r = m_dropsPs.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.035f;
            r.lengthScale = 1.2f;

            m_splashPs = MakePs("caca_pee_splash", 500, true);

            m_ground = MakeAudio("caca_pee_ground", Plugin.PeeGroundClip);
            m_water = MakeAudio("caca_pee_water", Plugin.PeeWaterClip);
        }

        private static ParticleSystem MakePs(string name, int max, bool live)
        {
            var go = new GameObject(name);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.maxParticles = max;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startSpeed = 0f;
            main.gravityModifier = live ? 1f : 0f;
            main.startLifetime = 0.4f;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            if (live)
            {
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var gr = new Gradient();
                gr.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                           new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
                col.color = gr;
            }
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s_dropMat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            return ps;
        }

        private static AudioSource MakeAudio(string name, AudioClip clip)
        {
            var go = new GameObject(name);
            var src = go.AddComponent<AudioSource>();
            src.clip = clip;
            src.loop = true;
            src.playOnAwake = false;
            src.volume = 0f;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 2f;
            src.maxDistance = 22f;
            src.dopplerLevel = 0f;
            src.bypassReverbZones = true;            // dry: no cave / room echo on the stream
            src.reverbZoneMix = 0f;
            if (Plugin.SfxMixer != null) src.outputAudioMixerGroup = Plugin.SfxMixer;
            return src;
        }

        /// <summary>The right hand is busy: whatever it held is hidden until it's free again.</summary>
        private GameObject m_hidden;

        private void HideWeapon(bool hide)
        {
            if (hide)
            {
                var item = Body.RightItem(m_player.GetComponent<VisEquipment>());
                if (item != null && item.activeSelf)
                {
                    if (m_hidden != null && m_hidden != item) m_hidden.SetActive(true);
                    item.SetActive(false);
                    m_hidden = item;
                }
            }
            else if (m_hidden != null)
            {
                m_hidden.SetActive(true);
                m_hidden = null;
            }
        }

        private void Idle()
        {
            HideWeapon(false);
            m_built = false;
            Destroy(m_line.gameObject);
            Destroy(m_dropsPs.gameObject, 1f);       // let the last splashes finish
            Destroy(m_splashPs.gameObject, 1f);
            Destroy(m_ground.gameObject);
            Destroy(m_water.gameObject);
            m_line = null;
            m_dropsPs = m_splashPs = null;
            m_ground = m_water = null;
            m_hitRate = 0f;
            m_waterShare = 0f;
            m_soundAt = Vector3.zero;
        }
    }
}
