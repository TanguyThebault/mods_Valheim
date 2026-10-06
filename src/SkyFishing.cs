using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// Whether a point is under the open sky: not in a dungeon (dungeons sit above 3000 m), not in a cave
    /// environment, and no rock or ground overhead (surface caves such as troll caves). The sky powers (the Thunder
    /// Spear's lightning, the Skyfisher's Rod) only work there.
    /// </summary>
    internal static class OpenSky
    {
        private static readonly List<string> s_caveEnvs = new List<string> { "Caves", "Crypt", "SunkenCrypt", "FrostCaves", "InfectedMine", "MountainCave" };
        private static int s_roofMask;

        public static bool At(Vector3 p)
        {
            if (Character.InInterior(p))
                return false;
            if (EnvMan.instance != null && EnvMan.instance.IsEnvironment(s_caveEnvs))
                return false;
            if (s_roofMask == 0)
                s_roofMask = LayerMask.GetMask("terrain", "static_solid");
            return !Physics.Raycast(p + Vector3.up * 1.5f, Vector3.up, 80f, s_roofMask, QueryTriggerInteraction.Ignore);
        }

        /// <summary>Checks the player's spot, and says so (discreetly) when the sky can't be reached.</summary>
        public static bool Check(Player p)
        {
            if (At(p.transform.position))
                return true;
            p.Message(MessageHud.MessageType.TopLeft, "$msg_lw_nosky");
            return false;
        }
    }

    /// <summary>
    /// The Skyfisher's Rod (Ocean, fished up). Hold the secondary attack: the rod casts its line straight up into
    /// the sky, something bites, and a few seconds later a giant fish drops out of the sky onto the spot you aim at
    /// and explodes on impact: blunt and fire damage falling off from the centre, creatures thrown up (never
    /// bosses), never buildings, and a few raw fish left on the ground. Only under the open sky, CastCooldown
    /// seconds apart (status icon with timer). The rod still fishes normally with its primary attack.
    /// Found only by fishing in the Ocean: a small chance per catch, growing with each catch that missed it.
    /// </summary>
    internal static class SkyFishing
    {
        public const string RpcName = "LegendaryWeapons_SkyFish";
        private static readonly string[] s_fish = { "Fish1", "Fish2", "Fish5", "Fish6", "Fish7", "Fish8", "Fish9", "Fish12" };
        private static readonly int s_pityKey = "lw_rod_pity".GetStableHashCode();
        private static ZRoutedRpc s_rpcInstance;
        private static float s_cooldownUntil = -1f;
        private static SE_Stats s_rest;
        internal static GameObject Explosion;
        /// <summary>Flipbook materials taken from the vanilla explosion (fire, smoke), with their sheet layout.</summary>
        internal static (Material mat, int x, int y) FireBook, SmokeBook;

        internal static void ResetCooldown() => s_cooldownUntil = -1f;

        public static void Register(ItemDrop.ItemData.SharedData rod)
        {
            foreach (var f in s_fish)
            {
                try
                {
                    var sr = AssetManager.Instance.GetSoftReference<GameObject>(f);   // materials live in other bundles
                    if (sr.IsValid) sr.Load();
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning(f + ": soft reference load failed: " + e.Message);
                }
            }
            foreach (var n in new[] { "fx_siegebomb_explosion", "fx_dynamite_explosion" })
                if ((Explosion = PrefabManager.Cache.GetPrefab<GameObject>(n)) != null)
                    break;
            if (Explosion != null)
                foreach (var ps in Explosion.GetComponentsInChildren<ParticleSystem>(true))
                {
                    var r = ps.GetComponent<ParticleSystemRenderer>();
                    if (r == null || r.sharedMaterial == null)
                        continue;
                    string n = (ps.name + " " + r.sharedMaterial.name).ToLowerInvariant();
                    var tsa = ps.textureSheetAnimation;
                    var entry = (r.sharedMaterial, tsa.enabled ? tsa.numTilesX : 1, tsa.enabled ? tsa.numTilesY : 1);
                    if (FireBook.mat == null && (n.Contains("fire") || n.Contains("flame") || n.Contains("explo"))) FireBook = entry;
                    else if (SmokeBook.mat == null && n.Contains("smoke")) SmokeBook = entry;
                    Plugin.Log.LogDebug("Explosion particles: " + ps.name + " / " + r.sharedMaterial.name + " sheet " + entry.Item2 + "x" + entry.Item3);
                }
            Plugin.Log.LogInfo("Skyfisher's flipbooks: fire " + (FireBook.mat != null ? FireBook.mat.name : "none") + ", smoke " + (SmokeBook.mat != null ? SmokeBook.mat.name : "none"));
            s_rest = ScriptableObject.CreateInstance<SE_Stats>();
            s_rest.name = "SE_LW_SkyRest";
            s_rest.m_name = "$se_rodsky_rest";
            s_rest.m_tooltip = "$se_rodsky_rest_tooltip";
            s_rest.m_icon = rod.m_icons != null && rod.m_icons.Length > 0 ? rod.m_icons[0] : null;
            s_rest.m_ttl = Plugin.CastCooldown.Value;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(s_rest, false));

            // the power attack: the rod's own cast animation, hitting nothing and throwing no float
            var cast = rod.m_attack.Clone();
            cast.m_attackType = Attack.AttackType.None;
            cast.m_bowDraw = false;
            cast.m_requiresReload = false;
            cast.m_attackProjectile = null;
            HoldPower.Register(new HoldPower.Spec
            {
                Token = Plugin.RodToken, HoldTime = () => Plugin.CastHoldTime.Value, PowerAttack = cast, Fire = Fire,
                Theme = ChargeFx.Theme.Wind, CanStart = CanStart, NoAmmo = true,     // no bait needed for the sky
            });
            Plugin.Log.LogInfo("Skyfisher's rod: cast animation " + cast.m_attackAnimation + ", explosion " + (Explosion != null ? Explosion.name : "none"));
        }

        private static bool CanStart(Player p) => Time.time >= s_cooldownUntil && OpenSky.Check(p);

        /// <summary>Where the fish will land: what the camera looks at (within CastRange), on the ground.</summary>
        private static Vector3 Aim(Player p)
        {
            var cam = GameCamera.instance != null ? GameCamera.instance.transform : p.transform;
            var mask = LayerMask.GetMask("terrain", "static_solid", "Default", "piece", "character", "character_net");
            var dir = cam.forward;
            if (Physics.Raycast(cam.position + dir * 2f, dir, out var hit, Plugin.CastRange.Value + 8f, mask, QueryTriggerInteraction.Ignore)
                && Vector3.Distance(hit.point, p.transform.position) <= Plugin.CastRange.Value)
                return hit.point;
            var flat = Vector3.ProjectOnPlane(dir, Vector3.up).normalized;
            var at = p.transform.position + flat * Plugin.CastRange.Value * 0.7f;
            if (ZoneSystem.instance != null)
                at.y = ZoneSystem.instance.GetGroundHeight(at);
            return at;
        }

        public static void Fire(Player p, ItemDrop.ItemData rod)
        {
            var target = Aim(p);
            if (!OpenSky.At(target))
                target = p.transform.position + p.transform.forward * 25f;         // under a roof: well in front instead
            int fish = Random.Range(0, s_fish.Length);
            s_cooldownUntil = Time.time + Plugin.CastCooldown.Value;
            s_rest.m_ttl = Plugin.CastCooldown.Value;
            p.GetSEMan().AddStatusEffect(s_rest, true);
            Broadcast(p, target, fish);
            SkyFish.Spawn(p, target, fish, true, rod);
            Plugin.Log.LogInfo("Skyfisher's rod: a " + s_fish[fish] + " falls on " + target.ToString("F0") +
                               " (" + Vector3.Distance(target, p.transform.position).ToString("F0") + " m away)");
        }

        internal static GameObject FishPrefab(int i) => PrefabManager.Cache.GetPrefab<GameObject>(s_fish[Mathf.Clamp(i, 0, s_fish.Length - 1)]);

        public static void RegisterRpc()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == s_rpcInstance)
                return;
            ZRoutedRpc.instance.Register<ZDOID, Vector3, int>(RpcName, (sender, caster, target, fish) =>
            {
                if (sender == ZNet.GetUID())
                    return;                          // the caster spawns its own
                var go = ZNetScene.instance != null ? ZNetScene.instance.FindInstance(caster) : null;
                SkyFish.Spawn(go != null ? go.GetComponent<Player>() : null, target, fish, false);
            });
            s_rpcInstance = ZRoutedRpc.instance;
        }

        private static void Broadcast(Player p, Vector3 target, int fish)
        {
            if (ZRoutedRpc.instance != null && ZRoutedRpc.instance == s_rpcInstance)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, p.GetZDOID(), target, fish);
        }

        // ------------------------------------------------------------------ found by fishing in the Ocean

        /// <summary>
        /// A catch in the Ocean may bring the rod up with it: CatchChance, plus CatchPity for every Ocean catch since
        /// (kept in the player's custom data). Once found, the chance falls back.
        /// </summary>
        [HarmonyPatch(typeof(FishingFloat), nameof(FishingFloat.Catch))]
        private static class Loot
        {
            private static void Postfix(Fish fish, Character owner)
            {
                if (!(owner is Player p) || p != Player.m_localPlayer || fish == null)
                    return;
                if (Heightmap.FindBiome(fish.transform.position) != Heightmap.Biome.Ocean)
                    return;
                var data = p.m_customData;
                int misses = data.TryGetValue("lw_rod_pity", out var s) && int.TryParse(s, out var m) ? m : 0;
                float chance = Plugin.CatchChance.Value + Plugin.CatchPity.Value * misses;
                if (Random.value >= chance)
                {
                    data["lw_rod_pity"] = (misses + 1).ToString();
                    return;
                }
                data["lw_rod_pity"] = "0";
                var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(Plugin.RodPrefab) : null;
                if (prefab == null)
                    return;
                if (p.GetInventory().CanAddItem(prefab, 1))
                    p.GetInventory().AddItem(prefab, 1);
                else
                    Object.Instantiate(prefab, p.transform.position + Vector3.up, Quaternion.identity);
                p.Message(MessageHud.MessageType.Center, "$msg_rodsky_found");
                Plugin.Log.LogInfo("Skyfisher's rod fished up after " + misses + " Ocean catches (chance " + chance.ToString("P1") + ")");
            }
        }
    }

    /// <summary>
    /// One cast, staged to be epic (every client plays it; the caster deals the damage):
    /// 1. the line shoots into the sky and the sky closes in (a thunderstorm forced for the scene), thunder rolls;
    /// 2. the struggle: the caster freezes leaning back, trembling, rooted, losing stamina, the line taut and
    ///    shuddering, the rod creaking, grunts, the camera shaking;
    /// 3. a great yank: the line snaps out of sight, thunder cracks, and far up a giant fish comes tumbling down,
    ///    lying flat and writhing like a ragdoll, its shadow spreading on the ground;
    /// 4. it bursts: a chain of explosions, a fountain of blood, chunks of flesh bouncing all around, blood raining
    ///    down, splatters left on the ground, a shockwave of dust; everything within CastRadius is hit (trees,
    ///    rocks, buildings, tames, players, the caster too, unless HitEverything is off), damage falling off from
    ///    the centre, creatures thrown up; fish and guts left to pick up. Then the sky clears.
    /// </summary>
    internal class SkyFish : MonoBehaviour
    {
        private const float LineTime = 1.0f, StrainTime = 4.5f, YankTime = 0.35f, ClearAfter = 4f;
        private static float s_transitionBefore = -1f;
        /// <summary>While our weather changes run, the game's linear blend eases in and out (EnvEase).</summary>
        internal static bool Easing => s_transitionBefore > 0f;
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_rightItem =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static readonly AccessTools.FieldRef<Character, Rigidbody> s_body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        private static readonly AccessTools.FieldRef<EnvMan, string> s_forceEnv = AccessTools.FieldRefAccess<EnvMan, string>("m_forceEnv");
        private static int s_active;
        private static string s_envBefore;

        private Player _caster;
        private bool _owner;
        private Vector3 _target, _ground, _skyPoint;
        private int _fishIndex;
        private float _age, _fallStart = -1f, _fallTime, _height, _landedAt = -1f;
        private LineRenderer _line;
        private Transform _fish, _shadow;
        private Material _shadowMat;
        private Animator _anim;
        private float _animSpeed = 1f;
        private Transform[] _spine;
        private Quaternion _roll;
        private Vector3 _spin;
        private bool _yanked, _envSet;
        private readonly List<(Material mat, float born)> _splats = new List<(Material, float)>();

        private ItemDrop.ItemData _rod;

        public static void Spawn(Player caster, Vector3 target, int fish, bool owner, ItemDrop.ItemData rod = null)
        {
            var go = new GameObject("rodsky_cast");
            var s = go.AddComponent<SkyFish>();
            s._rod = rod;
            s._caster = caster;
            s._owner = owner;
            s._target = target;
            s._fishIndex = fish;
            s._height = Plugin.VortexHeight.Value + Plugin.FallHeight.Value;   // out of the vortex, just above it
            s._ground = new Vector3(target.x, ZoneSystem.instance != null ? Mathf.Max(ZoneSystem.instance.GetGroundHeight(target), target.y) : target.y, target.z);
            s._skyPoint = s._ground + Vector3.up * s._height;
            s._fallTime = Plugin.FallTime.Value;
            Destroy(go, 40f);
        }

        /// <summary>While the caster strains, they can't walk away.</summary>
        internal static bool Rooted;

        private MeshFilter _rodMesh;
        private Vector3 _rodTipLocal;
        private GameObject _rodItem;

        /// <summary>The held rod's tip: the far end of its mesh along its length, away from the hand.</summary>
        private bool FindRod()
        {
            var vis = _caster != null ? _caster.GetComponent<VisEquipment>() : null;
            var item = vis != null ? s_rightItem(vis) : null;
            if (item == null)
                return false;
            if (item == _rodItem && _rodMesh != null)
                return true;
            _rodItem = item;
            _rodMesh = null;
            float best = 0f;
            foreach (var mf in item.GetComponentsInChildren<MeshFilter>())
                if (mf.sharedMesh != null && mf.sharedMesh.bounds.size.magnitude * mf.transform.lossyScale.x > best)
                {
                    best = mf.sharedMesh.bounds.size.magnitude * mf.transform.lossyScale.x;
                    _rodMesh = mf;
                }
            if (_rodMesh == null)
                return false;
            var b = _rodMesh.sharedMesh.bounds;
            var hand = _rodMesh.transform.InverseTransformPoint(item.transform.position);
            var e = b.extents;
            int k = e.x >= e.y && e.x >= e.z ? 0 : (e.y >= e.z ? 1 : 2);
            var axis = k == 0 ? Vector3.right : k == 1 ? Vector3.up : Vector3.forward;
            float s = Vector3.Dot(b.center - hand, axis) >= 0f ? 1f : -1f;
            _rodTipLocal = b.center + axis * s * e[k];
            return true;
        }

        private Vector3 RodTip()
        {
            if (FindRod())
                return _rodMesh.transform.TransformPoint(_rodTipLocal);
            if (_caster == null)
                return _ground + Vector3.up * 2f;
            var vis = _caster.GetComponent<VisEquipment>();
            var hand = vis != null && vis.m_rightHand != null ? vis.m_rightHand : _caster.transform;
            return hand.position + Vector3.up * 1.6f;
        }

        private void Start()
        {
            var lg = new GameObject("line");
            lg.transform.SetParent(transform, false);
            _line = lg.AddComponent<LineRenderer>();
            _line.sharedMaterial = Fx.Plain;
            _line.widthMultiplier = 0.03f;
            _line.startColor = _line.endColor = new Color(0.92f, 0.92f, 0.85f, 0.95f);
            _line.positionCount = 16;
            _line.useWorldSpace = true;
            Sfx.Play(Sfx.Reel, RodTip(), null);
            // the sky closes in: our own storm, blended smoothly over the game's weather (StormFx)
            StormFx.Request(true);
            _envSet = true;
            Sfx.Play(Sfx.PortalOpen, _ground + Vector3.up * Plugin.VortexHeight.Value * 0.6f, null);
            Sfx.Play(Sfx.Strike, _ground + Vector3.up * 60f, null);
            BuildVortex();
            if (_caster != null)
            {
                _anim = _caster.GetComponentInChildren<Animator>();
                var bones = new List<Transform>();
                foreach (var t in _caster.GetComponentsInChildren<Transform>())
                    if (t.name == "Spine1" || t.name == "Spine2" || t.name == "Head")
                        bones.Add(t);
                _spine = bones.ToArray();
            }
        }

        private void OnDestroy()
        {
            EndStrain();
            ClearSky();
            foreach (var (m, _) in _splats) if (m != null) Destroy(m);
            if (_shadowMat != null) Destroy(_shadowMat);
        }

        private void ClearSky()
        {
            if (!_envSet)
                return;
            _envSet = false;
            StormFx.Request(false);
        }

        private static System.Collections.IEnumerator RestoreTransition()
        {
            yield return new WaitForSeconds(Plugin.ClearFade.Value + 0.5f);
            if (s_active == 0 && EnvMan.instance != null && s_transitionBefore > 0f)
            {
                EnvMan.instance.m_transitionDuration = s_transitionBefore;
                s_transitionBefore = -1f;
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            UpdateVortex();
            float strainStart = LineTime, yankAt = LineTime + StrainTime;
            if (_age < yankAt)
            {
                if (_age >= strainStart)
                    Strain(_age - strainStart);
                return;
            }
            if (!_yanked)
                Yank();
            if (_fallStart < 0f)
                StartFall();
            if (_landedAt < 0f)
            {
                Fall();
                return;
            }
            // after: splatters fade, the sky clears
            float since = _age - _landedAt;
            if (since > ClearAfter)
                ClearSky();
            foreach (var (m, born) in _splats)
                if (m != null)
                {
                    var c = m.color;
                    c.a = 0.8f * Mathf.Clamp01(1f - (_age - born - 15f) / 6f);
                    m.color = c;
                }
        }

        private void UpdateLine(float strainStart, float yankAt)
        {
            var from = RodTip();
            float k = Mathf.Clamp01(_age / LineTime);
            var to = Vector3.Lerp(from, _vortex != null ? _vortex.position : _skyPoint, k * k);   // up into the portal
            // taut and shuddering while the caster strains, harder and harder
            float strain = _age >= strainStart ? Mathf.Clamp01((_age - strainStart) / StrainTime) : 0f;
            float shake = _age >= strainStart ? (0.15f + 0.6f * strain) : 0f;
            for (int i = 0; i < _line.positionCount; i++)
            {
                float t = (float)i / (_line.positionCount - 1);
                var p = Vector3.Lerp(from, to, t) + Random.insideUnitSphere * shake * Mathf.Sin(t * Mathf.PI);
                _line.SetPosition(i, p);
            }
            _line.enabled = _age < yankAt + 0.05f;
        }

        private void Strain(float t)
        {
            if (t < Time.deltaTime * 1.5f)
            {
                Sfx.Play(Sfx.Strain, RodTip(), _caster != null ? _caster.transform : null);
                if (_anim != null) { _animSpeed = _anim.speed; _anim.speed = 0f; }     // frozen in the effort
                if (_owner) Rooted = true;
            }
            float k = Mathf.Clamp01(t / StrainTime);
            if (_owner)
            {
                if (_caster != null) _caster.UseStamina(Plugin.StrainStamina.Value * Time.deltaTime / StrainTime);
                if (GameCamera.instance != null) GameCamera.instance.AddShake(_caster != null ? _caster.transform.position : _ground, 10f, 0.2f + 0.6f * k, false);
            }
        }

        /// <summary>Lean back and tremble, on top of the frozen pose (after the animator has written it).</summary>
        private void LateUpdate()
        {
            RaiseRod();
            if (!_yanked && _line != null)
                UpdateLine(LineTime, LineTime + StrainTime);           // after the arm is up: from the rod's real tip
            if (_spine == null || _spine.Length == 0)
                return;
            float strainStart = LineTime, yankAt = LineTime + StrainTime;
            float lean = 0f, tremble = 0f;
            if (_age >= strainStart && _age < yankAt)
            {
                float k = Mathf.Clamp01((_age - strainStart) / 0.4f);
                lean = -14f * k;
                tremble = 2f + 4f * Mathf.Clamp01((_age - strainStart) / StrainTime);
            }
            else if (_age >= yankAt && _age < yankAt + YankTime)
                lean = Mathf.Lerp(-18f, 12f, (_age - yankAt) / YankTime);              // the great heave
            if (lean == 0f && tremble == 0f)
                return;
            foreach (var b in _spine)
                b.localRotation = b.localRotation * Quaternion.Euler(lean / _spine.Length + Random.Range(-tremble, tremble) / _spine.Length,
                    Random.Range(-tremble, tremble) * 0.5f / _spine.Length, 0f);
        }

        /// <summary>The arm comes up and the rod points to the sky (toward the portal) until the line breaks.</summary>
        private void RaiseRod()
        {
            float yankAt = LineTime + StrainTime;
            float w = Mathf.Clamp01(_age / 0.4f) * Mathf.Clamp01((yankAt + 0.3f - _age) / 0.3f);
            if (w <= 0f || _anim == null || !_anim.isHuman || !FindRod())
                return;
            var upper = _anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lower = _anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand = _anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (upper == null || lower == null || hand == null)
                return;
            var t = _caster.transform;
            var u0 = upper.rotation;
            var l0 = lower.rotation;
            var h0 = hand.rotation;
            var toPortal = (_vortex != null ? _vortex.position : _skyPoint) - upper.position;
            var flat = Vector3.ProjectOnPlane(toPortal, Vector3.up).normalized;
            var dir = (Vector3.up * 1.6f + flat * 0.6f).normalized;                       // up, a little toward the portal
            var wrist = upper.position + t.up * 0.3f + flat * 0.32f + t.right * 0.08f;     // the arm raised before the chest
            var pole = upper.position + t.right * 0.4f - t.up * 0.3f;
            SolveTwoBone(upper, lower, hand, wrist, pole);
            var rodDir = RodTip() - _rodItem.transform.position;
            if (rodDir.sqrMagnitude > 1e-6f)
                hand.rotation = Quaternion.FromToRotation(rodDir, dir) * hand.rotation;
            if (w < 0.999f)
            {
                upper.rotation = Quaternion.Slerp(u0, upper.rotation, w);
                lower.rotation = Quaternion.Slerp(l0, lower.rotation, w);
                hand.rotation = Quaternion.Slerp(h0, hand.rotation, w);
            }
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

        /// <summary>The celestial line breaks: it bursts into twinkling, drifting motes of light along its whole length.</summary>
        private void BreakLine()
        {
            if (_line == null || _line.positionCount < 2)
                return;
            var go = new GameObject("line_motes");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 3.2f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.03f;
            main.maxParticles = 1400;
            var em = ps.emission;
            em.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;
            var noise = ps.noise;                                          // they drift and swirl
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.4f;
            var col = ps.colorOverLifetime;                                // and twinkle
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(1f, 0.85f, 0.5f), 0.5f), new GradientColorKey(new Color(0.6f, 0.8f, 1f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.25f, 0.15f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0.2f, 0.45f),
                              new GradientAlphaKey(0.9f, 0.6f), new GradientAlphaKey(0.3f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Fx.Soft;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            int segs = _line.positionCount - 1;
            for (int i = 0; i < segs; i++)
            {
                var a = _line.GetPosition(i);
                var b = _line.GetPosition(i + 1);
                for (int j = 0; j < 70; j++)
                {
                    var ep = new ParticleSystem.EmitParams
                    {
                        position = Vector3.Lerp(a, b, Random.value),
                        velocity = Random.insideUnitSphere * Random.Range(0.3f, 2.2f),
                        startSize = Random.Range(0.08f, 0.32f),
                        startColor = Color.Lerp(new Color(1f, 0.95f, 0.75f, 1f), new Color(0.75f, 0.9f, 1f, 1f), Random.value),
                    };
                    ps.Emit(ep, 1);
                }
            }
            Destroy(go, 4f);
        }

        private void EndStrain()
        {
            if (_anim != null && _anim.speed == 0f) _anim.speed = _animSpeed;
            if (_owner) Rooted = false;
        }

        private void Yank()
        {
            _yanked = true;
            BreakLine();
            _line.enabled = false;
            EndStrain();
            Sfx.Play(Sfx.Strike, _ground + Vector3.up * 40f, null);
            if (_owner && GameCamera.instance != null)
                GameCamera.instance.AddShake(_caster != null ? _caster.transform.position : _ground, 15f, 2.5f, false);
        }

        private void StartFall()
        {
            _fallStart = _age;
            if (_owner && _rod != null && _caster != null)
            {
                _rod.m_durability = 0f;                                         // the sky took everything the rod had
                _caster.Message(MessageHud.MessageType.TopLeft, "$msg_rodsky_broken");
            }
            Sfx.Play(Sfx.FishFall, _ground + Vector3.up * 25f, null);
            _fish = BuildFish();
            // flat, tumbling: a slow roll round its length, a little pitch, writhing (see Fall)
            _roll = Quaternion.Euler(0f, Random.Range(0f, 360f), Random.Range(-30f, 30f));
            _spin = new Vector3(Random.Range(-25f, 25f), Random.Range(-40f, 40f), Random.Range(60f, 110f) * (Random.value < 0.5f ? -1f : 1f));
            var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(transform, false);
            disc.transform.position = _ground + Vector3.up * 0.1f;
            disc.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            _shadowMat = new Material(Fx.Soft) { color = new Color(0f, 0f, 0f, 0.1f) };
            disc.GetComponent<MeshRenderer>().sharedMaterial = _shadowMat;
            disc.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _shadow = disc.transform;
        }

        private void Fall()
        {
            float u = Mathf.Clamp01((_age - _fallStart) / _fallTime);
            float y = _height * (1f - u * u);                                // accelerating, like a body in free fall
            if (_fish != null)
            {
                float emerge = Mathf.Clamp01((_age - _fallStart) / 0.6f);
                _fish.localScale = Vector3.one * Plugin.FishScale.Value * Mathf.Max(0.01f, emerge * emerge * (3f - 2f * emerge));
                _fish.position = _ground + Vector3.up * (y + 1f);
                _roll = _roll * Quaternion.Euler(_spin * Time.deltaTime);
                float writhe = Mathf.Sin(_age * 9f) * 18f;                    // flopping like a ragdoll
                _fish.rotation = _roll * Quaternion.Euler(writhe * 0.4f, writhe, 0f);
            }
            float near = 1f - Mathf.Clamp01(y / _height);
            if (_shadow != null)
            {
                _shadow.localScale = Vector3.one * Mathf.Lerp(Plugin.CastRadius.Value * 2.2f, Plugin.CastRadius.Value * 1.2f, near);
                _shadowMat.color = new Color(0f, 0f, 0f, 0.1f + 0.55f * near);
            }
            if (u >= 1f)
                Land();
        }

        /// <summary>A vanilla fish, as a bare visual (no network object, no scripts but its animator), giant.</summary>
        private Transform BuildFish()
        {
            var prefab = SkyFishing.FishPrefab(_fishIndex);
            if (prefab == null)
                return null;
            ZNetView.m_forceDisableInit = true;
            GameObject go;
            try { go = Instantiate(prefab, _skyPoint, Quaternion.identity); }
            finally { ZNetView.m_forceDisableInit = false; }
            foreach (var c in go.GetComponentsInChildren<Collider>(true)) Destroy(c);
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) Destroy(rb);
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true)) Destroy(mb);
            foreach (var nv in go.GetComponentsInChildren<ZNetView>(true)) Destroy(nv);
            foreach (var a in go.GetComponentsInChildren<Animator>(true)) a.speed = 3f;   // frantic flapping
            go.transform.SetParent(transform, true);
            go.transform.localScale = Vector3.one * 0.01f;                            // grows as it comes through
            go.SetActive(true);
            return go.transform;
        }


        // ------------------------------------------------------------------ the vortex in the sky

        private Transform _vortex;
        private ParticleSystem _clouds, _funnel, _dust;
        private Light _vortexLight;
        private LineRenderer _vortexBolt;
        private float _nextFlash;

        private static void Sheet(ParticleSystem ps, (Material mat, int x, int y) fb)
        {
            if (fb.mat == null || fb.x * fb.y <= 1)
                return;
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Grid;
            tsa.numTilesX = fb.x;
            tsa.numTilesY = fb.y;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.cycleCount = 1;
        }

        private ParticleSystem Loop(string name, Transform parent, Material mat, float rate, Vector2 life, Vector2 size, Color a, Color b)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = 1500;
            var em = ps.emission;
            em.rateOverTime = rate;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            ps.Play();
            return ps;
        }

        /// <summary>
        /// A whirlpool of storm clouds opening in the sky above the target: a wide disc of dark clouds wheeling round
        /// and drawn to its eye, a funnel twisting down from it, lightning flickering inside, and dust and leaves
        /// sucked up off the ground below. It grows while the caster strains, races at the yank, and spits the fish out.
        /// </summary>
        private void BuildVortex()
        {
            float h = Mathf.Min(_height, Plugin.VortexHeight.Value);
            _vortex = new GameObject("vortex").transform;
            _vortex.SetParent(transform, false);
            _vortex.position = _ground + Vector3.up * h;
            var cloudMat = SkyFishing.SmokeBook.mat ?? Fx.Soft;
            _clouds = Loop("clouds", _vortex, cloudMat, 90f, new Vector2(3f, 4.5f), new Vector2(10f, 22f),
                new Color(0.16f, 0.17f, 0.21f, 0.75f), new Color(0.28f, 0.3f, 0.36f, 0.6f));
            Sheet(_clouds, SkyFishing.SmokeBook);
            var cs = _clouds.shape;
            cs.shapeType = ParticleSystemShapeType.Circle;
            cs.radius = 45f;
            cs.rotation = new Vector3(-90f, 0f, 0f);
            var cv = _clouds.velocityOverLifetime;
            cv.enabled = true;
            cv.space = ParticleSystemSimulationSpace.Local;
            cv.orbitalY = new ParticleSystem.MinMaxCurve(0.6f);
            cv.radial = new ParticleSystem.MinMaxCurve(-6f);
            _funnel = Loop("funnel", _vortex, cloudMat, 70f, new Vector2(2f, 3f), new Vector2(5f, 10f),
                new Color(0.12f, 0.13f, 0.17f, 0.8f), new Color(0.25f, 0.27f, 0.33f, 0.7f));
            Sheet(_funnel, SkyFishing.SmokeBook);
            var fs = _funnel.shape;
            fs.shapeType = ParticleSystemShapeType.Cone;
            fs.angle = 12f;
            fs.radius = 9f;
            fs.rotation = new Vector3(90f, 0f, 0f);                // downwards
            var fv = _funnel.velocityOverLifetime;
            fv.enabled = true;
            fv.space = ParticleSystemSimulationSpace.Local;
            fv.orbitalY = new ParticleSystem.MinMaxCurve(1.4f);
            fv.y = new ParticleSystem.MinMaxCurve(-10f);
            fv.radial = new ParticleSystem.MinMaxCurve(-1.5f);
            // dust and leaves sucked up from the ground, round and round
            var dustRoot = new GameObject("ground_vortex").transform;
            dustRoot.SetParent(transform, false);
            dustRoot.position = _ground + Vector3.up * 0.3f;
            _dust = Loop("dust", dustRoot, Fx.Soft, 0f, new Vector2(1.5f, 2.5f), new Vector2(0.6f, 2f),
                new Color(0.5f, 0.44f, 0.36f, 0.35f), new Color(0.38f, 0.33f, 0.26f, 0.45f));
            var ds = _dust.shape;
            ds.shapeType = ParticleSystemShapeType.Circle;
            ds.radius = Plugin.CastRadius.Value * 0.8f;
            ds.rotation = new Vector3(-90f, 0f, 0f);
            var dv = _dust.velocityOverLifetime;
            dv.enabled = true;
            dv.space = ParticleSystemSimulationSpace.Local;
            dv.orbitalY = new ParticleSystem.MinMaxCurve(1.2f);
            dv.radial = new ParticleSystem.MinMaxCurve(-4f);
            dv.y = new ParticleSystem.MinMaxCurve(6f);
            var lg = new GameObject("flash");
            lg.transform.SetParent(_vortex, false);
            _vortexLight = lg.AddComponent<Light>();
            _vortexLight.type = LightType.Point;
            _vortexLight.color = new Color(0.7f, 0.8f, 1f);
            _vortexLight.range = 140f;
            _vortexLight.intensity = 0f;
            var bg = new GameObject("bolt");
            bg.transform.SetParent(_vortex, false);
            _vortexBolt = bg.AddComponent<LineRenderer>();
            _vortexBolt.sharedMaterial = Fx.Plain;
            _vortexBolt.widthMultiplier = 0.5f;
            _vortexBolt.useWorldSpace = true;
            _vortexBolt.positionCount = 10;
            _vortexBolt.enabled = false;
            _vortex.localScale = Vector3.one * 0.05f;
            BuildPortal();
        }

        private ParticleSystem _rim, _stars;
        private Transform _eye;
        private Material _eyeMat;
        private Light _portalLight;

        /// <summary>
        /// The eye of the vortex opens onto somewhere else: a dark violet void with stars twinkling deep inside, ringed by a
        /// wheeling band of cold light (violet to cyan), and a coloured glow on the clouds and the land below.
        /// </summary>
        private void BuildPortal()
        {
            var eyeGo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(eyeGo.GetComponent<Collider>());
            eyeGo.name = "portal_eye";
            eyeGo.transform.SetParent(_vortex, false);
            eyeGo.transform.localPosition = Vector3.down * 0.5f;
            eyeGo.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            eyeGo.transform.localScale = Vector3.one * 30f;
            _eyeMat = new Material(Fx.Soft) { color = new Color(0.08f, 0.03f, 0.16f, 0f) };
            var er = eyeGo.GetComponent<MeshRenderer>();
            er.sharedMaterial = _eyeMat;
            er.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _eye = eyeGo.transform;

            _rim = Loop("portal_rim", _vortex, Fx.Soft, 0f, new Vector2(0.8f, 1.6f), new Vector2(1.5f, 3.5f),
                new Color(0.7f, 0.45f, 1f, 0.85f), new Color(0.45f, 0.9f, 1f, 0.85f));
            var rs = _rim.shape;
            rs.shapeType = ParticleSystemShapeType.Circle;
            rs.radius = 14f;
            rs.radiusThickness = 0.05f;
            rs.rotation = new Vector3(-90f, 0f, 0f);
            var rv = _rim.velocityOverLifetime;
            rv.enabled = true;
            rv.space = ParticleSystemSimulationSpace.Local;
            rv.orbitalY = new ParticleSystem.MinMaxCurve(2.4f);
            _rim.transform.localPosition = Vector3.down * 0.8f;

            _stars = Loop("portal_stars", _vortex, Fx.Soft, 0f, new Vector2(0.6f, 1.8f), new Vector2(0.15f, 0.6f),
                new Color(1f, 1f, 1f, 1f), new Color(0.7f, 0.85f, 1f, 1f));
            var ss = _stars.shape;
            ss.shapeType = ParticleSystemShapeType.Circle;
            ss.radius = 12f;
            ss.rotation = new Vector3(-90f, 0f, 0f);
            _stars.transform.localPosition = Vector3.down * 0.3f;

            var lg = new GameObject("portal_glow");
            lg.transform.SetParent(_vortex, false);
            lg.transform.localPosition = Vector3.down * 4f;
            _portalLight = lg.AddComponent<Light>();
            _portalLight.type = LightType.Point;
            _portalLight.color = new Color(0.6f, 0.45f, 1f);
            _portalLight.range = 160f;
            _portalLight.intensity = 0f;
        }

        private static readonly Color[] s_palette =
        {
            new Color(0.75f, 0.4f, 1f), new Color(0.35f, 0.9f, 1f), new Color(1f, 0.35f, 0.85f),
            new Color(0.45f, 1f, 0.55f), new Color(1f, 0.85f, 0.35f), new Color(0.55f, 0.6f, 1f),
        };
        private readonly List<(LineRenderer line, float until)> _bolts = new List<(LineRenderer, float)>();
        private readonly List<(LineRenderer core, LineRenderer glow, Vector3 foot, float phase, int hue)> _rays =
            new List<(LineRenderer, LineRenderer, Vector3, float, int)>();
        private float _nextBolt;

        private LineRenderer NewLine(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var l = go.AddComponent<LineRenderer>();
            l.sharedMaterial = Fx.Plain;
            l.useWorldSpace = true;
            l.widthMultiplier = width;
            l.numCapVertices = 2;
            l.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            l.enabled = false;
            return l;
        }

        /// <summary>Jagged multicolour lightning crawling over the vortex: rim to eye, across the disc, with forks.</summary>
        private void PortalLightning(float open)
        {
            for (int i = _bolts.Count - 1; i >= 0; i--)
                if (Time.time >= _bolts[i].until)
                {
                    Destroy(_bolts[i].line.gameObject);
                    _bolts.RemoveAt(i);
                }
            if (_landedAt >= 0f || open <= 0.05f || Time.time < _nextBolt)
                return;
            _nextBolt = Time.time + Random.Range(0.05f, 0.25f) / Mathf.Max(0.25f, open);
            var c = _vortex.position;
            float R = 40f * _vortex.localScale.x;
            var col = s_palette[Random.Range(0, s_palette.Length)];
            float a0 = Random.Range(0f, 360f);
            var start = c + Quaternion.Euler(0f, a0, 0f) * Vector3.forward * R * Random.Range(0.7f, 1f) + Vector3.down * Random.Range(0f, 4f);
            var end = Random.value < 0.6f
                ? c + Vector3.down * 0.5f + Random.insideUnitSphere * 3f                         // into the eye
                : c + Quaternion.Euler(0f, a0 + Random.Range(90f, 200f), 0f) * Vector3.forward * R * Random.Range(0.5f, 1f);
            Bolt(start, end, col, Random.Range(0.5f, 1.3f), 14, 0.08f + Random.Range(0.05f, 0.15f));
            for (int f = 0; f < 2; f++)                                                         // forks
            {
                var from = Vector3.Lerp(start, end, Random.Range(0.25f, 0.75f));
                Bolt(from, from + Random.insideUnitSphere * R * 0.35f, col, 0.35f, 7, 0.1f);
            }
            _vortexLight.color = col;
            _vortexLight.intensity = Mathf.Max(_vortexLight.intensity, Random.Range(5f, 10f));
        }

        private void Bolt(Vector3 a, Vector3 b, Color col, float width, int points, float life)
        {
            var l = NewLine("portal_bolt", width);
            l.positionCount = points;
            float len = Vector3.Distance(a, b);
            for (int i = 0; i < points; i++)
            {
                float t = (float)i / (points - 1);
                l.SetPosition(i, Vector3.Lerp(a, b, t) + Random.insideUnitSphere * len * 0.06f * Mathf.Sin(t * Mathf.PI));
            }
            var bright = Color.Lerp(col, Color.white, 0.55f);
            l.startColor = bright;
            l.endColor = new Color(col.r, col.g, col.b, 0.8f);
            l.enabled = true;
            _bolts.Add((l, Time.time + life));
        }

        private class Shaft
        {
            public LineRenderer Line;
            public float Born, Life, Width, Seed;
            public int Hue;
        }

        private readonly List<Shaft> _shafts = new List<Shaft>();
        private float _nextShaft;
        private ParticleSystem _motes, _haze;
        private static Material s_shaftMat;

        /// <summary>A soft beam: transparent edges across, brighter at the top, fading out toward the ground.</summary>
        private static Material ShaftMat()
        {
            if (s_shaftMat != null)
                return s_shaftMat;
            const int w = 64, h = 32;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "lw_shaft", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float v = (y + 0.5f) / h * 2f - 1f, u = (x + 0.5f) / w;
                    float across = Mathf.Exp(-v * v * 4.5f);
                    float along = Mathf.Clamp01(u / 0.08f) * (1f - 0.75f * u) * Mathf.Clamp01((1f - u) / 0.12f);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, across * along));
                }
            tex.Apply(true);
            s_shaftMat = new Material(Fx.Plain) { name = "lw_shaftmat", mainTexture = tex };
            return s_shaftMat;
        }

        /// <summary>
        /// Veils of strange light from the eye down to the land: soft wide beams that are born, drift and sway (top and
        /// foot wandering), change hue and fade away, never the same twice; motes of light sinking through them and
        /// coloured haze swirling slowly under the portal.
        /// </summary>
        private void PortalRays(float open)
        {
            if (_motes == null)
                BuildHaze();
            bool alive = _landedAt < 0f;
            float show = Mathf.Clamp01((open - 0.25f) / 0.45f);
            if (alive && show > 0f && Time.time >= _nextShaft && _shafts.Count < 16)
            {
                _nextShaft = Time.time + Random.Range(0.25f, 0.7f);
                var l = NewLine("portal_shaft", 1f);
                l.sharedMaterial = ShaftMat();
                l.textureMode = LineTextureMode.Stretch;
                l.positionCount = 6;
                l.alignment = LineAlignment.View;
                _shafts.Add(new Shaft { Line = l, Born = Time.time, Life = Random.Range(4f, 8f), Width = Random.Range(3f, 10f), Seed = Random.Range(0f, 100f), Hue = Random.Range(0, s_palette.Length) });
            }
            var c = _vortex.position + Vector3.down * 2f;
            float R = Plugin.CastRadius.Value;
            for (int i = _shafts.Count - 1; i >= 0; i--)
            {
                var sh = _shafts[i];
                float age = Time.time - sh.Born, k = age / sh.Life;
                if (k >= 1f || (!alive && sh.Line.startColor.a < 0.002f))
                {
                    Destroy(sh.Line.gameObject);
                    _shafts.RemoveAt(i);
                    continue;
                }
                float life = Mathf.Sin(Mathf.PI * Mathf.Clamp01(k));                   // fades in and out
                if (!alive) life *= Mathf.Clamp01(1f - (Time.time - Mathf.Max(_landedAt, 0f)) / 3f);
                float tx = Time.time * 0.08f;
                var topOff = new Vector3(Mathf.PerlinNoise(sh.Seed, tx) - 0.5f, 0f, Mathf.PerlinNoise(tx, sh.Seed) - 0.5f) * 18f * _vortex.localScale.x;
                var footOff = new Vector3(Mathf.PerlinNoise(sh.Seed + 7f, tx * 1.6f) - 0.5f, 0f, Mathf.PerlinNoise(tx * 1.6f, sh.Seed + 7f) - 0.5f) * 2.4f * R;
                var top = c + topOff;
                var foot = _ground + footOff;
                if (ZoneSystem.instance != null) foot.y = ZoneSystem.instance.GetGroundHeight(foot);
                for (int p = 0; p < sh.Line.positionCount; p++)                       // a slight sway along the beam
                {
                    float t = (float)p / (sh.Line.positionCount - 1);
                    var sway = new Vector3(Mathf.Sin(Time.time * 0.6f + sh.Seed + t * 2f), 0f, Mathf.Cos(Time.time * 0.5f + sh.Seed + t * 2.3f)) * 2.5f * Mathf.Sin(t * Mathf.PI);
                    sh.Line.SetPosition(p, Vector3.Lerp(top, foot, t) + sway);
                }
                var col = Color.Lerp(s_palette[sh.Hue], s_palette[(sh.Hue + 2) % s_palette.Length], 0.5f + 0.5f * Mathf.Sin(Time.time * 0.4f + sh.Seed));
                col = Color.Lerp(col, Color.white, 0.25f);
                float a = 0.16f * life * show * (0.75f + 0.25f * Mathf.PerlinNoise(Time.time * 1.3f, sh.Seed));
                sh.Line.startWidth = sh.Width * 0.6f;
                sh.Line.endWidth = sh.Width * 1.6f;                                     // widening as it falls
                sh.Line.startColor = new Color(col.r, col.g, col.b, a);
                sh.Line.endColor = new Color(col.r, col.g, col.b, a * 0.5f);
                sh.Line.enabled = true;
            }
            if (_motes != null)
            {
                var me = _motes.emission;
                me.rateOverTime = alive ? 90f * show : 0f;
                var he = _haze.emission;
                he.rateOverTime = alive ? 10f * Mathf.Clamp01(open * 1.5f) : 0f;
            }
        }

        private void BuildHaze()
        {
            // motes of light sinking slowly through the column under the portal
            _motes = Loop("portal_motes", _vortex, Fx.Soft, 0f, new Vector2(5f, 9f), new Vector2(0.12f, 0.4f),
                new Color(0.85f, 0.75f, 1f, 0.9f), new Color(0.6f, 0.95f, 1f, 0.9f));
            var mm = _motes.main;
            mm.simulationSpace = ParticleSystemSimulationSpace.World;
            mm.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2f);
            var ms = _motes.shape;
            ms.shapeType = ParticleSystemShapeType.Cone;
            ms.angle = 22f;
            ms.radius = 10f;
            ms.rotation = new Vector3(90f, 0f, 0f);
            var mn = _motes.noise;
            mn.enabled = true;
            mn.strength = 1.2f;
            mn.frequency = 0.15f;
            // coloured haze: huge faint puffs swirling under the eye, slowly shifting hue
            _haze = Loop("portal_haze", _vortex, Fx.Soft, 0f, new Vector2(7f, 12f), new Vector2(18f, 38f),
                new Color(0.55f, 0.35f, 0.85f, 0.07f), new Color(0.3f, 0.7f, 0.85f, 0.07f));
            var hm = _haze.main;
            hm.simulationSpace = ParticleSystemSimulationSpace.World;
            hm.startSpeed = new ParticleSystem.MinMaxCurve(0.2f, 1f);
            var hs = _haze.shape;
            hs.shapeType = ParticleSystemShapeType.Cone;
            hs.angle = 35f;
            hs.radius = 16f;
            hs.rotation = new Vector3(90f, 0f, 0f);
            var hv = _haze.velocityOverLifetime;
            hv.enabled = true;
            hv.space = ParticleSystemSimulationSpace.Local;
            hv.orbitalY = new ParticleSystem.MinMaxCurve(0.15f);
            hv.y = new ParticleSystem.MinMaxCurve(-2.5f);
        }

        private void UpdatePortal(float open)
        {
            if (_eye == null)
                return;
            PortalLightning(open);
            PortalRays(open);
            float pulse = 0.85f + 0.15f * Mathf.Sin(Time.time * 2.1f);
            _eyeMat.color = new Color(0.08f, 0.03f, 0.16f, 0.95f * open);
            _eye.localScale = Vector3.one * 30f * (0.15f + 0.85f * open);
            var re = _rim.emission;
            re.rateOverTime = _landedAt < 0f ? 420f * open : 0f;
            var se = _stars.emission;
            se.rateOverTime = _landedAt < 0f ? 120f * open : 0f;
            _portalLight.intensity = _landedAt < 0f ? 2.5f * open * pulse : Mathf.MoveTowards(_portalLight.intensity, 0f, 2f * Time.deltaTime);
            if (_landedAt >= 0f)
                _eyeMat.color = new Color(0.08f, 0.03f, 0.16f, Mathf.MoveTowards(_eyeMat.color.a, 0f, 0.5f * Time.deltaTime));
        }

        private void UpdateVortex()
        {
            if (_vortex == null)
                return;
            float yankAt = LineTime + StrainTime;
            // forms slowly while the caster strains, full size at the yank; the portal opens in its eye
            float grow = Mathf.Clamp01(_age / yankAt);
            grow = grow * grow * (3f - 2f * grow);
            _vortex.localScale = Vector3.one * Mathf.Lerp(0.05f, 1.15f, grow);
            UpdatePortal(Mathf.Clamp01((_age - LineTime * 0.5f) / (yankAt - LineTime * 0.5f)));
            float spin = _age < yankAt ? Mathf.Lerp(0.4f, 1f, grow) : 2.2f;            // races once the fish is hooked
            _vortex.Rotate(Vector3.up, 25f * spin * Time.deltaTime, Space.World);
            var dem = _dust.emission;
            dem.rateOverTime = _landedAt < 0f ? 140f * grow : 0f;
            // lightning in the clouds: flashes and short jagged bolts inside the vortex
            if (_landedAt < 0f && Time.time >= _nextFlash)
            {
                _nextFlash = Time.time + Random.Range(0.15f, 0.7f) / Mathf.Max(0.3f, spin);
                _vortexLight.intensity = Random.Range(4f, 9f);
                var c = _vortex.position;
                var a = c + Random.insideUnitSphere * 30f * _vortex.localScale.x;
                var b = c + Random.insideUnitSphere * 30f * _vortex.localScale.x;
                for (int i = 0; i < _vortexBolt.positionCount; i++)
                {
                    float t = (float)i / (_vortexBolt.positionCount - 1);
                    _vortexBolt.SetPosition(i, Vector3.Lerp(a, b, t) + Random.insideUnitSphere * 3f * Mathf.Sin(t * Mathf.PI));
                }
                _vortexBolt.startColor = _vortexBolt.endColor = new Color(0.85f, 0.92f, 1f, 1f);
                _vortexBolt.enabled = true;
            }
            _vortexLight.intensity = Mathf.MoveTowards(_vortexLight.intensity, 0f, 30f * Time.deltaTime);
            if (_vortexLight.intensity < 1.5f)
                _vortexBolt.enabled = false;
        }

        /// <summary>The fish is out: the vortex stops feeding and fades away while the sky clears.</summary>
        private void ReleaseVortex()
        {
            if (_vortex == null)
                return;
            Sfx.Play(Sfx.PortalClose, _vortex.position + Vector3.down * 20f, null);
            foreach (var ps in new[] { _clouds, _funnel, _dust, _rim, _stars, _motes, _haze })
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _vortexBolt.enabled = false;
        }

        // ------------------------------------------------------------------ the blast, beyond the gore

        private ParticleSystem Once(string name, Vector3 at, Material mat, int count, float duration, Vector2 speed, Vector2 size, Vector2 life,
            float gravity, Color a, Color b)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = duration;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Mathf.Max(count, 10) * 2;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            return ps;
        }

        /// <summary>
        /// Fire and smoke on top of the gore: a fireball rolling up, a mushroom of smoke climbing for seconds, a ring of
        /// smoke rolling out along the ground, embers and rock shards flying, a flash lighting up the whole area, and
        /// a scorch mark under the crater.
        /// </summary>
        private void ImpactFx(Vector3 at, float r)
        {
            var fireMat = SkyFishing.FireBook.mat ?? Fx.Soft;
            var smokeMat = SkyFishing.SmokeBook.mat ?? Fx.Soft;
            // fireball
            var fire = Once("fireball", at + Vector3.up * 2f, fireMat, 70, 0.4f, new Vector2(4f, 12f), new Vector2(6f, 16f), new Vector2(0.8f, 1.6f),
                -0.35f, new Color(1f, 0.75f, 0.35f), new Color(1f, 0.45f, 0.15f));
            Sheet(fire, SkyFishing.FireBook);
            var fe = fire.emission; fe.rateOverTime = 0f; fe.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
            var fsh = fire.shape; fsh.shapeType = ParticleSystemShapeType.Sphere; fsh.radius = r * 0.2f;
            fire.Play();
            // the mushroom: a column of smoke rising and spreading for a few seconds
            var column = Once("smoke_column", at, smokeMat, 0, 3.5f, new Vector2(10f, 18f), new Vector2(5f, 11f), new Vector2(4f, 6.5f),
                -0.05f, new Color(0.18f, 0.16f, 0.15f, 0.85f), new Color(0.32f, 0.29f, 0.27f, 0.75f));
            Sheet(column, SkyFishing.SmokeBook);
            var ce = column.emission; ce.rateOverTime = 70f;
            var csh = column.shape; csh.shapeType = ParticleSystemShapeType.Cone; csh.angle = 12f; csh.radius = r * 0.15f;
            column.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var cl = column.limitVelocityOverLifetime; cl.enabled = true; cl.limit = 4f; cl.dampen = 0.08f;
            var cgrow = column.sizeOverLifetime; cgrow.enabled = true;
            cgrow.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 2.6f));
            column.Play();
            // a ring of smoke rolling out along the ground
            var ring = Once("smoke_ring", at + Vector3.up * 1f, smokeMat, 90, 0.3f, new Vector2(14f, 20f), new Vector2(5f, 10f), new Vector2(2.5f, 4f),
                0f, new Color(0.3f, 0.27f, 0.24f, 0.7f), new Color(0.42f, 0.38f, 0.33f, 0.6f));
            Sheet(ring, SkyFishing.SmokeBook);
            var re = ring.emission; re.rateOverTime = 0f; re.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });
            var rsh = ring.shape; rsh.shapeType = ParticleSystemShapeType.Circle; rsh.radius = 2f; rsh.rotation = new Vector3(-90f, 0f, 0f);
            var rl = ring.limitVelocityOverLifetime; rl.enabled = true; rl.limit = 2f; rl.dampen = 0.06f;
            ring.Play();
            // embers: glowing streaks flung everywhere, bouncing
            var embers = Once("embers", at + Vector3.up, Fx.Soft, 260, 0.3f, new Vector2(15f, 38f), new Vector2(0.08f, 0.25f), new Vector2(1.2f, 2.8f),
                1.2f, new Color(1f, 0.8f, 0.3f), new Color(1f, 0.4f, 0.1f));
            var ee = embers.emission; ee.rateOverTime = 0f; ee.SetBursts(new[] { new ParticleSystem.Burst(0f, 260) });
            var esh = embers.shape; esh.shapeType = ParticleSystemShapeType.Hemisphere; esh.radius = 2f;
            embers.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var er = embers.GetComponent<ParticleSystemRenderer>(); er.renderMode = ParticleSystemRenderMode.Stretch; er.velocityScale = 0.06f;
            var ec = embers.collision; ec.enabled = true; ec.type = ParticleSystemCollisionType.World; ec.bounce = 0.3f; ec.dampen = 0.4f;
            embers.Play();
            // rock shards torn out of the crater
            var rocks = Once("rocks", at + Vector3.up * 0.5f, Fx.Plain, 90, 0.2f, new Vector2(10f, 26f), new Vector2(0.3f, 1.1f), new Vector2(3f, 5f),
                2.2f, new Color(0.3f, 0.28f, 0.26f), new Color(0.45f, 0.42f, 0.38f));
            var rke = rocks.emission; rke.rateOverTime = 0f; rke.SetBursts(new[] { new ParticleSystem.Burst(0f, 90) });
            var rksh = rocks.shape; rksh.shapeType = ParticleSystemShapeType.Cone; rksh.angle = 60f; rksh.radius = 2f;
            rocks.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            if (s_cube == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s_cube = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }
            var rkr = rocks.GetComponent<ParticleSystemRenderer>(); rkr.renderMode = ParticleSystemRenderMode.Mesh; rkr.mesh = s_cube;
            var rkm = rocks.main; rkm.startRotation3D = true;
            rkm.startRotationX = rkm.startRotationY = rkm.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rkc = rocks.collision; rkc.enabled = true; rkc.type = ParticleSystemCollisionType.World; rkc.bounce = 0.25f; rkc.dampen = 0.5f;
            var rkrot = rocks.rotationOverLifetime; rkrot.enabled = true; rkrot.separateAxes = true;
            rkrot.x = rkrot.y = rkrot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            rocks.Play();
            // a flash lighting up everything around
            var lg = new GameObject("blast_light");
            lg.transform.SetParent(transform, false);
            lg.transform.position = at + Vector3.up * 6f;
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.7f, 0.4f);
            l.range = r * 5f;
            l.intensity = 12f;
            StartCoroutine(FadeLight(l, 1.4f));
            // the scorch mark
            var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Destroy(q.GetComponent<Collider>());
            q.transform.SetParent(transform, true);
            q.transform.position = at + Vector3.up * 0.06f;
            q.transform.rotation = Quaternion.Euler(90f, Random.Range(0f, 360f), 0f);
            q.transform.localScale = Vector3.one * r * 1.1f;
            var m = new Material(Fx.Soft) { color = new Color(0.04f, 0.03f, 0.03f, 0.8f) };
            var mr = q.GetComponent<MeshRenderer>();
            mr.sharedMaterial = m;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _splats.Add((m, _age));
        }

        private static System.Collections.IEnumerator FadeLight(Light l, float seconds)
        {
            float t = 0f, start = l.intensity;
            while (t < seconds && l != null)
            {
                t += Time.deltaTime;
                l.intensity = start * (1f - t / seconds) * (1f - t / seconds);
                yield return null;
            }
            if (l != null) Destroy(l.gameObject);
        }

        private void Land()
        {
            _landedAt = _age;
            var at = _ground;
            float r = Plugin.CastRadius.Value;
            if (_fish != null) Destroy(_fish.gameObject);
            if (_shadow != null) Destroy(_shadow.gameObject);
            Sfx.Play(Sfx.FishImpact, at, null);
            if (GameCamera.instance != null)
                GameCamera.instance.AddShake(at, 90f, 5f, false);
            // a chain of explosions: the heart, then a ring bursting outwards
            Explode(at, 2.5f);
            for (int i = 0; i < 10; i++)
            {
                var p = at + Quaternion.Euler(0f, i * 36f + Random.Range(-15f, 15f), 0f) * Vector3.forward * r * Random.Range(0.25f, 0.7f);
                StartCoroutine(Later(Random.Range(0.08f, 0.6f), () => Explode(p, 1.4f)));
            }
            Gore(at, r);
            ImpactFx(at, r);
            ReleaseVortex();
            if (_owner)
                Hurt(at, r);
        }

        private System.Collections.IEnumerator Later(float s, System.Action a)
        {
            yield return new WaitForSeconds(s);
            a();
        }

        private static void Explode(Vector3 at, float scale)
        {
            if (SkyFishing.Explosion == null)
                return;
            var e = Instantiate(SkyFishing.Explosion, at, Quaternion.identity);
            e.transform.localScale *= scale;
            foreach (var ps in e.GetComponentsInChildren<ParticleSystem>())
            {
                var main = ps.main;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            }
        }

        private ParticleSystem Burst(string name, Vector3 at, Material mat, int count, Vector2 speed, Vector2 size, Vector2 life,
            float gravity, Color a, Color b, float coneAngle, bool collide)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            go.transform.rotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 2;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = coneAngle;
            shape.radius = 1.5f;
            if (collide)
            {
                var col = ps.collision;
                col.enabled = true;
                col.type = ParticleSystemCollisionType.World;
                col.dampen = 0.5f;
                col.bounce = 0.2f;
                col.lifetimeLoss = 0f;
            }
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            ps.Play();
            return ps;
        }

        private static Mesh s_cube;

        /// <summary>Blood, flesh, more blood: a fountain, bouncing chunks, a blood rain, splatters and a dust ring.</summary>
        private void Gore(Vector3 at, float r)
        {
            var dark = new Color(0.35f, 0.01f, 0.01f, 0.95f);
            var red = new Color(0.62f, 0.04f, 0.03f, 0.9f);
            Burst("blood_fountain", at, Fx.Soft, 600, new Vector2(10f, 34f), new Vector2(0.3f, 1.3f), new Vector2(1.5f, 3f), 2f, dark, red, 55f, false);
            var chunks = Burst("flesh", at + Vector3.up * 0.5f, Fx.Plain, 160, new Vector2(12f, 36f), new Vector2(0.2f, 0.7f), new Vector2(3f, 5f),
                2.5f, new Color(0.7f, 0.22f, 0.2f), new Color(0.92f, 0.6f, 0.55f), 70f, true);
            if (s_cube == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s_cube = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
            }
            var cr = chunks.GetComponent<ParticleSystemRenderer>();
            cr.renderMode = ParticleSystemRenderMode.Mesh;
            cr.mesh = s_cube;
            var cm = chunks.main;
            cm.startRotation3D = true;
            cm.startRotationX = cm.startRotationY = cm.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var rot = chunks.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = rot.y = rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            // the shockwave: a ring of dust rushing outwards along the ground
            var ring = Burst("shockwave", at + Vector3.up * 0.5f, Fx.Soft, 160, new Vector2(22f, 30f), new Vector2(3f, 6f), new Vector2(0.9f, 1.3f),
                0f, new Color(0.55f, 0.45f, 0.35f, 0.45f), new Color(0.4f, 0.32f, 0.25f, 0.35f), 89f, false);
            var rs = ring.shape;
            rs.radius = 0.5f;
            // blood raining down over the whole zone for a while
            var rain = Burst("blood_rain", at + Vector3.up * 30f, Fx.Soft, 1, new Vector2(0f, 0f), new Vector2(0.1f, 0.25f), new Vector2(2.2f, 2.6f),
                1.6f, dark, red, 0f, false);
            rain.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
            var rm = rain.main;
            rm.loop = false;
            rm.duration = 2.5f;
            rm.startDelay = 0.6f;
            var re = rain.emission;
            re.SetBursts(new ParticleSystem.Burst[0]);
            re.rateOverTime = 260f;
            var rsh = rain.shape;
            rsh.shapeType = ParticleSystemShapeType.Circle;
            rsh.radius = r;
            rain.Play();
            // splatters on the ground, around the crater, fading after a while
            int mask = LayerMask.GetMask("terrain", "static_solid", "Default", "piece");
            for (int i = 0; i < 40; i++)
            {
                var p = at + Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward * r * Mathf.Sqrt(Random.value);
                if (!Physics.Raycast(p + Vector3.up * 10f, Vector3.down, out var hit, 30f, mask, QueryTriggerInteraction.Ignore))
                    continue;
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, true);
                q.transform.position = hit.point + hit.normal * 0.05f;
                q.transform.rotation = Quaternion.LookRotation(-hit.normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
                q.transform.localScale = Vector3.one * Random.Range(1f, 4f);
                var m = new Material(Fx.Soft) { color = new Color(Random.Range(0.3f, 0.5f), 0.01f, 0.01f, 0.8f) };
                var mr = q.GetComponent<MeshRenderer>();
                mr.sharedMaterial = m;
                mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _splats.Add((m, _age));
            }
        }

        /// <summary>
        /// The caster's blast. With HitEverything (default) it spares nothing: creatures, tames, other players and
        /// the caster (PvP or not), trees and logs (chop), rocks (pickaxe), buildings. Damage falls off from the centre.
        /// </summary>
        private void Hurt(Vector3 at, float radius)
        {
            bool all = Plugin.HitEverything.Value;
            var seen = new HashSet<GameObject>();
            int n = 0;
            foreach (var col in Physics.OverlapSphere(at, radius, Geometry.HitMask, QueryTriggerInteraction.Collide))
            {
                var go = Projectile.FindHitObject(col);
                if (go == null || !seen.Add(go))
                    continue;
                var destr = go.GetComponent<IDestructible>();
                if (destr == null)
                    continue;
                var c = destr as Character;
                if (!all && (go.GetComponent<WearNTear>() != null || (c != null && (_caster == null || !Geometry.CanHit(_caster, c)))))
                    continue;
                bool convex = !(col is MeshCollider mc) || mc.convex;
                var point = convex ? col.ClosestPoint(at) : col.bounds.center;
                float dist = Vector3.Distance(point, at);
                float share = Mathf.Lerp(1f, Plugin.CastEdgeDamage.Value, Mathf.Clamp01((dist - 2f) / Mathf.Max(0.1f, radius - 2f)));
                float dmg = Plugin.CastDamage.Value * share;
                var hit = new HitData();
                hit.m_damage.m_blunt = dmg;
                hit.m_damage.m_fire = dmg * 0.35f;
                hit.m_damage.m_chop = dmg * 0.8f;         // trees and logs
                hit.m_damage.m_pickaxe = dmg * 0.8f;      // rocks and ore
                hit.m_toolTier = 4;
                hit.m_point = point;
                var away = Vector3.ProjectOnPlane(point - at, Vector3.up);
                hit.m_dir = away.sqrMagnitude > 0.01f ? away.normalized : Vector3.forward;
                hit.m_pushForce = 120f * share;
                hit.m_staggerMultiplier = 3f;
                hit.m_dodgeable = false;
                hit.m_blockable = false;
                hit.m_ignorePVP = all;
                hit.m_hitType = HitData.HitType.PlayerHit;
                if (_caster != null && c != _caster) hit.SetAttacker(_caster);
                destr.Damage(hit);
                n++;
                if (c != null && !c.IsBoss() && !c.IsDead())
                {
                    var nview = c.GetComponent<ZNetView>();
                    var body = s_body(c);
                    if (body != null && nview != null && nview.IsOwner())
                    {
                        float up = 12f * share * Mathf.Clamp(50f / Mathf.Max(1f, body.mass), 0.3f, 1f);
                        body.linearVelocity = new Vector3(body.linearVelocity.x, Mathf.Max(body.linearVelocity.y, 0f) + up, body.linearVelocity.z);
                        c.TimeoutGroundForce(1.2f);
                    }
                }
            }
            // what is left of the fish
            foreach (var (name, min, max) in new[] { ("FishRaw", 4, 7), ("Entrails", 2, 4) })
            {
                var prefab = PrefabManager.Cache.GetPrefab<GameObject>(name);
                if (prefab == null)
                    continue;
                for (int i = Random.Range(min, max + 1); i > 0; i--)
                {
                    var d = Object.Instantiate(prefab, at + Vector3.up * 1.2f + Random.insideUnitSphere * 2f, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    var b = d.GetComponent<Rigidbody>();
                    if (b != null) b.linearVelocity = Random.insideUnitSphere * 7f + Vector3.up * 8f;
                }
            }
            Plugin.Log.LogInfo("Skyfisher's fish burst: " + n + " things hit within " + radius + " m" + (all ? " (everything)" : ""));
        }
    }

    /// <summary>Test command: every legendary power ready again.</summary>
    internal class ResetCooldownsCommand : ConsoleCommand
    {
        public override string Name => "lw_reset";

        public override string Help => "Legendary Weapons: reset every power's cooldown (test)";

        public override void Run(string[] args)
        {
            HoldPower.ResetAll();
            LightningCall.ResetCooldown();
            FoxFang.ResetCooldown();
            SkyFishing.ResetCooldown();
            FogHorn.ResetCooldown();
            var p = Player.m_localPlayer;
            if (p != null)
                foreach (var se in p.GetSEMan().GetStatusEffects().ToArray())
                    if (se != null && se.name.StartsWith("SE_LW_") && se.name != "SE_LW_Camouflage" && !se.name.StartsWith("SE_LW_Ymir"))
                        p.GetSEMan().RemoveStatusEffect(se.NameHash(), true);
            Console.instance.Print("lw_reset: every legendary power is ready");
            Plugin.Log.LogInfo("Cooldowns reset (lw_reset)");
        }
    }

    /// <summary>The caster can't walk away while straining against the sky.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class SkyFishRoot
    {
        private static void Prefix(Player __instance, ref Vector3 movedir, ref bool jump, ref bool run, ref bool autoRun)
        {
            if (!SkyFish.Rooted || __instance != Player.m_localPlayer)
                return;
            movedir = Vector3.zero;
            jump = run = autoRun = false;
        }
    }

    /// <summary>The weather blend is linear (and the rain switches at the very end): eased in and out during our storm.</summary>
    [HarmonyPatch(typeof(EnvMan), "InterpolateEnvironment", new[] { typeof(EnvSetup), typeof(EnvSetup), typeof(float) })]
    internal static class EnvEase
    {
        private static void Prefix(ref float i)
        {
            if (SkyFish.Easing)
                i = i * i * (3f - 2f * i);
        }
    }

    /// <summary>
    /// Our own storm for the sky cast, laid over the game's weather with a smooth weight (the game's forced environments
    /// swap the sky, clouds and rain in a single frame): dimmer, colder sun, darker ambient, thick blue-grey fog, heavy
    /// rain clouds, and our own rain around the camera. Rolls in over StormFade, out over ClearFade (eased).
    /// </summary>
    internal class StormFx : MonoBehaviour
    {
        internal static float W;
        private static int s_wanted;
        private static float s_progress;
        private static StormFx s_instance;
        private ParticleSystem _rain;

        internal static void Request(bool on)
        {
            s_wanted = Mathf.Max(0, s_wanted + (on ? 1 : -1));
            if (s_instance == null)
            {
                var go = new GameObject("lw_storm");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<StormFx>();
            }
        }

        private void Update()
        {
            float fade = s_wanted > 0 ? Plugin.StormFade.Value : Plugin.ClearFade.Value;
            s_progress = Mathf.MoveTowards(s_progress, s_wanted > 0 ? 1f : 0f, Time.deltaTime / Mathf.Max(0.5f, fade));
            W = s_progress * s_progress * (3f - 2f * s_progress);
            var cam = Camera.main;
            if (cam != null)
            {
                if (_rain == null)
                    _rain = MakeRain();
                _rain.transform.position = cam.transform.position + Vector3.up * 12f;
                var em = _rain.emission;
                bool sheltered = Player.m_localPlayer != null && Player.m_localPlayer.InShelter();
                em.rateOverTime = sheltered ? 0f : 2500f * W * W;
            }
        }

        private ParticleSystem MakeRain()
        {
            var go = new GameObject("lw_rain");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.startLifetime = 1.2f;
            main.startSpeed = new ParticleSystem.MinMaxCurve(16f, 22f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.02f, 0.035f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.8f, 0.9f, 0.35f), new Color(0.6f, 0.65f, 0.75f, 0.25f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 4000;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Box;
            sh.scale = new Vector3(40f, 1f, 40f);
            sh.rotation = new Vector3(90f, 0f, 0f);                       // falling down
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.06f;
            r.lengthScale = 1f;
            r.sharedMaterial = Fx.Soft;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
            return ps;
        }
    }

    [HarmonyPatch(typeof(EnvMan), "SetEnv")]
    internal static class StormBlend
    {
        private static readonly int s_rain = Shader.PropertyToID("_Rain"), s_opacity = Shader.PropertyToID("_Opacity");
        private static readonly int s_sunColor = Shader.PropertyToID("_SunColor"), s_ambientColor = Shader.PropertyToID("_AmbientColor");
        private static readonly AccessTools.FieldRef<EnvMan, Light> s_dirLight = AccessTools.FieldRefAccess<EnvMan, Light>("m_dirLight");

        private static void Postfix(EnvMan __instance)
        {
            float w = StormFx.W;
            if (w <= 0.0001f)
                return;
            var sun = s_dirLight(__instance);
            if (sun != null)
            {
                sun.intensity *= Mathf.Lerp(1f, 0.3f, w);
                sun.color = Color.Lerp(sun.color, new Color(0.62f, 0.68f, 0.8f), w);
                Shader.SetGlobalColor(s_sunColor, sun.color * sun.intensity);
            }
            RenderSettings.fogColor = Color.Lerp(RenderSettings.fogColor, new Color(0.2f, 0.22f, 0.28f), w);
            RenderSettings.fogDensity = Mathf.Lerp(RenderSettings.fogDensity, Mathf.Max(RenderSettings.fogDensity * 2.5f, 0.006f), w);
            RenderSettings.ambientLight = Color.Lerp(RenderSettings.ambientLight, new Color(0.22f, 0.24f, 0.3f), w);
            Shader.SetGlobalColor(s_ambientColor, RenderSettings.ambientLight);
            if (__instance.m_clouds != null)
            {
                var m = __instance.m_clouds.material;
                m.SetFloat(s_rain, Mathf.Lerp(m.GetFloat(s_rain), 1f, w));
                m.SetFloat(s_opacity, Mathf.Lerp(m.GetFloat(s_opacity), 1f, w));
            }
        }
    }
}
