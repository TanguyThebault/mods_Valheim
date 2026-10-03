using System.Collections.Generic;
using System.Linq;
using BepInEx.Configuration;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Sea mammals in the Ocean: humpback whales (slate grey, slow, alone, long dives) and orcas (black and white,
    /// faster, in pods, sometimes leap out). Generated models on our own swimming skeleton; movement is ours too
    /// (owner-driven, synced with ZSyncTransform). When they surface they blow: a mist spout from the blowhole
    /// and the sound of the breath, seen and heard by everyone (RPC). Ambient fauna: no hit points yet.
    /// </summary>
    internal static class Sea
    {
        public const string WhalePrefab = "OceanWhale";
        public const string OrcaPrefab = "OceanOrca";

        private static ConfigEntry<int> s_whaleMax, s_orcaMax;
        private static ConfigEntry<float> s_whaleLength, s_orcaLength, s_whaleChance, s_orcaChance, s_blowVolume;
        internal static readonly Dictionary<string, AudioClip[]> Blows = new Dictionary<string, AudioClip[]>();
        internal static float BlowVolume => s_blowVolume.Value;

        public static void BindConfig(ConfigFile config)
        {
            s_whaleMax = config.Bind("Whale", "MaxSpawned", 1, "Max whales around a player (restart).");
            s_whaleChance = config.Bind("Whale", "SpawnChance", 10f, "Chance per spawn check (every 15 min), % (restart).");
            s_whaleLength = config.Bind("Whale", "Length", 14f, "Whale length, m (restart).");
            s_orcaMax = config.Bind("Orca", "MaxSpawned", 3, "Max orcas around a player (restart).");
            s_orcaChance = config.Bind("Orca", "SpawnChance", 12f, "Chance per spawn check (every 15 min), % (restart).");
            s_orcaLength = config.Bind("Orca", "Length", 7f, "Orca length, m (restart).");
            s_blowVolume = config.Bind("Sea", "BlowVolume", 0.9f, "Volume of the blowhole spout, 0-1 (live).");
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
            Blows["whale"] = Look.LoadClips("sfx_sea", "whale_blow");
            Blows["orca"] = Look.LoadClips("sfx_sea", "orca_blow");
            var mist = FindMistMaterial();

            Make(WhalePrefab, "whale", "$ocean_whale", template, mist, s_whaleLength.Value, new SeaSwimmer.Settings
            {
                Speed = 2.4f, TurnRate = 12f, Depth = 7f, MinDepth = 14f, SurfaceEvery = 55f, SurfaceFor = 10f,
                BreachChance = 0f, Wander = 25f, Species = "whale",
            }, finZ: new Vector2(0.45f, 0.8f), anim: a =>
            {
                a.Amplitude = 18f; a.Horizontal = 8f; a.BaseFrequency = 0.2f; a.Exponent = 1.25f; a.WaveNumber = 1.8f; a.FlukeBoost = 1.9f;
            }, spout: new Vector2(6.5f, 1.4f));
            Make(OrcaPrefab, "orca", "$ocean_orca", template, mist, s_orcaLength.Value, new SeaSwimmer.Settings
            {
                Speed = 5.5f, TurnRate = 32f, Depth = 4f, MinDepth = 8f, SurfaceEvery = 28f, SurfaceFor = 5f,
                BreachChance = 0.3f, Wander = 35f, Species = "orca",
            }, finZ: new Vector2(0.48f, 0.75f), anim: a =>
            {
                a.Amplitude = 16f; a.Horizontal = 7f; a.BaseFrequency = 0.45f; a.Exponent = 1.45f; a.WaveNumber = 2.4f; a.FlukeBoost = 1.4f;
            }, spout: new Vector2(3.5f, 0.7f));
            AddSpawn(WhalePrefab, 16f, s_whaleMax.Value, 1, 1, s_whaleChance.Value);
            AddSpawn(OrcaPrefab, 9f, s_orcaMax.Value, 2, 3, s_orcaChance.Value);
        }

        /// <summary>A soft smoke/steam particle material from a vanilla effect (the steam of cooked food).</summary>
        private static Material FindMistMaterial()
        {
            foreach (var name in new[] { "CookedDeerMeat", "CookedMeat", "NeckTailGrilled" })
            {
                var p = PrefabManager.Cache.GetPrefab<GameObject>(name);
                var r = p != null ? p.GetComponentsInChildren<ParticleSystemRenderer>(true).FirstOrDefault(x => x.name.Contains("Steam") && x.sharedMaterial != null) : null;
                if (r != null)
                    return r.sharedMaterial;
            }
            Plugin.Log.LogWarning("Sea: no mist material found, spouts use the default particle material");
            return null;
        }

        private static void Make(string prefabName, string model, string hover, Material template, Material mist, float length,
            SeaSwimmer.Settings settings, Vector2 finZ, System.Action<ProcSwimmer> anim, Vector2 spout)
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

            AddBlowhole(go, d, visual, rig, mist, spout.x, spout.y);
            anim(go.AddComponent<ProcSwimmer>());
            go.AddComponent<SeaSwimmer>().Set(settings);
            var h = go.AddComponent<HoverText>();
            h.m_text = hover;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            Plugin.Log.LogInfo("Registered " + prefabName + " (" + length + " m, " + bones.Count + " bones)");
        }

        /// <summary>
        /// The spout: a burst of mist from the top of the head (highest point of the front of the model), riding
        /// the head bone. Height ~ speed^2 / (2 g gravityModifier).
        /// </summary>
        private static void AddBlowhole(GameObject go, ModelData d, Transform visual, Dictionary<string, Transform> rig,
            Material mist, float speed, float size)
        {
            Vector3 top = d.Pos[0];
            float best = float.MinValue;
            for (int i = 0; i < d.Pos.Length; i++)
            {
                var n = d.Norm(i);
                if (n.z > 0.7f && n.z < 0.88f && Mathf.Abs(n.x - 0.5f) < 0.12f && d.Pos[i].y > best)
                {
                    best = d.Pos[i].y;
                    top = d.Pos[i];
                }
            }
            Vector3 origin = new Vector3(d.Bounds.center.x, d.Bounds.min.y, d.Bounds.center.z);
            var hole = new GameObject("Blowhole").transform;
            hole.SetParent(rig.TryGetValue("Spine1", out var head) ? head : visual, false);
            hole.position = visual.TransformPoint(top - origin);
            hole.rotation = Quaternion.LookRotation(Vector3.up, go.transform.forward);   // cone axis = +z = up
            hole.localScale = Vector3.one;

            var ps = hole.gameObject.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 1.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.75f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(size * 0.5f, size * 1.2f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.08f;   // mist hangs and drifts, it doesn't rain back down
            main.startColor = new Color(0.95f, 0.97f, 1f, 0.6f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = 250;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 70), new ParticleSystem.Burst(0.12f, 50), new ParticleSystem.Burst(0.3f, 30) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 9f;
            shape.radius = size * 0.15f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.9f, 0.95f), 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0.45f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(1f, 2.4f)));
            var vel = ps.limitVelocityOverLifetime;
            vel.enabled = true;
            vel.limit = speed * 0.5f;       // fast burst, then it slows and spreads
            vel.dampen = 0.18f;
            var r = hole.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            if (mist != null)
                r.sharedMaterial = mist;
        }

        private static void AddSpawn(string prefab, float minDepth, int max, int groupMin, int groupMax, float chance)
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
                m_spawnInterval = 900f,      // rare: a check every 15 minutes
                m_spawnChance = chance,
                m_spawnDistance = 80f,
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
    /// Owner-driven swimming: cruise at depth between waypoints in deep enough water along a gently weaving path,
    /// turn away from shallows, come up to breathe now and then and blow (orcas may leap clear of the water),
    /// never leave the sea.
    /// </summary>
    public class SeaSwimmer : MonoBehaviour
    {
        [System.Serializable]
        public class Settings
        {
            public float Speed = 3f, TurnRate = 20f, Depth = 6f, MinDepth = 12f, SurfaceEvery = 40f, SurfaceFor = 6f, BreachChance;
            public float Wander = 25f;      // degrees of weaving either side of the course
            public string Species = "whale";
        }

        public Settings S = new Settings();
        private ZNetView _nview;
        private ParticleSystem _spout;
        private ProcSwimmer _anim;
        private AudioSource _audio;
        private Vector3 _target;
        private float _retarget, _surfaceTimer, _surfacing, _vy, _seed;
        private bool _breaching, _blown;

        internal void Set(Settings s) { S = s; }

        private void OnEnable() { SeaSwimmerRegistry.All.Add(this); }

        private void OnDisable() { SeaSwimmerRegistry.All.Remove(this); }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _spout = GetComponentInChildren<ParticleSystem>(true);
            _anim = GetComponent<ProcSwimmer>();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 8f;
            _audio.maxDistance = 140f;
            _audio.dopplerLevel = 0f;
            _seed = Random.Range(0f, 100f);
            _surfaceTimer = Random.Range(S.SurfaceEvery * 0.3f, S.SurfaceEvery);
        }

        private void Start()
        {
            if (AudioMan.instance != null)
                _audio.outputAudioMixerGroup = AudioMan.instance.m_ambientMixer;
            if (_nview == null || !_nview.IsValid())
                return;
            _nview.Register("WL_Spout", RPC_Spout);
            if (_nview.IsOwner() && ZoneSystem.instance != null)
            {
                var p = transform.position;
                p.y = Water() - S.Depth;
                transform.position = p;
                PickTarget();
            }
        }

        /// <summary>Everyone sees and hears the blow.</summary>
        private void RPC_Spout(long sender)
        {
            if (_spout != null)
                _spout.Play(true);
            _anim?.Spout();
            if (Sea.Blows.TryGetValue(S.Species, out var clips) && clips.Length > 0)
            {
                _audio.pitch = Random.Range(0.93f, 1.07f);
                _audio.PlayOneShot(clips[Random.Range(0, clips.Length)], Sea.BlowVolume);
            }
        }

        private static float Water() => ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;

        private static float Ground(Vector3 p) => ZoneSystem.instance.GetGroundHeight(p);

        private bool Deep(Vector3 p, float depth) => Ground(p) < Water() - depth;

        private void PickTarget()
        {
            for (int i = 0; i < 12; i++)
            {
                Vector2 c = Random.insideUnitCircle * 140f;
                var t = transform.position + new Vector3(c.x, 0f, c.y);
                if (Deep(t, S.MinDepth))
                {
                    _target = t;
                    _retarget = Random.Range(40f, 80f);
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
            if (_retarget <= 0f || Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(_target.x, 0, _target.z)) < 12f)
                PickTarget();
            if (!Deep(p + transform.forward * 25f, S.MinDepth * 0.6f))
                PickTarget();   // shallows ahead
            if (_surfaceTimer <= 0f)
            {
                _surfaceTimer = Random.Range(S.SurfaceEvery * 0.7f, S.SurfaceEvery * 1.3f);
                _surfacing = S.SurfaceFor;
                _blown = false;
            }

            float wantY = water - S.Depth;
            if (_surfacing > 0f)
            {
                _surfacing -= dt;
                wantY = water - 0.6f;
                if (!_blown && p.y > water - 1.3f)
                {
                    _blown = true;
                    _nview.InvokeRPC(ZNetView.Everybody, "WL_Spout");
                }
                if (_blown && S.BreachChance > 0f && Random.value < S.BreachChance * dt / Mathf.Max(S.SurfaceFor, 0.1f) * 2f)
                {
                    _breaching = true;
                    _vy = Random.Range(7f, 9f);
                    _surfacing = 0f;
                    return;
                }
            }
            wantY = Mathf.Max(wantY, Ground(p) + 3f);

            // weave gently around the course (two slow sines), so the path is never a straight line
            float weave = S.Wander * (Mathf.Sin(Time.time * 0.11f + _seed) * 0.7f + Mathf.Sin(Time.time * 0.047f + _seed * 2f) * 0.3f);
            Vector3 flat = Quaternion.Euler(0f, weave, 0f) * new Vector3(_target.x - p.x, 0f, _target.z - p.z).normalized;
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
