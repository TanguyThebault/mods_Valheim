using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// The Fog Horn (Mistlands): an item you blow into, no weapon.
    /// - Primary attack: a plain blow that lasts as long as the button is held (HoldMax s at most); a short press of
    ///   the secondary: a short blow. Any time.
    /// - Hold the secondary attack HoldTime: the call. CallCount spirit animals picked at random from Animals rise from
    ///   the mist around you, tamed (on your side) but free, hunting every enemy around for Duration seconds, then
    ///   they dissolve. Once
    ///   every Cooldown seconds ("Silent mist" status with its timer); before that, the hold is a plain blow.
    /// While blowing, the right hand brings the horn to the mouth (arm IK after the animator, every client) and the
    /// player can still walk. Spirits: tamed, no drops, no corpse, their end time in their ZDO (so they also vanish
    /// after a reload), a translucent cold glow, a trail of mist; every client dresses them (Character.Awake).
    /// They are aggressive: their owner keeps them hunting the nearest enemy within HuntRange.
    /// </summary>
    internal static class FogHorn
    {
        private const string RpcName = "LegendaryWeapons_FogHorn";
        private const string HoldRpc = "LegendaryWeapons_FogHornHold";
        internal const string BlowUntil = "lw_horn_until", BlowStart = "lw_horn_start", BlowCall = "lw_horn_call",
            GhostUntil = "lw_ghost_until", GhostSpan = "lw_ghost_span";
        private static float s_readyAt = -1f;
        private static SE_Stats s_rest;
        private static ZRoutedRpc s_rpc;
        internal static GameObject CallSfx, ShortSfx, RiseSfx, FadeSfx, ReleaseSfx;
        internal static AudioClip HoldClip;
        private static float s_holdSince = -1f;
        private static readonly Dictionary<long, AudioSource> s_holdSources = new Dictionary<long, AudioSource>();

        internal static bool Ready => Time.time >= s_readyAt;
        internal static void ResetCooldown() => s_readyAt = -1f;

        public static void Register(ItemDrop.ItemData.SharedData shared, Sprite icon)
        {
            s_rest = ScriptableObject.CreateInstance<SE_Stats>();
            s_rest.name = "SE_LW_HornRest";
            s_rest.m_name = "$se_hornfog_rest";
            s_rest.m_tooltip = "$se_hornfog_rest_tooltip";
            s_rest.m_icon = icon;
            s_rest.m_ttl = Plugin.HornCooldown.Value;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(s_rest, false));
            foreach (var name in Animals())
            {
                try
                {
                    var sr = AssetManager.Instance.GetSoftReference<GameObject>(name);   // materials in other bundles
                    if (sr.IsValid) sr.Load();
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogDebug(name + ": no soft reference (" + e.Message + ")");
                }
            }
        }

        internal static IEnumerable<string> Animals() =>
            Plugin.HornAnimals.Value.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0);

        public static void RegisterRpc()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == s_rpc)
                return;
            ZRoutedRpc.instance.Register<Vector3, bool>(RpcName, (sender, pos, call) => Sfx.Play(call ? CallSfx : ShortSfx, pos, null));
            ZRoutedRpc.instance.Register<bool>(HoldRpc, (sender, on) => HoldSound(sender, on));
            s_rpc = ZRoutedRpc.instance;
        }

        private static double Now => ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : Time.time;

        /// <summary>Test (lw_hornpose): keep the horn at the mouth until called again.</summary>
        internal static void TogglePose(Player p)
        {
            var zdo = p.GetComponent<ZNetView>().GetZDO();
            bool on = zdo.GetFloat(BlowUntil) < (float)Now + 1000f;
            zdo.Set(BlowCall, 0f);
            zdo.Set(BlowStart, (float)Now - 1f);
            zdo.Set(BlowUntil, on ? (float)Now + 1e6f : (float)Now);
        }

        internal static bool Blowing(Player p, out float weight, out bool call)
        {
            weight = 0f;
            call = false;
            var nview = p != null ? p.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid())
                return false;
            var zdo = nview.GetZDO();
            float until = zdo.GetFloat(BlowUntil);
            if (until <= 0f)
                return false;
            call = zdo.GetFloat(BlowCall) > 0.5f;
            float left = until - (float)Now, since = (float)Now - zdo.GetFloat(BlowStart);
            if (left < -0.3f || since < 0f)
                return false;
            weight = Mathf.Clamp01(since / 0.3f) * Mathf.Clamp01((left + 0.3f) / 0.3f);
            return weight > 0f;
        }

        internal static bool BlowingLocal
        {
            get
            {
                var p = Player.m_localPlayer;
                return p != null && Blowing(p, out var w, out _) && w > 0f;
            }
        }

        /// <summary>Blow into the horn; `call` (hold, ready) also summons the spirits.</summary>
        internal static void Blow(Player p, bool call)
        {
            if (p == null || BlowingLocal || p.IsDead() || p.IsSwimming())
                return;
            var zdo = p.GetComponent<ZNetView>().GetZDO();
            zdo.Set(BlowCall, call ? 1f : 0f);
            zdo.Set(BlowStart, (float)Now);
            zdo.Set(BlowUntil, (float)Now + (call ? Plugin.HornCallTime : Plugin.HornShortTime));
            var pos = p.GetEyePoint();
            if (ZRoutedRpc.instance != null && ZRoutedRpc.instance == s_rpc)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, pos, call);
            else
                Sfx.Play(call ? CallSfx : ShortSfx, pos, null);
            if (!call)
                return;
            s_readyAt = Time.time + Plugin.HornCooldown.Value;
            s_rest.m_ttl = Plugin.HornCooldown.Value;
            p.GetSEMan().AddStatusEffect(s_rest, true);
            p.Message(MessageHud.MessageType.Center, "$msg_hornfog_call");
            Plugin.Instance.StartCoroutine(Summon(p));
        }

        internal static bool HoldingLocal => s_holdSince >= 0f;

        /// <summary>The held blow (primary attack): starts, keeps the horn at the mouth while held, stops at HoldMax.</summary>
        internal static void HoldTick(Player p, bool held)
        {
            if (p == null)
                return;
            var zdo = p.GetComponent<ZNetView>().GetZDO();
            if (held && !HoldingLocal)
            {
                if (BlowingLocal || p.IsDead() || p.IsSwimming() || HoldClip == null)
                    return;
                s_holdSince = Time.time;
                zdo.Set(BlowCall, 0f);
                zdo.Set(BlowStart, (float)Now);
                Broadcast(true);
            }
            if (!HoldingLocal)
                return;
            bool over = !held || Time.time - s_holdSince >= Plugin.HornHoldMax.Value || p.IsDead() || p.IsSwimming();
            if (over)
            {
                s_holdSince = -1f;
                zdo.Set(BlowUntil, (float)Now + 0.35f);              // the horn comes down while the note dies
                Broadcast(false);
                return;
            }
            zdo.Set(BlowUntil, (float)Now + 0.5f);
        }

        private static void Broadcast(bool on)
        {
            if (ZRoutedRpc.instance != null && ZRoutedRpc.instance == s_rpc)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, HoldRpc, on);
            else
                HoldSound(ZNet.GetUID(), on);
        }

        /// <summary>On every client: the long note on the blower, cut short with its release and echoes.</summary>
        private static void HoldSound(long sender, bool on)
        {
            Player who = null;
            foreach (var pl in Player.GetAllPlayers())
            {
                var nv = pl != null ? pl.GetComponent<ZNetView>() : null;
                if (nv != null && nv.IsValid() && nv.GetZDO().GetOwner() == sender)
                    who = pl;
            }
            if (s_holdSources.TryGetValue(sender, out var src) && src != null)
            {
                Plugin.Instance.StartCoroutine(FadeOut(src, 0.12f));
                s_holdSources.Remove(sender);
            }
            if (who == null)
                return;
            if (!on)
            {
                Sfx.Play(ReleaseSfx, who.GetEyePoint(), null);
                return;
            }
            var go = new GameObject("lw_horn_hold");
            go.transform.SetParent(who.transform, false);
            go.transform.localPosition = Vector3.up * 1.6f;
            var a = go.AddComponent<AudioSource>();
            a.clip = HoldClip;
            a.spatialBlend = 1f;
            a.rolloffMode = AudioRolloffMode.Linear;
            a.minDistance = 25f;
            a.maxDistance = 350f;
            a.dopplerLevel = 0f;
            a.bypassReverbZones = true;
            var mix = ShortSfx != null ? ShortSfx.GetComponent<AudioSource>() : null;
            if (mix != null) a.outputAudioMixerGroup = mix.outputAudioMixerGroup;
            a.Play();
            s_holdSources[sender] = a;
        }

        private static IEnumerator FadeOut(AudioSource a, float time)
        {
            float v = a.volume;
            for (float t = 0f; t < time && a != null; t += Time.deltaTime)
            {
                a.volume = v * (1f - t / time);
                yield return null;
            }
            if (a != null) Object.Destroy(a.gameObject);
        }

        private static IEnumerator Summon(Player p)
        {
            yield return new WaitForSeconds(0.9f);
            var pool = Animals().Select(n => ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(n) : null)
                                .Where(g => g != null && g.GetComponent<Character>() != null && g.GetComponent<MonsterAI>() != null)
                                .ToList();
            if (pool.Count == 0)
            {
                Plugin.Log.LogWarning("Fog Horn: none of the animals (" + Plugin.HornAnimals.Value + ") exists");
                yield break;
            }
            // distinct animals while the list allows, then repeats
            var picks = pool.OrderBy(_ => Random.value).ToList();
            while (picks.Count < Plugin.HornCount.Value)
                picks.Add(pool[Random.Range(0, pool.Count)]);
            int n = Plugin.HornCount.Value;
            float baseAngle = Random.Range(0f, 360f);
            for (int i = 0; i < n && p != null; i++)
            {
                float a = baseAngle + 360f * i / n + Random.Range(-20f, 20f);
                var pos = p.transform.position + Quaternion.Euler(0f, a, 0f) * Vector3.forward * Random.Range(3f, 4.5f);
                if (Physics.Raycast(pos + Vector3.up * 20f, Vector3.down, out var hit, 60f, LayerMask.GetMask("terrain", "static_solid", "Default", "piece")))
                    pos = hit.point + Vector3.up * 0.1f;
                var rot = Quaternion.LookRotation(Vector3.ProjectOnPlane(pos - p.transform.position, Vector3.up));
                var go = Object.Instantiate(picks[i], pos, rot);
                Ghostify(go, p);
                Plugin.Log.LogInfo("Fog Horn: a spirit " + picks[i].name + " rises at " + pos.ToString("F1"));
                yield return new WaitForSeconds(0.35f);
            }
        }

        private static void Ghostify(GameObject go, Player p)
        {
            var nview = go.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid())
            {
                nview.GetZDO().Set(GhostUntil, (float)Now + Plugin.HornDuration.Value);
                nview.GetZDO().Set(GhostSpan, Plugin.HornDuration.Value);
            }
            var c = go.GetComponent<Character>();
            c.SetLevel(Mathf.Clamp(Plugin.HornLevel.Value, 1, 4));
            c.SetTamed(true);                                          // on the player's side, but they don't follow
            if (go.GetComponent<SpiritLook>() == null)
                go.AddComponent<SpiritLook>();
        }

        internal static bool IsSpirit(ZNetView nview) =>
            nview != null && nview.IsValid() && nview.GetZDO().GetFloat(GhostUntil) > 0f;
    }

    /// <summary>The horn's controls (local player, horn in hand): blows instead of attacks.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class FogHornControls
    {
        private static float s_holdStart = -1f;
        private static bool s_waitRelease;
        private static ChargeFx s_charge;

        private static void Prefix(Player __instance, ref bool attack, ref bool attackHold, ref bool secondaryAttack, ref bool secondaryAttackHold)
        {
            if (__instance != Player.m_localPlayer)
                return;
            var w = __instance.GetCurrentWeapon();
            if (w == null || w.m_shared.m_name != Plugin.HornToken)
            {
                Reset();
                return;
            }
            bool primary = attack || attackHold, held = secondaryAttack || secondaryAttackHold;
            attack = attackHold = secondaryAttack = secondaryAttackHold = false;       // a horn doesn't strike
            FogHorn.HoldTick(__instance, primary && s_holdStart < 0f);                 // held blow, as long as held
            if (FogHorn.HoldingLocal)
                return;
            if (s_waitRelease)
            {
                if (!held) s_waitRelease = false;
                return;
            }
            if (!held)
            {
                bool shortPress = s_holdStart >= 0f;
                Reset();
                if (shortPress) FogHorn.Blow(__instance, false);
                return;
            }
            if (s_holdStart < 0f)
                s_holdStart = Time.time;
            float hold = Plugin.HornHoldTime.Value;
            if (s_charge == null && FogHorn.Ready && Time.time - s_holdStart >= HoldPower.ChargeDelay)
                s_charge = ChargeFx.Begin(__instance, ChargeFx.Theme.Wind, Mathf.Max(0.05f, hold - HoldPower.ChargeDelay));
            if (Time.time - s_holdStart < hold)
                return;
            Reset();
            s_waitRelease = true;
            if (!FogHorn.Ready)
                __instance.Message(MessageHud.MessageType.TopLeft, "$msg_hornfog_notyet");
            FogHorn.Blow(__instance, FogHorn.Ready);
        }

        private static void Reset()
        {
            s_holdStart = -1f;
            if (s_charge != null)
            {
                s_charge.Stop();
                s_charge = null;
            }
        }
    }

    /// <summary>
    /// The horn to the mouth, on every client: after the animator, a two-bone IK brings the right wrist so that the
    /// horn's mouthpiece touches the lips, the horn raised forward and up. Mist pours from the bell meanwhile.
    /// </summary>
    [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomLateUpdate))]
    internal static class FogHornPose
    {
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Character> s_character =
            AccessTools.FieldRefAccess<CharacterAnimEvent, Character>("m_character");
        private static readonly AccessTools.FieldRef<Character, Animator> s_animator =
            AccessTools.FieldRefAccess<Character, Animator>("m_animator");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_rightItem =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static readonly Dictionary<Player, HornMist> s_mist = new Dictionary<Player, HornMist>();
        private static readonly Dictionary<Player, Vector3> s_mouth = new Dictionary<Player, Vector3>();

        private static void Postfix(CharacterAnimEvent __instance)
        {
            if (!(s_character(__instance) is Player p))
                return;
            bool on = FogHorn.Blowing(p, out float weight, out bool call);
            var item = s_rightItem(p.GetComponent<VisEquipment>());
            if (!on || item == null)
            {
                if (s_mist.TryGetValue(p, out var m) && m != null) m.Stop();
                s_mist.Remove(p);
                s_mouth.Remove(p);
                return;
            }
            var anim = s_animator(p);
            if (anim == null || !anim.isHuman)
                return;
            var head = anim.GetBoneTransform(HumanBodyBones.Head);
            var upper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lower = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (head == null || upper == null || lower == null || hand == null)
                return;
            var t = p.transform;
            // the lips, in the body's frame, smoothed: the head's little animation moves don't shake the horn
            var mouthLocal = t.InverseTransformPoint(head.position + t.forward * 0.11f - t.up * Plugin.HornMouthDrop.Value);
            if (s_mouth.TryGetValue(p, out var prev))
                mouthLocal = Vector3.Lerp(prev, mouthLocal, 1f - Mathf.Exp(-Time.deltaTime * 4f));
            s_mouth[p] = mouthLocal;
            var mouth = t.TransformPoint(mouthLocal);
            var dir = (t.forward + t.up * 0.45f).normalized;                     // the horn raised forward and up
            var headDir = WeaponModels.Heads.TryGetValue(Plugin.HornPrefab, out var hd) ? hd : Vector3.up;
            var u0 = upper.rotation;
            var l0 = lower.rotation;
            var h0 = hand.rotation;
            var pole = upper.position + t.right * 0.3f - t.up * 0.4f - t.forward * 0.1f;   // elbow out and down

            // The hand's rotation is absolute, built from the body only (no roll inherited from the animation, which
            // made the horn shake): the horn's bell axis along `dir`, its side kept level, then [FogHorn] Roll.
            // The item hangs from the hand at a fixed offset and turn (its attach), measured in the hand's frame.
            var itemLocal = Quaternion.Inverse(hand.rotation) * item.transform.rotation;
            var offsetLocal = Quaternion.Inverse(hand.rotation) * (item.transform.position - hand.position);
            var side = Vector3.Cross(headDir, Mathf.Abs(Vector3.Dot(headDir, Vector3.right)) > 0.9f ? Vector3.up : Vector3.right).normalized;
            var up = Quaternion.AngleAxis(Plugin.HornRoll.Value, dir) * Vector3.ProjectOnPlane(t.up, dir).normalized;
            var itemRot = Quaternion.LookRotation(dir, up) * Quaternion.Inverse(Quaternion.LookRotation(headDir, side));
            var handRot = itemRot * Quaternion.Inverse(itemLocal);
            // the mouthpiece lies MouthpieceOffset behind the item's origin, along the horn: that point goes on the lips
            var wrist = mouth + dir * Plugin.HornMouthpiece.Value - handRot * offsetLocal;
            SolveTwoBone(upper, lower, hand, wrist, pole);
            hand.rotation = handRot;
            if (weight < 0.999f)
            {
                upper.rotation = Quaternion.Slerp(u0, upper.rotation, weight);
                lower.rotation = Quaternion.Slerp(l0, lower.rotation, weight);
                hand.rotation = Quaternion.Slerp(h0, hand.rotation, weight);
            }

            if (!s_mist.TryGetValue(p, out var mist) || mist == null)
                s_mist[p] = mist = HornMist.Start(call);
            mist.Place(item.transform.position + dir * Plugin.HornLength.Value, dir, weight);
        }

        private static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
        {
            float lab = Vector3.Distance(a.position, b.position);
            float lbc = Vector3.Distance(b.position, c.position);
            var toT = target - a.position;
            float dist = Mathf.Clamp(toT.magnitude, Mathf.Abs(lab - lbc) + 1e-3f, lab + lbc - 1e-3f);
            var d = toT.normalized;
            float x = (lab * lab - lbc * lbc + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, lab * lab - x * x));
            var bend = Vector3.ProjectOnPlane(pole - a.position, d);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(b.position - a.position, d);
            bend.Normalize();
            var elbow = a.position + d * x + bend * h;
            a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
            b.rotation = Quaternion.FromToRotation(c.position - b.position, a.position + d * dist - b.position) * b.rotation;
        }
    }

    /// <summary>Mist pouring from the horn's bell while it sounds (local look, every client).</summary>
    internal class HornMist : MonoBehaviour
    {
        private ParticleSystem _ps;

        internal static HornMist Start(bool call)
        {
            var go = new GameObject("lw_horn_mist");
            var m = go.AddComponent<HornMist>();
            var ps = m._ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, call ? 2.6f : 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.8f, call ? 2.6f : 1.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.86f, 0.95f, 0.22f), new Color(0.9f, 0.95f, 1f, 0.12f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.02f;
            main.maxParticles = 300;
            var em = ps.emission;
            em.rateOverTime = call ? 45f : 22f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Cone;
            sh.angle = 14f;
            sh.radius = 0.06f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(1f, 3f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Fx.Soft;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return m;
        }

        internal void Place(Vector3 pos, Vector3 dir, float weight)
        {
            transform.position = pos;
            transform.rotation = Quaternion.LookRotation(dir);
            var em = _ps.emission;
            em.enabled = weight > 0.6f;
        }

        internal void Stop()
        {
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            Destroy(gameObject, 3f);
        }
    }

    /// <summary>
    /// A spirit animal's look and life, on every client: a translucent cold glow (unlit, see-through, keeping the
    /// creature's own texture), no shadow, a trail of mist and a faint light; rising from mist when it appears,
    /// dissolving at the end of its time. The owner removes it then. No drops, no corpse.
    /// </summary>
    internal class SpiritLook : MonoBehaviour
    {
        private static readonly Color Tint = new Color(0.42f, 0.56f, 0.76f, 1f);   // not too bright: daylight bloom made them glare
        private ZNetView _nview;
        private readonly List<Material> _mats = new List<Material>();
        private Light _light;
        private ParticleSystem _trail;
        private bool _fadeStarted, _gone;
        private float _scale = 1f, _nextHunt;
        internal static readonly HashSet<Character> All = new HashSet<Character>();
        private static readonly AccessTools.FieldRef<MonsterAI, Character> s_target = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");

        private void Start()
        {
            _nview = GetComponent<ZNetView>();
            var c = GetComponent<Character>();
            if (c != null) All.Add(c);
            if (c != null)
            {
                c.m_deathEffects = new EffectList();                   // no ragdoll, no blood: it just dissolves
            }
            // no drops, no breeding: switched off, not destroyed (CharacterDrop is subscribed to the death event: a
            // destroyed one made OnDeath throw before the creature was removed, leaving it alive at 0 health)
            var drop = GetComponent<CharacterDrop>();
            if (drop != null)
            {
                drop.SetDropsEnabled(false);
                drop.m_drops.Clear();
            }
            var procreation = GetComponent<Procreation>();
            if (procreation != null) procreation.enabled = false;
            var bounds = new Bounds(transform.position, Vector3.zero);
            var shader = Shader.Find("Sprites/Default");
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer || r is LineRenderer)
                    continue;
                bounds.Encapsulate(r.bounds);
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = new Material(shader) { name = "lw_spirit" };
                    if (mats[i] != null && mats[i].HasProperty("_MainTex")) m.mainTexture = mats[i].mainTexture;
                    m.color = new Color(Tint.r, Tint.g, Tint.b, 0f);
                    mats[i] = m;
                    _mats.Add(m);
                }
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            _scale = Mathf.Clamp(bounds.size.magnitude / 2f, 0.6f, 4f);
            var lgo = new GameObject("lw_spirit_light");
            lgo.transform.SetParent(transform, false);
            lgo.transform.localPosition = Vector3.up * bounds.extents.y;
            _light = lgo.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = Tint;
            _light.range = 2f * _scale;
            _light.intensity = 0f;
            _trail = MakeTrail();
            Burst(transform.position + Vector3.up * bounds.extents.y, 40);
            Sfx.Play(FogHorn.RiseSfx, transform.position, null);
        }

        private void OnDestroy() => All.Remove(GetComponent<Character>());

        private ParticleSystem MakeTrail()
        {
            var go = new GameObject("lw_spirit_trail");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.4f * _scale, 0.9f * _scale);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.7f, 0.85f, 1f, 0.18f), new Color(0.85f, 0.92f, 1f, 0.1f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.03f;
            main.maxParticles = 200;
            var em = ps.emission;
            em.rateOverTime = 16f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.5f * _scale;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Fx.Soft;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.localPosition = Vector3.up * 0.5f * _scale;
            ps.Play();
            return ps;
        }

        /// <summary>A puff of mist swirling up (appearing) or out (dissolving).</summary>
        private void Burst(Vector3 at, int count)
        {
            var go = new GameObject("lw_spirit_burst");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f * _scale, 1.2f * _scale);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.88f, 1f, 0.35f), new Color(0.9f, 0.95f, 1f, 0.2f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = -0.05f;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.6f * _scale;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.6f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Fx.Soft;
            ps.Play();
            Destroy(go, 3f);
        }

        private void Update()
        {
            if (_gone || _nview == null || !_nview.IsValid())
                return;
            var zdo = _nview.GetZDO();
            float until = zdo.GetFloat(FogHorn.GhostUntil), span = zdo.GetFloat(FogHorn.GhostSpan, 30f);
            float now = ZNet.instance != null ? (float)ZNet.instance.GetTimeSeconds() : Time.time;
            float left = until - now, age = span - left;
            float alpha = Plugin.HornAlpha.Value * Mathf.Clamp01(age / 1.2f) * Mathf.Clamp01(left / 1.5f);
            alpha *= 0.85f + 0.15f * Mathf.Sin(Time.time * 2.3f + transform.position.x);      // a slow shimmer
            foreach (var m in _mats)
                m.color = new Color(Tint.r, Tint.g, Tint.b, alpha);
            _light.intensity = 0.45f * alpha / Mathf.Max(0.01f, Plugin.HornAlpha.Value);
            if (!_fadeStarted && left <= 1.5f)
            {
                _fadeStarted = true;
                Burst(transform.position + Vector3.up * 0.6f * _scale, 30);
                Sfx.Play(FogHorn.FadeSfx, transform.position, null);
                var em = _trail.emission;
                em.enabled = false;
            }
            if (left <= 0f && _nview.IsOwner())
            {
                _gone = true;
                ZNetScene.instance.Destroy(gameObject);
                return;
            }
            if (_nview.IsOwner() && Time.time >= _nextHunt)
            {
                _nextHunt = Time.time + 0.25f;
                Hunt();
            }
        }

        /// <summary>Aggressive: no waiting to be attacked, they go for the nearest enemy in range.</summary>
        private void Hunt()
        {
            var me = GetComponent<Character>();
            var ai = GetComponent<MonsterAI>();
            if (me == null || ai == null || me.IsDead())
                return;
            float range = Plugin.HornAggro.Value;
            // nothing timid left in them: never passive, never fleeing, not afraid of fire
            ai.m_viewRange = Mathf.Max(ai.m_viewRange, range);
            ai.m_alertRange = Mathf.Max(ai.m_alertRange, range);
            ai.m_passiveAggresive = false;
            ai.m_fleeIfHurtWhenTargetCantBeReached = false;
            ai.m_fleeIfNotAlerted = false;
            ai.m_fleeIfLowHealth = 0f;
            ai.m_afraidOfFire = false;
            ai.m_avoidFire = false;
            var current = s_target(ai);
            if (current != null && !current.IsDead() && Vector3.Distance(current.transform.position, transform.position) < range * 1.3f)
                return;
            Character best = null;
            float bestDist = range;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c == me || c.IsDead() || !BaseAI.IsEnemy(me, c))
                    continue;
                float d = Vector3.Distance(c.transform.position, transform.position);
                if (d < bestDist)
                {
                    bestDist = d;
                    best = c;
                }
            }
            if (best == null)
                return;
            s_target(ai) = best;
            Traverse.Create(ai).Method("SetAlerted", true).GetValue();
        }
    }

    /// <summary>
    /// The game never makes two creatures of the same group (a herd of lox) enemies, tamed or not: a spirit is the enemy
    /// of every wild creature, its own kind included (and they fight back). Spirits never fight each other or players.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character), typeof(Character) })]
    internal static class SpiritEnemies
    {
        private static void Postfix(Character a, Character b, ref bool __result)
        {
            if (__result || a == null || b == null || a == b || SpiritLook.All.Count == 0)
                return;
            bool sa = SpiritLook.All.Contains(a), sb = SpiritLook.All.Contains(b);
            if (sa == sb)
                return;
            var other = sa ? b : a;
            if (other.IsPlayer() || other.IsTamed() || other.GetFaction() == Character.Faction.Players)
                return;
            __result = true;
        }
    }

    /// <summary>The game's enemy HUD shows stars for levels 2 and 3 only: a level-4 spirit gets three.</summary>
    /// <summary>
    /// The spirits' stars are pink (a third star doesn't exist in the game: Lekinox prefers marking them): the
    /// vanilla one- or two-star icons of their HUD, recoloured once; a level above 3 shows as two pink stars.
    /// </summary>
    [HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
    internal static class PinkStars
    {
        private static readonly Color Pink = new Color(1f, 0.45f, 0.82f, 1f);

        private static void Postfix(EnemyHud __instance)
        {
            if (SpiritLook.All.Count == 0)
                return;
            var huds = Traverse.Create(__instance).Field("m_huds").GetValue() as System.Collections.IDictionary;
            if (huds == null)
                return;
            foreach (var c in SpiritLook.All)
            {
                if (c == null || c.GetLevel() < 2 || !huds.Contains(c))
                    continue;
                var data = huds[c];
                var level2 = Traverse.Create(data).Field("m_level2").GetValue() as RectTransform;
                var level3 = Traverse.Create(data).Field("m_level3").GetValue() as RectTransform;
                if (c.GetLevel() > 3 && level3 != null)
                    level3.gameObject.SetActive(true);
                foreach (var stars in new[] { level2, level3 })
                {
                    if (stars == null || stars.Find("lw_pink") != null)
                        continue;
                    foreach (var g in stars.GetComponentsInChildren<UnityEngine.UI.Graphic>(true))
                        g.color = Pink;
                    new GameObject("lw_pink").transform.SetParent(stars, false);      // done marker
                }
            }
        }
    }

    /// <summary>The spirits hit harder ([FogHorn] DamageMultiplier).</summary>
    [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
    internal static class SpiritDamage
    {
        private static void Prefix(HitData hit)
        {
            var attacker = hit?.GetAttacker();
            if (attacker != null && attacker.GetComponent<SpiritLook>() != null)
                hit.m_damage.Modify(Plugin.HornDamage.Value);
        }
    }

    /// <summary>Spirits are dressed on every client as they appear (their end time is in their ZDO).</summary>
    [HarmonyPatch(typeof(Character), "Awake")]
    internal static class SpiritAwake
    {
        private static void Postfix(Character __instance)
        {
            var nview = __instance.GetComponent<ZNetView>();
            if (FogHorn.IsSpirit(nview) && __instance.GetComponent<SpiritLook>() == null)
                __instance.gameObject.AddComponent<SpiritLook>();
        }
    }
}
