using System.Collections.Generic;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// The Thunder Spear's lightning call (local player only). Press the call key with the spear in hand: the
    /// spear crackles, and CallDelay seconds later a bolt falls on it, wherever it is.
    /// - Thrown and stuck in something: the bolt strikes the spear and hurts the creatures around it.
    /// - Still flying: the bolt waits for it to land.
    /// - Never thrown: the bolt strikes the spear in the hand, so the thrower takes the damage.
    /// The bolt is shown to every player (routed RPC); damage is dealt once, by the caller.
    /// </summary>
    internal static class LightningCall
    {
        public const string RpcName = "LegendaryWeapons_Bolt";
        private const float MaxWaitForLanding = 3f;

        private class Call
        {
            public Player Owner;
            public float Start;
            public float Due;
            public int Quality;
            public GameObject Glow;
            public Light Light;
        }

        private static Call s_call;
        private static ZRoutedRpc s_rpcInstance;
        private static float s_cooldownUntil = -1f;
        private static SE_Stats s_rest;

        /// <summary>The cooldown after a strike, shown discreetly as a status icon with its timer.</summary>
        public static void Register(Sprite icon)
        {
            s_rest = ScriptableObject.CreateInstance<SE_Stats>();
            s_rest.name = "SE_LW_StormRest";
            s_rest.m_name = "$se_spearthunder_rest";
            s_rest.m_tooltip = "$se_spearthunder_rest_tooltip";
            s_rest.m_icon = icon;
            s_rest.m_ttl = Plugin.CallCooldown.Value;
            Jotunn.Managers.ItemManager.Instance.AddStatusEffect(new Jotunn.Entities.CustomStatusEffect(s_rest, false));
        }

        /// <summary>The storm can be called again (CallCooldown seconds after the last strike).</summary>
        public static bool Ready => Time.time >= s_cooldownUntil;

        internal static void ResetCooldown() => s_cooldownUntil = -1f;

        private static void StartCooldown(Player p)
        {
            s_cooldownUntil = Time.time + Plugin.CallCooldown.Value;
            if (s_rest != null && p != null)
            {
                s_rest.m_ttl = Plugin.CallCooldown.Value;
                p.GetSEMan().AddStatusEffect(s_rest, true);
            }
        }

        public static bool IsPending(Character owner)
        {
            return s_call != null && s_call.Owner == owner;
        }

        public static void TryStart(Player p)
        {
            var weapon = p.GetCurrentWeapon();
            if (weapon == null || weapon.m_shared.m_name != Plugin.SpearToken)
                return;
            if (s_call != null || p.IsDead() || !Ready)
                return;
            var call = new Call
            {
                Owner = p,
                Start = Time.time,
                Due = Time.time + Plugin.CallDelay.Value,
                Quality = weapon.m_quality,
            };
            call.Glow = new GameObject("thunderspear_glow");
            call.Light = call.Glow.AddComponent<Light>();
            call.Light.type = LightType.Point;
            call.Light.color = new Color(0.55f, 0.75f, 1f);
            call.Light.range = 4f;
            call.Light.intensity = 0f;
            call.Glow.transform.position = SpearPosition(p);
            s_call = call;

            var vis = p.GetComponent<VisEquipment>();
            Sfx.Play(Sfx.Charge, SpearPosition(p), vis != null ? vis.m_rightHand : p.transform);
            p.Message(MessageHud.MessageType.Center, "$msg_thunderspear_called");
            Plugin.Log.LogDebug("Lightning called, due in " + Plugin.CallDelay.Value + " s");
        }

        /// <summary>Every frame, from the plugin.</summary>
        public static void Tick()
        {
            var call = s_call;
            if (call == null)
                return;
            if (call.Owner == null || call.Owner.IsDead())
            {
                End();
                return;
            }

            var spear = ThunderSpearProjectile.Get(call.Owner);
            Vector3 spearPos = spear != null ? spear.transform.position : SpearPosition(call.Owner);
            float progress = Mathf.Clamp01((Time.time - call.Start) / Mathf.Max(0.1f, call.Due - call.Start));
            call.Glow.transform.position = spearPos;
            call.Light.intensity = (0.4f + 3.6f * progress * progress) * Random.Range(0.4f, 1.2f);
            call.Light.range = 3f + 5f * progress;

            if (Time.time < call.Due)
                return;
            if (spear == null)
            {
                // Never thrown: the bolt finds the spear in the thrower's hand.
                Strike(call, SpearPosition(call.Owner), true);
                StartCooldown(call.Owner);
            }
            else if (spear.IsFlying && Time.time < call.Due + MaxWaitForLanding)
            {
                return; // let it land first
            }
            else
            {
                Strike(call, spear.transform.position, false);
                spear.OnStruck();
                StartCooldown(call.Owner);
            }
            End();
        }

        private static void End()
        {
            if (s_call != null && s_call.Glow != null)
                Object.Destroy(s_call.Glow);
            s_call = null;
        }

        private static Vector3 SpearPosition(Character c)
        {
            var vis = c.GetComponent<VisEquipment>();
            return vis != null && vis.m_rightHand != null ? vis.m_rightHand.position : c.GetTopPoint();
        }

        private static void Strike(Call call, Vector3 point, bool onThrower)
        {
            Broadcast(point);

            float damage = Plugin.StrikeDamage.Value + Plugin.StrikeDamagePerLevel.Value * Mathf.Max(0, call.Quality - 1);
            var done = new HashSet<Character>();
            foreach (var col in Physics.OverlapSphere(point, Plugin.StrikeRadius.Value, Geometry.CharacterMask,
                         QueryTriggerInteraction.Collide))
            {
                var go = Projectile.FindHitObject(col);
                var c = go != null ? go.GetComponent<Character>() : null;
                if (c == null || done.Contains(c) || c.IsDead())
                    continue;
                bool isOwner = c == call.Owner;
                if (isOwner ? !onThrower : !Geometry.CanHit(call.Owner, c))
                    continue;
                done.Add(c);

                // full damage within 1 m of the impact, then falling off linearly to StrikeEdgeDamage at the edge
                // (distance to the nearest point of the body, so a big creature at the edge still counts as close)
                bool convex = !(col is MeshCollider mc) || mc.convex;     // ClosestPoint needs a convex collider
                float dist = Vector3.Distance(convex ? col.ClosestPoint(point) : c.GetCenterPoint(), point);
                float u = Mathf.Clamp01((dist - 1f) / Mathf.Max(0.1f, Plugin.StrikeRadius.Value - 1f));
                float share = Mathf.Lerp(1f, Plugin.StrikeEdgeDamage.Value, u);
                var hit = new HitData();
                hit.m_damage.m_lightning = damage * share;
                hit.m_point = c.GetCenterPoint();
                Vector3 away = Vector3.ProjectOnPlane(c.transform.position - point, Vector3.up);
                hit.m_dir = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.down;
                hit.m_pushForce = Plugin.StrikePush.Value * share;
                hit.m_dodgeable = false;
                hit.m_blockable = false;
                hit.m_ranged = true;
                if (onThrower)
                {
                    // Nature's own bolt: no attacker, so it reaches the thrower even without PvP.
                    hit.m_hitType = HitData.HitType.Undefined;
                }
                else
                {
                    hit.SetAttacker(call.Owner);
                    hit.m_hitType = HitData.HitType.PlayerHit;
                    hit.m_skill = Skills.SkillType.Spears;
                }
                c.Damage(hit);
                Plugin.Log.LogDebug("  " + c.name + " at " + dist.ToString("F1") + " m: " + (damage * share).ToString("F0") + " lightning");
            }

            Plugin.Log.LogInfo("Lightning strike " + (onThrower ? "on the thrower" : "on the spear") + ": " +
                               done.Count + " hit, " + damage + " lightning at the centre, " +
                               (damage * Plugin.StrikeEdgeDamage.Value) + " at " + Plugin.StrikeRadius.Value + " m");
            if (onThrower)
                call.Owner.Message(MessageHud.MessageType.Center, "$msg_thunderspear_self");
        }

        // ------------------------------------------------------------------ the bolt, seen by everyone

        public static void RegisterRpc()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == s_rpcInstance)
                return;
            ZRoutedRpc.instance.Register<Vector3>(RpcName, (sender, pos) => LightningBolt.Spawn(pos));
            s_rpcInstance = ZRoutedRpc.instance;
        }

        private static void Broadcast(Vector3 point)
        {
            if (ZRoutedRpc.instance != null && ZRoutedRpc.instance == s_rpcInstance)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, point);
            else
                LightningBolt.Spawn(point);
        }
    }

    /// <summary>
    /// A lightning bolt: a jagged, flickering line from high in the sky down to the point, with side branches,
    /// a wide faint glow, a flash of light, a burst of sparks and a thunderclap.
    /// </summary>
    internal class LightningBolt : MonoBehaviour
    {
        private const float Lifetime = 0.55f;
        private const int Segments = 28;
        private static Material s_material;

        private Vector3 _top, _bottom;
        private LineRenderer _core, _glow;
        private readonly List<LineRenderer> _branches = new List<LineRenderer>();
        private Light _flash;
        private float _age;
        private float _nextReshape;

        public static void Spawn(Vector3 point)
        {
            var go = new GameObject("thunderspear_bolt");
            go.transform.position = point;
            var bolt = go.AddComponent<LightningBolt>();
            bolt.Build(point);
            Sfx.Play(Sfx.Strike, point, null);
            Sparks(point);
            StrikeArea.Spawn(point, Plugin.StrikeRadius.Value, BoltMaterial());
            Destroy(go, Lifetime);
        }

        private static Material BoltMaterial()
        {
            if (s_material == null)
            {
                var shader = Shader.Find("Sprites/Default");
                s_material = new Material(shader);
            }
            return s_material;
        }

        private LineRenderer Line(string name, float width, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = BoltMaterial();
            lr.useWorldSpace = true;
            lr.startWidth = width;
            lr.endWidth = width * 0.6f;
            lr.startColor = lr.endColor = color;
            lr.numCapVertices = 2;
            lr.numCornerVertices = 1;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private void Build(Vector3 point)
        {
            _bottom = point;
            Vector2 drift = Random.insideUnitCircle * 10f;
            _top = point + new Vector3(drift.x, Plugin.BoltHeight.Value, drift.y);
            _glow = Line("glow", 1.6f, new Color(0.45f, 0.6f, 1f, 0.25f));
            _core = Line("core", 0.3f, new Color(0.92f, 0.96f, 1f, 1f));
            for (int i = 0; i < 4; i++)
                _branches.Add(Line("branch", 0.12f, new Color(0.8f, 0.88f, 1f, 0.9f)));

            var flashGo = new GameObject("flash");
            flashGo.transform.SetParent(transform, false);
            flashGo.transform.position = point + Vector3.up * 2f;
            _flash = flashGo.AddComponent<Light>();
            _flash.type = LightType.Point;
            _flash.color = new Color(0.75f, 0.85f, 1f);
            _flash.range = 35f;
            _flash.intensity = 8f;
            Reshape();
        }

        private void Reshape()
        {
            var pts = Jagged(_top, _bottom, Segments, 3.5f);
            _core.positionCount = pts.Length;
            _core.SetPositions(pts);
            _glow.positionCount = pts.Length;
            _glow.SetPositions(pts);
            foreach (var b in _branches)
            {
                int from = Random.Range(3, Segments - 6);
                Vector3 start = pts[from];
                Vector2 side = Random.insideUnitCircle.normalized * Random.Range(3f, 9f);
                Vector3 end = start + new Vector3(side.x, -Random.Range(4f, 12f), side.y);
                var bp = Jagged(start, end, 8, 1.2f);
                b.positionCount = bp.Length;
                b.SetPositions(bp);
            }
        }

        /// <summary>Points from a to b with random sideways kinks, smaller near the ends.</summary>
        private static Vector3[] Jagged(Vector3 a, Vector3 b, int segments, float amplitude)
        {
            var pts = new Vector3[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                float t = (float)i / segments;
                Vector3 p = Vector3.Lerp(a, b, t);
                if (i > 0 && i < segments)
                {
                    float taper = Mathf.Sin(t * Mathf.PI);
                    Vector2 k = Random.insideUnitCircle * amplitude * (0.4f + 0.6f * taper);
                    p += new Vector3(k.x, 0f, k.y);
                }
                pts[i] = p;
            }
            return pts;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = _age / Lifetime;
            // Flicker: a few on/off pulses, reshaped each time, fading out.
            bool on = (int)(_age / 0.05f) % 3 != 2;
            float fade = 1f - t;
            if (Time.time >= _nextReshape)
            {
                _nextReshape = Time.time + 0.07f;
                Reshape();
            }
            _core.enabled = _glow.enabled = on;
            foreach (var b in _branches)
                b.enabled = on && t < 0.6f;
            var c = _core.startColor;
            c.a = fade;
            _core.startColor = _core.endColor = c;
            _flash.intensity = (on ? 8f : 3f) * fade * fade;
        }

        private static void Sparks(Vector3 point)
        {
            var go = new GameObject("thunderspear_sparks");
            go.transform.position = point;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f); // the hemisphere opens along +Z: point it up
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.2f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 14f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.9f, 1f), new Color(1f, 1f, 1f));
            main.gravityModifier = 1.5f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 200;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.3f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Fx.Soft;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.04f;
            r.lengthScale = 1f;
            ps.Play();
            Object.Destroy(go, 1.5f);
        }
    }
}
