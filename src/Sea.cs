using System.Collections.Generic;
using BepInEx.Configuration;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Sea mammals in the Ocean: humpback whales (slow, alone, long dives, surface to breathe) and orcas (faster,
    /// in pods, sometimes leap out of the water). Generated models on our own swimming skeleton; movement is ours
    /// too (owner-driven, synced with ZSyncTransform). Ambient fauna: no hit points yet.
    /// </summary>
    internal static class Sea
    {
        public const string WhalePrefab = "OceanWhale";
        public const string OrcaPrefab = "OceanOrca";

        private static ConfigEntry<int> s_whaleMax, s_orcaMax;
        private static ConfigEntry<float> s_whaleLength, s_orcaLength;

        public static void BindConfig(ConfigFile config)
        {
            s_whaleMax = config.Bind("Whale", "MaxSpawned", 1, "Max whales around a player (restart).");
            s_whaleLength = config.Bind("Whale", "Length", 14f, "Whale length, m (restart).");
            s_orcaMax = config.Bind("Orca", "MaxSpawned", 3, "Max orcas around a player (restart).");
            s_orcaLength = config.Bind("Orca", "Length", 7f, "Orca length, m (restart).");
        }

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", new Dictionary<string, string> { { "ocean_whale", "Humpback whale" }, { "ocean_orca", "Orca" } });
            loc.AddTranslation("French", new Dictionary<string, string> { { "ocean_whale", "Baleine à bosse" }, { "ocean_orca", "Orque" } });
        }

        public static void Register()
        {
            var hare = PrefabManager.Cache.GetPrefab<GameObject>("Hare");
            var template = hare != null ? hare.GetComponentInChildren<Renderer>(true).sharedMaterial : null;
            if (template == null)
            {
                Plugin.Log.LogError("Sea: no material template");
                return;
            }
            Make(WhalePrefab, "whale", "$ocean_whale", template, s_whaleLength.Value, new SeaSwimmer.Settings
            {
                Speed = 2.6f, TurnRate = 14f, Depth = 7f, MinDepth = 14f, SurfaceEvery = 50f, SurfaceFor = 9f, BreachChance = 0f,
            }, finZ: new Vector2(0.45f, 0.78f), amplitude: 10f, frequency: 0.22f);
            Make(OrcaPrefab, "orca", "$ocean_orca", template, s_orcaLength.Value, new SeaSwimmer.Settings
            {
                Speed = 5.5f, TurnRate = 35f, Depth = 4f, MinDepth = 8f, SurfaceEvery = 25f, SurfaceFor = 5f, BreachChance = 0.3f,
            }, finZ: new Vector2(0.48f, 0.75f), amplitude: 15f, frequency: 0.45f);
            AddSpawn(WhalePrefab, 16f, s_whaleMax.Value, 1, 1);
            AddSpawn(OrcaPrefab, 9f, s_orcaMax.Value, 2, 4);
        }

        private static void Make(string prefabName, string model, string hover, Material template, float length,
            SeaSwimmer.Settings settings, Vector2 finZ, float amplitude, float frequency)
        {
            var d = ModelData.Load(model);
            if (d == null)
                return;
            var go = PrefabManager.Instance.CreateEmptyPrefab(prefabName, true);
            foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<MeshRenderer>()) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<MeshFilter>()) Object.DestroyImmediate(c);
            go.AddComponent<ZSyncTransform>();

            var bones = ProcRig.SwimmerBones(d, finZ.x, finZ.y, out var allowed);
            var rig = ProcRig.Build(go, d, bones, allowed, template, length, model + "_rig");
            // centre the body on the object so it swims around its own position, not its belly
            var visual = go.transform.Find("Visual_rig");
            visual.localPosition = new Vector3(0f, -length * d.Bounds.size.y / d.Bounds.size.z * 0.5f, 0f);
            var anim = go.AddComponent<ProcSwimmer>();
            anim.Amplitude = amplitude;
            anim.BaseFrequency = frequency;
            go.AddComponent<SeaSwimmer>().Set(settings);
            var h = go.AddComponent<HoverText>();
            h.m_text = hover;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            Plugin.Log.LogInfo("Registered " + prefabName + " (" + length + " m, " + bones.Count + " bones)");
        }

        private static void AddSpawn(string prefab, float minDepth, int max, int groupMin, int groupMax)
        {
            var go = PrefabManager.Instance.GetPrefab(prefab);
            if (go == null)
                return;
            SpawnListPatch.Ensure();
            SpawnListPatch.List.m_spawners.Add(new SpawnSystem.SpawnData
            {
                m_name = prefab + "_Ocean",
                m_prefab = go,
                m_biome = Heightmap.Biome.Ocean,
                m_maxSpawned = max,
                m_spawnInterval = 240f,
                m_spawnChance = 35f,
                m_spawnDistance = 60f,
                m_groupSizeMin = groupMin,
                m_groupSizeMax = groupMax,
                m_groupRadius = 12f,
                m_spawnAtDay = true,
                m_spawnAtNight = true,
                m_minAltitude = -1000f,
                m_maxAltitude = 1000f,
                m_minOceanDepth = minDepth,
                m_maxOceanDepth = 1000f,
                m_inForest = true,
                m_outsideForest = true,
                m_groundOffset = 0f,
            });
        }
    }

    /// <summary>
    /// Owner-driven swimming: cruise at depth between waypoints in deep enough water, turn away from shallows,
    /// come up to breathe now and then (orcas may leap clear of the water), never leave the sea.
    /// </summary>
    public class SeaSwimmer : MonoBehaviour
    {
        [System.Serializable]
        public class Settings
        {
            public float Speed = 3f, TurnRate = 20f, Depth = 6f, MinDepth = 12f, SurfaceEvery = 40f, SurfaceFor = 6f, BreachChance;
        }

        public Settings S = new Settings();
        private ZNetView _nview;
        private Vector3 _target;
        private float _retarget, _surfaceTimer, _surfacing, _vy;
        private bool _breaching;

        internal void Set(Settings s) { S = s; }

        private void OnEnable() { SeaSwimmerRegistry.All.Add(this); }

        private void OnDisable() { SeaSwimmerRegistry.All.Remove(this); }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _surfaceTimer = Random.Range(S.SurfaceEvery * 0.3f, S.SurfaceEvery);
        }

        private void Start()
        {
            if (_nview != null && _nview.IsValid() && _nview.IsOwner() && ZoneSystem.instance != null)
            {
                var p = transform.position;
                p.y = Water() - S.Depth;
                transform.position = p;
                PickTarget();
            }
        }

        private static float Water() => ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;

        private static float Ground(Vector3 p) => ZoneSystem.instance.GetGroundHeight(p);

        private bool Deep(Vector3 p, float depth) => Ground(p) < Water() - depth;

        private void PickTarget()
        {
            for (int i = 0; i < 12; i++)
            {
                Vector2 c = Random.insideUnitCircle * 120f;
                var t = transform.position + new Vector3(c.x, 0f, c.y);
                if (Deep(t, S.MinDepth))
                {
                    _target = t;
                    _retarget = Random.Range(30f, 60f);
                    return;
                }
            }
            _target = transform.position - transform.forward * 40f;   // turn back
            _retarget = 15f;
        }

        private void Update()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || ZoneSystem.instance == null)
                return;
            float dt = Time.deltaTime;
            float water = Water();
            Vector3 p = transform.position;

            if (_breaching)
            {
                _vy -= 9.81f * dt;
                p += transform.forward * S.Speed * 1.4f * dt + Vector3.up * _vy * dt;
                var dir = (transform.forward * S.Speed * 1.4f + Vector3.up * _vy).normalized;
                transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(dir), dt * 6f);
                transform.position = p;
                if (_vy < 0f && p.y < water - 1.5f)
                    _breaching = false;
                return;
            }

            _retarget -= dt;
            _surfaceTimer -= dt;
            if (_retarget <= 0f || Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(_target.x, 0, _target.z)) < 10f)
                PickTarget();
            if (!Deep(p + transform.forward * 25f, S.MinDepth * 0.6f))
                PickTarget();   // shallows ahead
            if (_surfaceTimer <= 0f)
            {
                _surfaceTimer = Random.Range(S.SurfaceEvery * 0.7f, S.SurfaceEvery * 1.3f);
                _surfacing = S.SurfaceFor;
            }

            float wantY = water - S.Depth;
            if (_surfacing > 0f)
            {
                _surfacing -= dt;
                wantY = water - 0.7f;
                if (S.BreachChance > 0f && p.y > water - 1.5f && Random.value < S.BreachChance * dt / Mathf.Max(S.SurfaceFor, 0.1f) * 3f)
                {
                    _breaching = true;
                    _vy = Random.Range(7f, 9f);
                    _surfacing = 0f;
                    return;
                }
            }
            wantY = Mathf.Max(wantY, Ground(p) + 3f);

            Vector3 flat = new Vector3(_target.x - p.x, 0f, _target.z - p.z).normalized;
            float climb = Mathf.Clamp((wantY - p.y) * 0.25f, -0.45f, 0.45f);
            var wanted = Quaternion.LookRotation((flat + Vector3.up * climb).normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, S.TurnRate * dt);
            p += transform.forward * S.Speed * dt;
            p.y = Mathf.Min(p.y, water - 0.4f);
            transform.position = p;
        }
    }

    internal static class SeaSwimmerRegistry
    {
        internal static readonly List<SeaSwimmer> All = new List<SeaSwimmer>();
    }

    /// <summary>Removes a local effect object (e.g. a corpse) after a while.</summary>
    public class SelfDestruct : MonoBehaviour
    {
        public float Seconds = 10f;

        private void Start() { Destroy(gameObject, Seconds); }
    }
}
