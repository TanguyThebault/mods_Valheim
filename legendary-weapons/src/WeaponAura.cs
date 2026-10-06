using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// The aura IS the cooldown: a legendary held in a hand glows with its element (runes, storm, wind, earth, leaves,
    /// sky, mist, embers, frost) only while its power is ready, and not at all while it recharges; it flares up the
    /// moment the power comes back. The rest status effects still time the cooldowns but are hidden from the HUD
    /// ([Aura] HideCooldownIcons). On every client: the local player's readiness goes in their ZDO (lw_aura_ready).
    /// Brighter with the upgrade level; [Aura] Strength scales it all.
    /// </summary>
    internal static class WeaponAuras
    {
        internal enum Element { Rune, Storm, Wind, Earth, Leaves, Sky, Mist, Ember, Frost }

        internal class Info
        {
            public Element Element;
            public string Rest;                  // the power's rest status effect (cooldown), null if none
        }

        internal static readonly Dictionary<string, Info> ByPrefab = new Dictionary<string, Info>
        {
            { Plugin.ItemPrefab, new Info { Element = Element.Rune } },
            { Plugin.SpearPrefab, new Info { Element = Element.Storm, Rest = "SE_LW_StormRest" } },
            { Plugin.SwordPrefab, new Info { Element = Element.Wind, Rest = "SE_LW_Rest_item_swordwind" } },
            { Plugin.MacePrefab, new Info { Element = Element.Earth, Rest = "SE_LW_Rest_item_maceearth" } },
            { Plugin.KnifePrefab, new Info { Element = Element.Leaves, Rest = "SE_LW_FoxRest" } },
            { Plugin.RodPrefab, new Info { Element = Element.Sky, Rest = "SE_LW_SkyRest" } },
            { Plugin.HornPrefab, new Info { Element = Element.Mist, Rest = "SE_LW_HornRest" } },
            { Plugin.SurtrPrefab, new Info { Element = Element.Ember, Rest = "SE_LW_Rest_item_swordsurtr" } },
            { Plugin.YmirPrefab, new Info { Element = Element.Frost, Rest = "SE_LW_Rest_item_atgeirymir" } },
        };

        private static Dictionary<int, string> s_byHash;
        private static readonly AccessTools.FieldRef<VisEquipment, int> s_rightItem = AccessTools.FieldRefAccess<VisEquipment, int>("m_rightItem");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_rightInstance = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static float s_next;
        private static readonly int s_readyHash = "lw_aura_ready".GetStableHashCode();
        private static readonly HashSet<int> s_restHashes = new HashSet<int>();

        /// <summary>Our cooldown effects, kept off the HUD (the aura shows the cooldown instead).</summary>
        internal static bool IsRest(StatusEffect se)
        {
            if (s_restHashes.Count == 0)
                foreach (var i in ByPrefab.Values)
                    if (i.Rest != null) s_restHashes.Add(i.Rest.GetStableHashCode());
            return se != null && s_restHashes.Contains(se.NameHash());
        }

        /// <summary>The element's colours (also the icons' halo).</summary>
        internal static Color Main(Element e)
        {
            switch (e)
            {
                case Element.Rune: return new Color(1f, 0.62f, 0.22f);
                case Element.Storm: return new Color(0.55f, 0.75f, 1f);
                case Element.Wind: return new Color(0.88f, 0.95f, 1f);
                case Element.Earth: return new Color(0.2f, 0.18f, 0.15f);
                case Element.Leaves: return new Color(0.75f, 0.45f, 0.18f);
                case Element.Sky: return new Color(0.6f, 0.82f, 1f);
                case Element.Mist: return new Color(0.68f, 0.78f, 0.92f);
                case Element.Ember: return new Color(1f, 0.42f, 0.1f);
                default: return new Color(0.72f, 0.9f, 1f);
            }
        }

        public static void Tick()
        {
            if (Time.time < s_next)
                return;
            s_next = Time.time + 0.2f;
            if (s_byHash == null)
            {
                s_byHash = new Dictionary<int, string>();
                foreach (var k in ByPrefab.Keys) s_byHash[k.GetStableHashCode()] = k;
            }
            foreach (var p in Player.GetAllPlayers())
            {
                var vis = p != null ? p.GetComponent<VisEquipment>() : null;
                if (vis == null)
                    continue;
                var inst = s_rightInstance(vis);
                if (inst == null || !s_byHash.TryGetValue(s_rightItem(vis), out var prefab))
                    continue;
                var fx = inst.GetComponent<AuraFx>();
                if (fx == null)
                {
                    fx = inst.AddComponent<AuraFx>();
                    fx.Setup(ByPrefab[prefab].Element, prefab);
                }
                fx.Strength = Strength(p, prefab);
            }
        }

        /// <summary>0 while the power recharges; once ready, by upgrade level (local: read here and shared via the ZDO).</summary>
        private static float Strength(Player p, string prefab)
        {
            int quality = 1;
            bool ready = true;
            var nview = p.GetComponent<ZNetView>();
            if (p == Player.m_localPlayer)
            {
                var w = p.GetCurrentWeapon();
                if (w != null) quality = w.m_quality;
                var rest = ByPrefab[prefab].Rest;
                if (rest != null && p.GetSEMan().GetStatusEffect(rest.GetStableHashCode()) != null)
                    ready = false;
                if (nview != null && nview.IsValid() && nview.GetZDO().GetBool(s_readyHash, true) != ready)
                    nview.GetZDO().Set(s_readyHash, ready);
            }
            else if (nview != null && nview.IsValid())
                ready = nview.GetZDO().GetBool(s_readyHash, true);
            if (!ready)
                return 0f;
            float level = Mathf.Lerp(0.75f, 1f, (Mathf.Clamp(quality, 1, 4) - 1) / 3f);
            return level * Plugin.AuraStrength.Value;
        }
    }

    /// <summary>The cooldown icons stay off the HUD: the aura tells when a power is ready.</summary>
    [HarmonyPatch(typeof(SEMan), nameof(SEMan.GetHUDStatusEffects))]
    internal static class HideRestIcons
    {
        private static void Postfix(List<StatusEffect> effects)
        {
            if (Plugin.HideCooldownIcons.Value)
                effects.RemoveAll(WeaponAuras.IsRest);
        }
    }

    /// <summary>The particles of one held weapon: born in its bounding box, drifting after its element.</summary>
    internal class AuraFx : MonoBehaviour
    {
        internal float Strength = 0.5f;
        private float _shown = 0f;                    // 0 at first: drawn with its power ready, the weapon flares
        private int _arcFlurry;
        private Light _light;
        private ParticleSystem _ps;
        private WeaponAuras.Element _e;
        private float _rate, _alpha;
        private Color _a, _b;

        private Vector3 _axis, _headCentre, _headSize;
        private float _quiet = 1f;                     // per element: how much it shows next to the others
        private LineRenderer[] _arcs;
        private float _nextArc;
        private readonly float[] _arcUntil = new float[2];

        private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        internal void Setup(WeaponAuras.Element e, string prefab)
        {
            _e = e;
            if (!Geometry.LocalBounds(transform, transform, out var bounds))
                bounds = new Bounds(Vector3.up * 0.5f, new Vector3(0.1f, 1f, 0.1f));
            var go = new GameObject("lw_aura");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = bounds.center;
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = Vector3.Max(bounds.size * 0.9f, Vector3.one * 0.02f);
            // the long axis and the head (the end away from the hand, which is the weapon's origin)
            var ext = bounds.extents;
            int k = ext.x >= ext.y && ext.x >= ext.z ? 0 : (ext.y >= ext.z ? 1 : 2);
            _axis = k == 0 ? Vector3.right : k == 1 ? Vector3.up : Vector3.forward;
            float half = k == 0 ? ext.x : k == 1 ? ext.y : ext.z;
            if (WeaponModels.Heads.TryGetValue(prefab, out var head) ? Vector3.Dot(head, _axis) < 0f : Vector3.Dot(bounds.center, _axis) < 0f)
                _axis = -_axis;
            Vector3 headEnd = bounds.center + _axis * half;
            if (Vector3.Dot(headEnd - bounds.center, _axis) < 0f) headEnd = bounds.center - _axis * half;
            _headCentre = bounds.center + _axis * half * (e == WeaponAuras.Element.Wind ? 0.05f : 0.72f);
            _headSize = Vector3.Max(Vector3.Scale(bounds.size, Vector3.one - Abs(_axis)) * 0.9f + Abs(_axis) * half * 0.56f, Vector3.one * 0.02f);
            if (e == WeaponAuras.Element.Frost || e == WeaponAuras.Element.Storm || e == WeaponAuras.Element.Wind)
            {
                go.transform.localPosition = _headCentre;           // on the blade only, not along the whole haft
                shape.scale = _headSize;
            }
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(0.7f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var noise = _ps.noise;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Fx.Soft;
            Color main1 = WeaponAuras.Main(e);
            _a = main1;
            _b = Color.Lerp(main1, Color.white, 0.5f);
            _alpha = 0.8f;
            switch (e)
            {
                case WeaponAuras.Element.Rune:
                    Set(main, 0.8f, 1.4f, 0.05f, 0.15f, 0.015f, 0.035f, -0.05f); _rate = 12f;
                    break;
                case WeaponAuras.Element.Storm:
                    Set(main, 0.1f, 0.28f, 0.4f, 1.6f, 0.018f, 0.04f, 0f); _rate = 45f; _alpha = 1f;
                    _b = new Color(0.95f, 0.98f, 1f);
                    noise.enabled = true; noise.strength = 3f; noise.frequency = 6f;
                    var sparks = _ps.trails;
                    sparks.enabled = true;
                    sparks.ratio = 0.5f;
                    sparks.lifetime = new ParticleSystem.MinMaxCurve(0.06f);
                    sparks.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
                    sparks.dieWithParticles = true;
                    r.trailMaterial = Fx.Plain;
                    break;
                case WeaponAuras.Element.Wind:
                    // faint threads of air sliding along the blade to its tip and curling off it
                    Set(main, 0.45f, 0.8f, 0f, 0f, 0.004f, 0.008f, 0f); _rate = 9f; _alpha = 0.35f;
                    _a = new Color(0.85f, 0.94f, 1f);
                    _b = Color.white;
                    var flow = _ps.velocityOverLifetime;
                    flow.enabled = true;
                    flow.space = ParticleSystemSimulationSpace.Local;
                    flow.x = new ParticleSystem.MinMaxCurve(_axis.x * 0.9f, _axis.x * 1.6f);
                    flow.y = new ParticleSystem.MinMaxCurve(_axis.y * 0.9f, _axis.y * 1.6f);
                    flow.z = new ParticleSystem.MinMaxCurve(_axis.z * 0.9f, _axis.z * 1.6f);
                    noise.enabled = true; noise.strength = 0.25f; noise.frequency = 2f;
                    var trails = _ps.trails;
                    trails.enabled = true;
                    trails.ratio = 1f;
                    trails.lifetime = new ParticleSystem.MinMaxCurve(0.35f);
                    trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.3f, 1f), new Keyframe(1f, 0f)));
                    trails.inheritParticleColor = true;
                    trails.dieWithParticles = true;
                    trails.minVertexDistance = 0.02f;
                    r.trailMaterial = Fx.Plain;
                    r.sharedMaterial = Fx.Soft;
                    break;
                case WeaponAuras.Element.Earth:
                    Set(main, 0.8f, 1.4f, 0.05f, 0.15f, 0.015f, 0.035f, -0.05f); _rate = 12f; _alpha = 0.9f;
                    _a = new Color(0.08f, 0.07f, 0.06f);
                    _b = new Color(0.22f, 0.2f, 0.17f);
                    break;
                case WeaponAuras.Element.Leaves:
                    Set(main, 1.2f, 2f, 0f, 0.1f, 0.03f, 0.05f, 0.05f); _rate = 3.5f; _alpha = 0.9f;
                    r.sharedMaterial = Fx.Leaf;
                    main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                    var rot = _ps.rotationOverLifetime;
                    rot.enabled = true;
                    rot.z = new ParticleSystem.MinMaxCurve(-3f, 3f);
                    break;
                case WeaponAuras.Element.Sky:
                    Set(main, 1f, 1.8f, 0.05f, 0.2f, 0.02f, 0.04f, -0.04f); _rate = 7f; _alpha = 0.6f;
                    break;
                case WeaponAuras.Element.Mist:
                    Set(main, 1.2f, 2f, 0.03f, 0.12f, 0.12f, 0.25f, -0.01f); _rate = 4f; _alpha = 0.12f;
                    break;
                case WeaponAuras.Element.Ember:
                    Set(main, 0.7f, 1.4f, 0.1f, 0.4f, 0.012f, 0.03f, -0.18f); _rate = 16f; _alpha = 1f;
                    _b = new Color(1f, 0.8f, 0.35f);
                    noise.enabled = true; noise.strength = 0.5f; noise.frequency = 1.2f;
                    break;
                default:
                    Set(main, 0.9f, 1.6f, 0.02f, 0.1f, 0.012f, 0.03f, 0.03f); _rate = 11f; _alpha = 0.9f;
                    var twinkle = _ps.sizeOverLifetime;
                    twinkle.enabled = true;
                    twinkle.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.5f), new Keyframe(0.3f, 1.2f),
                        new Keyframe(0.55f, 0.6f), new Keyframe(0.8f, 1.1f), new Keyframe(1f, 0.3f)));
                    break;
            }
            // more visible than a hint: twice as dense, a third bigger
            _rate *= 2f;
            // Lekinox: the knife, the wind blade, the axe and the spear showed far too much
            switch (e)
            {
                case WeaponAuras.Element.Leaves: case WeaponAuras.Element.Rune: _quiet = 0.3f; break;
                case WeaponAuras.Element.Wind: _quiet = 0.38f; break;         // faint threads
                case WeaponAuras.Element.Storm: _quiet = 0.35f; break;
            }
            _rate *= _quiet;
            if (e == WeaponAuras.Element.Storm)
            {
                BuildArcs();
                _rate = 0f;                                  // the arcs only (Lekinox): no sparks
            }
            _alpha = Mathf.Min(1f, _alpha * 1.4f);
            var mm = _ps.main;
            mm.startSize = new ParticleSystem.MinMaxCurve(mm.startSize.constantMin * 1.35f, mm.startSize.constantMax * 1.35f);
            mm.maxParticles = 300;
            if (e != WeaponAuras.Element.Earth && e != WeaponAuras.Element.Leaves && e != WeaponAuras.Element.Mist)
            {
                var lg = new GameObject("lw_aura_light");
                lg.transform.SetParent(go.transform, false);
                _light = lg.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = Color.Lerp(main1, Color.white, 0.2f);
                _light.range = 1.6f;
                _light.intensity = 0f;
                _light.shadows = LightShadows.None;
            }
            var em = _ps.emission;
            em.rateOverTime = 0f;
            _ps.Play();
        }

        /// <summary>Storm: thin, jagged little arcs jumping across the spearhead now and then.</summary>
        private void BuildArcs()
        {
            _arcs = new LineRenderer[2];
            for (int i = 0; i < _arcs.Length; i++)
            {
                var go = new GameObject("lw_arc");
                go.transform.SetParent(transform, false);
                var lr = go.AddComponent<LineRenderer>();
                lr.sharedMaterial = Fx.Plain;
                lr.useWorldSpace = true;
                lr.positionCount = 7;
                lr.widthMultiplier = 0.012f;
                lr.numCapVertices = 1;
                lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                lr.receiveShadows = false;
                lr.enabled = false;
                _arcs[i] = lr;
            }
        }

        private void Arcs(float k)
        {
            for (int i = 0; i < _arcs.Length; i++)
                if (_arcs[i].enabled && Time.time >= _arcUntil[i])
                    _arcs[i].enabled = false;
            if (k <= 0f || Time.time < _nextArc)
                return;
            _nextArc = _arcFlurry > 0 ? Time.time + 0.05f : Time.time + Random.Range(0.12f, 0.5f) / Mathf.Max(0.3f, k);
            if (_arcFlurry > 0) _arcFlurry--;
            int n = Random.Range(0, _arcs.Length);
            var lr = _arcs[n];
            Vector3 a = _headCentre + Vector3.Scale(_headSize, new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f)));
            Vector3 b = _headCentre + Vector3.Scale(_headSize, new Vector3(Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f), Random.Range(-0.5f, 0.5f)));
            for (int j = 0; j < lr.positionCount; j++)
            {
                float t = j / (float)(lr.positionCount - 1);
                Vector3 p = Vector3.Lerp(a, b, t);
                if (j > 0 && j < lr.positionCount - 1)
                    p += Random.insideUnitSphere * 0.03f;
                lr.SetPosition(j, transform.TransformPoint(p));
            }
            var c = Color.Lerp(_a, Color.white, Random.Range(0.4f, 0.9f));
            lr.startColor = lr.endColor = new Color(c.r, c.g, c.b, 0.9f);
            lr.enabled = true;
            _arcUntil[n] = Time.time + Random.Range(0.04f, 0.09f);
        }

        /// <summary>The power is back: a quick flare of the element round the weapon.</summary>
        private void Flare()
        {
            var main = _ps.main;
            var emit = new ParticleSystem.EmitParams
            {
                startColor = new Color(_b.r, _b.g, _b.b, Mathf.Min(1f, _alpha * 1.2f)),
            };
            _ps.Emit(emit, 40);
        }

        private static void Set(ParticleSystem.MainModule main, float life0, float life1, float speed0, float speed1,
            float size0, float size1, float gravity)
        {
            main.startLifetime = new ParticleSystem.MinMaxCurve(life0, life1);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed0, speed1);
            main.startSize = new ParticleSystem.MinMaxCurve(size0, size1);
            main.gravityModifier = gravity;
        }

        private void Update()
        {
            if (_ps == null)
                return;
            float k = Strength;
            if (_shown == 0f && k > 0f)
            {
                if (_arcs == null) Flare();
                else _arcFlurry = 5;
            }
            _shown = k;
            // ready: a slow breathing pulse
            if (k > 0f) k *= 0.85f + 0.15f * Mathf.Sin(Time.time * 2.2f);
            if (_light != null) _light.intensity = 0.45f * k * Mathf.Sqrt(_quiet);
            if (_arcs != null) Arcs(k);
            var em = _ps.emission;
            em.rateOverTime = _rate * k;
            var main = _ps.main;
            float a = _alpha * Mathf.Lerp(0.6f, 1f, k) * Mathf.Lerp(0.6f, 1f, _quiet);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(_a.r, _a.g, _a.b, a), new Color(_b.r, _b.g, _b.b, a));
        }
    }
}
