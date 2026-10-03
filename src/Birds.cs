using System.Collections.Generic;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ThrowingAxe
{
    /// <summary>
    /// Perching birds built on the vanilla RandomFlyingBird (the crow's flight, landing and "fly off when a
    /// player comes close" logic), each as its own prefab:
    /// - Meadow sparrow: tiny, brown with a red breast, sings, drops nothing;
    /// - Black Forest crow: croaks (the crow's own calls), keeps its feathers.
    /// Both only perch on things (rocks, trunks, roofs, fences), never the ground, and fly at least
    /// MinClearance above the ground except on the last metres to a perch.
    /// </summary>
    internal static class Birds
    {
        public const string SparrowPrefab = "MeadowSparrow";
        public const string CrowPrefab = "BlackForestCrow";

        internal static readonly List<AudioClip> Songs = new List<AudioClip>();
        private static ConfigEntry<float> s_sparrowScale;
        private static ConfigEntry<int> s_sparrowMax;
        private static ConfigEntry<int> s_crowMax;
        internal static ConfigEntry<float> SongVolume;
        internal static ConfigEntry<float> SongInterval;
        internal static ConfigEntry<float> MinClearance;

        public static void BindConfig(ConfigFile config)
        {
            s_sparrowScale = config.Bind("Sparrow", "Scale", 0.3f, "Size relative to the vanilla crow (restart).");
            s_sparrowMax = config.Bind("Sparrow", "MaxSpawned", 4, "Max sparrows around a player (restart).");
            SongVolume = config.Bind("Sparrow", "SongVolume", 0.5f, "Song volume, 0-1 (live).");
            SongInterval = config.Bind("Sparrow", "SongInterval", 11f,
                "Average seconds between two songs of a perched sparrow; about 2.5x longer in flight (live).");
            s_crowMax = config.Bind("Crow", "MaxSpawned", 3, "Max Black Forest crows around a player (restart).");
            MinClearance = config.Bind("Birds", "MinClearance", 2f, "Minimum flight height above the ground, m (live).");
        }

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", "sparrow", "Sparrow");
            loc.AddTranslation("French", "sparrow", "Moineau");
            loc.AddTranslation("English", "blackforest_crow", "Crow");
            loc.AddTranslation("French", "blackforest_crow", "Corbeau");
        }

        public static void Register()
        {
            var crow = PrefabManager.Cache.GetPrefab<GameObject>("Crow");
            if (crow == null || crow.GetComponent<RandomFlyingBird>() == null)
            {
                Plugin.Log.LogError("Vanilla Crow not found: birds disabled");
                return;
            }
            RegisterSparrow(crow);
            RegisterCrow(crow);
        }

        private static void RegisterSparrow(GameObject crow)
        {
            Songs.Clear();
            Songs.AddRange(Look.LoadClips("sfx", "sparrow_song"));

            var go = PrefabManager.Instance.CreateClonedPrefab(SparrowPrefab, crow);
            go.transform.localScale *= s_sparrowScale.Value;
            var bird = go.GetComponent<RandomFlyingBird>();
            bird.m_flyRange = 18f;
            bird.m_minAlt = 3f;
            bird.m_maxAlt = 7f;
            bird.m_speed = 7f;
            bird.m_turnRate = 30f;       // flits
            bird.m_wpDuration = 2.5f;
            bird.m_flapDuration = 1.2f;
            bird.m_sailDuration = 0.6f;  // mostly flapping, short glides
            bird.m_landChance = 0.6f;
            bird.m_landDuration = 10f;
            bird.m_avoidDangerDistance = 5f;
            bird.m_randomNoise = new EffectList(); // no crow calls; SparrowSong sings instead

            foreach (var c in go.GetComponentsInChildren<DropOnDestroyed>(true)) Object.DestroyImmediate(c);
            foreach (var d in go.GetComponentsInChildren<Destructible>(true))
            {
                d.m_health = 1f;
                d.m_destroyedEffect = OwnFeathers(d.m_destroyedEffect, SparrowPrefab, new Color(0.55f, 0.40f, 0.26f));
            }

            Look.Paint(go, Look.Mask("sparrow"), new Color(0.55f, 0.40f, 0.26f));
            go.AddComponent<PerchBird>();
            go.AddComponent<SparrowSong>();
            SetHover(go, "$sparrow");
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            AddSpawn(go, Heightmap.Biome.Meadows, s_sparrowMax.Value, 3);
            Plugin.Log.LogInfo("Registered " + SparrowPrefab + ", " + Songs.Count + " songs");
        }

        private static void RegisterCrow(GameObject crow)
        {
            var go = PrefabManager.Instance.CreateClonedPrefab(CrowPrefab, crow);
            var bird = go.GetComponent<RandomFlyingBird>();
            bird.m_flyRange = 25f;
            bird.m_minAlt = 4f;
            bird.m_maxAlt = 12f;
            bird.m_speed = 9f;
            bird.m_landChance = 0.5f;
            bird.m_landDuration = 14f;
            bird.m_avoidDangerDistance = 7f;
            bird.m_randomNoiseIntervalMin = 6f;   // croaks now and then
            bird.m_randomNoiseIntervalMax = 14f;
            foreach (var d in go.GetComponentsInChildren<Destructible>(true))
                d.m_destroyedEffect = OwnFeathers(d.m_destroyedEffect, CrowPrefab, Color.black);
            go.AddComponent<PerchBird>();
            SetHover(go, "$blackforest_crow");
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            AddSpawn(go, Heightmap.Biome.BlackForest, s_crowMax.Value, 2);
            Plugin.Log.LogInfo("Registered " + CrowPrefab + " (croaks: " + bird.m_randomNoise.m_effectPrefabs.Length + " effects)");
        }

        private static void SetHover(GameObject go, string text)
        {
            var hover = go.GetComponent<HoverText>();
            if (hover != null)
                hover.m_text = text;
        }

        /// <summary>Our own copy of the death puff, its particles recoloured (sparrows don't lose black feathers).</summary>
        private static EffectList OwnFeathers(EffectList list, string prefix, Color color)
        {
            if (list?.m_effectPrefabs == null)
                return list;
            var result = new EffectList { m_effectPrefabs = new EffectList.EffectData[list.m_effectPrefabs.Length] };
            for (int i = 0; i < list.m_effectPrefabs.Length; i++)
            {
                var ed = list.m_effectPrefabs[i];
                var copy = new EffectList.EffectData
                {
                    m_prefab = ed.m_prefab, m_enabled = ed.m_enabled, m_variant = ed.m_variant, m_attach = ed.m_attach,
                    m_follow = ed.m_follow, m_inheritParentRotation = ed.m_inheritParentRotation,
                    m_inheritParentScale = ed.m_inheritParentScale, m_randomRotation = ed.m_randomRotation, m_scale = ed.m_scale,
                    m_multiplyParentVisualScale = ed.m_multiplyParentVisualScale,
                };
                if (ed.m_prefab != null && ed.m_prefab.GetComponentInChildren<ParticleSystem>(true) != null)
                {
                    var vfx = PrefabManager.Instance.CreateClonedPrefab(prefix + "_" + ed.m_prefab.name, ed.m_prefab);
                    foreach (var ps in vfx.GetComponentsInChildren<ParticleSystem>(true))
                    {
                        var main = ps.main;
                        main.startColor = new ParticleSystem.MinMaxGradient(color * 0.85f, color * 1.15f);
                    }
                    PrefabManager.Instance.AddPrefab(new CustomPrefab(vfx, true));
                    copy.m_prefab = vfx;
                    copy.m_inheritParentScale = true;  // a sparrow's puff is sparrow-sized
                }
                result.m_effectPrefabs[i] = copy;
            }
            return result;
        }

        private static void AddSpawn(GameObject prefab, Heightmap.Biome biome, int max, int group)
        {
            SpawnListPatch.Ensure();
            SpawnListPatch.List.m_spawners.Add(new SpawnSystem.SpawnData
            {
                m_name = prefab.name + "_" + biome,
                m_prefab = prefab,
                m_biome = biome,
                m_maxSpawned = max,
                m_spawnInterval = 60f,
                m_spawnChance = 50f,
                m_spawnDistance = 20f,
                m_groupSizeMin = 1,
                m_groupSizeMax = group,
                m_groupRadius = 4f,
                m_spawnAtDay = true,
                m_spawnAtNight = false,
                m_minAltitude = 1f,
                m_inForest = true,
                m_outsideForest = true,
                m_groundOffset = 6f,      // appear in the air, never on the ground
                m_groundOffsetRandom = 3f,
            });
        }
    }

    /// <summary>Marks our birds for the perch and clearance patches.</summary>
    public class PerchBird : MonoBehaviour
    {
    }

    /// <summary>Sings the sparrow songs.</summary>
    public class SparrowSong : MonoBehaviour
    {
        private ZNetView _nview;
        private AudioSource _source;
        private float _timer;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _source = gameObject.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 1f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 3f;
            _source.maxDistance = 35f;
            _source.dopplerLevel = 0f;
            _source.volume = Birds.SongVolume.Value;
            _timer = Random.Range(1f, 8f);
        }

        private void Start()
        {
            if (AudioMan.instance != null)
                _source.outputAudioMixerGroup = AudioMan.instance.m_ambientMixer; // follows the ambient volume slider
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f)
                return;
            bool landed = _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(ZDOVars.s_landed);
            float mean = Birds.SongInterval.Value * (landed ? 1f : 2.5f);
            _timer = Random.Range(mean * 0.55f, mean * 1.45f);
            if (!EnvMan.IsDaylight() && Random.value > 0.15f)
                return;
            if (Birds.Songs.Count == 0 || _source.isPlaying)
                return;
            _source.pitch = Random.Range(0.92f, 1.1f);
            _source.volume = Birds.SongVolume.Value;
            _source.PlayOneShot(Birds.Songs[Random.Range(0, Birds.Songs.Count)]);
        }
    }

    /// <summary>
    /// Replaces RandomFlyingBird.FindLandingPoint for our birds: look down at random spots around home and only
    /// accept the top of something (rock, trunk, roof, fence...), never the terrain or water. No perch found:
    /// the vanilla code just keeps flying.
    /// </summary>
    [HarmonyPatch(typeof(RandomFlyingBird), "FindLandingPoint")]
    internal static class BirdPerch
    {
        private static readonly AccessTools.FieldRef<RandomFlyingBird, Vector3> s_spawnPoint =
            AccessTools.FieldRefAccess<RandomFlyingBird, Vector3>("m_spawnPoint");
        private static int s_mask;

        private static bool Prefix(RandomFlyingBird __instance, ref Vector3 waypoint, ref bool __result)
        {
            if (__instance.GetComponent<PerchBird>() == null)
                return true;
            if (s_mask == 0)
                s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");

            __result = false;
            waypoint = new Vector3(0f, -999f, 0f);
            Vector3 home = s_spawnPoint(__instance);
            for (int i = 0; i < 24; i++)
            {
                Vector2 c = Random.insideUnitCircle * __instance.m_flyRange;
                Vector3 origin = new Vector3(home.x + c.x, Mathf.Max(home.y, __instance.transform.position.y) + 40f, home.z + c.y);
                if (!Physics.Raycast(origin, Vector3.down, out var hit, 120f, s_mask, QueryTriggerInteraction.Ignore))
                    continue;
                if (hit.collider.GetComponentInParent<Heightmap>() != null)
                    continue; // ground
                if (hit.normal.y < 0.7f)
                    continue; // too steep to sit on
                float ground = ZoneSystem.instance.GetGroundHeight(hit.point);
                if (hit.point.y - ground < 0.5f)
                    continue; // basically the ground (flat stones, floor planks at grade)
                if (hit.point.y < ZoneSystem.instance.m_waterLevel + 0.3f)
                    continue;
                if (Player.IsPlayerInRange(hit.point, __instance.m_avoidDangerDistance))
                    continue;
                waypoint = hit.point;
                __result = true;
                return false;
            }
            return false;
        }
    }

    /// <summary>
    /// The vanilla bird flies straight at its waypoint and can dip into hills on the way. Keep our birds at
    /// least MinClearance above the ground (or water), except on the final approach to a perch.
    /// </summary>
    [HarmonyPatch(typeof(RandomFlyingBird), nameof(RandomFlyingBird.CustomFixedUpdate))]
    internal static class BirdClearance
    {
        private static readonly AccessTools.FieldRef<RandomFlyingBird, bool> s_groundwp =
            AccessTools.FieldRefAccess<RandomFlyingBird, bool>("m_groundwp");
        private static readonly AccessTools.FieldRef<RandomFlyingBird, Vector3> s_waypoint =
            AccessTools.FieldRefAccess<RandomFlyingBird, Vector3>("m_waypoint");
        private static readonly AccessTools.FieldRef<RandomFlyingBird, ZNetView> s_nview =
            AccessTools.FieldRefAccess<RandomFlyingBird, ZNetView>("m_nview");

        private static void Postfix(RandomFlyingBird __instance, float dt)
        {
            if (__instance.GetComponent<PerchBird>() == null)
                return;
            var nview = s_nview(__instance);
            if (nview == null || !nview.IsValid() || !nview.IsOwner() || nview.GetZDO().GetBool(ZDOVars.s_landed))
                return;
            Vector3 p = __instance.transform.position;
            if (s_groundwp(__instance))
            {
                Vector3 wp = s_waypoint(__instance);
                if (new Vector2(wp.x - p.x, wp.z - p.z).magnitude < 4f)
                    return; // landing on a perch
            }
            float floor = Mathf.Max(ZoneSystem.instance.GetGroundHeight(p), ZoneSystem.instance.m_waterLevel);
            float min = floor + Birds.MinClearance.Value;
            if (p.y >= min)
                return;
            p.y = p.y < floor + 0.3f ? floor + 0.3f : p.y;
            p.y = Mathf.Min(min, p.y + 12f * dt); // climb back smoothly
            __instance.transform.position = p;
        }
    }

    /// <summary>
    /// Our own spawn list, added to every SpawnSystem (one per zone) as it wakes up. The first time, logs the
    /// vanilla spawners of the Black Forest (to check the creature list against the real game data).
    /// </summary>
    [HarmonyPatch(typeof(SpawnSystem), "Awake")]
    internal static class SpawnListPatch
    {
        internal static SpawnSystemList List;
        private static bool s_dumped;

        internal static void Ensure()
        {
            if (List != null)
                return;
            var holder = new GameObject("ThrowingAxe_spawnlist");
            Object.DontDestroyOnLoad(holder);
            List = holder.AddComponent<SpawnSystemList>();
        }

        private static void Postfix(SpawnSystem __instance)
        {
            if (List != null && !__instance.m_spawnLists.Contains(List))
                __instance.m_spawnLists.Add(List);
            if (s_dumped)
                return;
            s_dumped = true;
            var sb = new StringBuilder("Black Forest spawners:");
            foreach (var list in __instance.m_spawnLists)
                foreach (var s in list.m_spawners.Where(s => s.m_enabled && s.m_prefab != null &&
                                                             (s.m_biome & Heightmap.Biome.BlackForest) != 0))
                    sb.Append("\n  " + s.m_prefab.name + " (" + s.m_name + ") max " + s.m_maxSpawned + ", " +
                              (s.m_spawnAtDay ? "day" : "") + (s.m_spawnAtNight ? " night" : "") +
                              (string.IsNullOrEmpty(s.m_requiredGlobalKey) ? "" : ", needs " + s.m_requiredGlobalKey) +
                              (s.m_requiredEnvironments.Count > 0 ? ", weather " + string.Join("/", s.m_requiredEnvironments) : ""));
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}
