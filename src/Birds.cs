using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
    /// player comes close" logic), each as its own prefab with its own shape, coat and voice:
    /// - Meadow sparrow: tiny, short beak, brown with a red breast; sings by day, roosts high at night;
    /// - Black Forest crow: croaks by day, roosts high at night, keeps its feathers;
    /// - Owl: stout, short beak, tawny; hides high by day (only flies off if disturbed), hoots and hunts
    ///   rabbits and field mice at night.
    /// All only perch on things (rocks, trunks, roofs, fences), never the ground, and fly at least
    /// MinClearance above the ground except on the last metres to a perch or a prey.
    /// </summary>
    internal static class Birds
    {
        public const string SparrowPrefab = "MeadowSparrow";
        public const string CrowPrefab = "BlackForestCrow";
        public const string OwlPrefab = "MeadowOwl";

        private static ConfigEntry<float> s_sparrowScale;
        private static ConfigEntry<int> s_sparrowMax;
        private static ConfigEntry<int> s_crowMax;
        private static ConfigEntry<float> s_crowVolume;
        private static ConfigEntry<float> s_owlVolume;
        private static ConfigEntry<int> s_owlMax;
        internal static ConfigEntry<float> SongVolume;
        internal static ConfigEntry<float> SongInterval;
        internal static ConfigEntry<float> MinClearance;
        internal static ConfigEntry<float> OwlStrikeDamage;
        private static ConfigEntry<bool> s_owlFlip;

        public static void BindConfig(ConfigFile config)
        {
            s_sparrowScale = config.Bind("Sparrow", "Scale", 0.3f, "Size relative to the vanilla crow (restart).");
            s_sparrowMax = config.Bind("Sparrow", "MaxSpawned", 4, "Max sparrows around a player (restart).");
            SongVolume = config.Bind("Sparrow", "SongVolume", 0.5f, "Song volume, 0-1 (live).");
            SongInterval = config.Bind("Sparrow", "SongInterval", 11f,
                "Average seconds between two songs of a perched sparrow; about 2.5x longer in flight (live).");
            s_crowMax = config.Bind("Crow", "MaxSpawned", 3, "Max Black Forest crows around a player (restart).");
            s_crowVolume = config.Bind("Crow", "Volume", 0.8f, "Croak volume, 0-1 (live).");
            s_owlMax = config.Bind("Owl", "MaxSpawned", 1, "Max owls around a player (restart).");
            s_owlVolume = config.Bind("Owl", "Volume", 0.7f, "Hoot volume, 0-1 (live).");
            s_owlFlip = config.Bind("Owl", "FlipFlyingModel", false,
                "Turn the flying owl model around if it flies backwards (restart).");
            OwlStrikeDamage = config.Bind("Owl", "StrikeDamage", 25f, "Damage of an owl's strike on its prey (live).");
            MinClearance = config.Bind("Birds", "MinClearance", 2f, "Minimum flight height above the ground, m (live).");
        }

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "sparrow", "Sparrow" }, { "blackforest_crow", "Crow" }, { "meadow_owl", "Owl" },
            });
            loc.AddTranslation("French", new Dictionary<string, string>
            {
                { "sparrow", "Moineau" }, { "blackforest_crow", "Corbeau" }, { "meadow_owl", "Chouette" },
            });
        }

        public static void Register()
        {
            var crow = PrefabManager.Cache.GetPrefab<GameObject>("Crow");
            if (crow == null || crow.GetComponent<RandomFlyingBird>() == null)
            {
                Plugin.Log.LogError("Vanilla Crow not found: birds disabled");
                return;
            }
            Voices["sparrow"] = new VoiceSpec(Look.LoadClips("sfx", "sparrow_song"), () => SongInterval.Value, () => SongVolume.Value, 35f, day: true, night: false);
            Voices["crow"] = new VoiceSpec(Look.LoadClips("sfx_crow", "crow_caw"), () => 12f, () => s_crowVolume.Value, 60f, day: true, night: false);
            Voices["owl"] = new VoiceSpec(Look.LoadClips("sfx_owl", "owl_hoot"), () => 18f, () => s_owlVolume.Value, 70f, day: false, night: true);
            RegisterSparrow(crow);
            RegisterCrow(crow);
            RegisterOwl(crow);
        }

        // ------------------------------------------------------------ species

        private static void RegisterSparrow(GameObject crow)
        {
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

            foreach (var c in go.GetComponentsInChildren<DropOnDestroyed>(true)) Object.DestroyImmediate(c);
            foreach (var d in go.GetComponentsInChildren<Destructible>(true))
            {
                d.m_health = 1f;
                d.m_destroyedEffect = OwnFeathers(d.m_destroyedEffect, SparrowPrefab, new Color(0.55f, 0.40f, 0.26f));
            }
            Reshape(go, beak: 0.55f, tail: 0.85f, width: 1.1f, head: 1.05f);
            Look.Paint(go, Look.Mask("sparrow"), new Color(0.55f, 0.40f, 0.26f));
            Finish(go, "$sparrow", "sparrow", restAtNight: true, restByDay: false);
            AddSpawn(go, Heightmap.Biome.Meadows, s_sparrowMax.Value, 3, day: true, night: false);
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
            foreach (var d in go.GetComponentsInChildren<Destructible>(true))
                d.m_destroyedEffect = OwnFeathers(d.m_destroyedEffect, CrowPrefab, Color.black);
            Finish(go, "$blackforest_crow", "crow", restAtNight: true, restByDay: false);
            AddSpawn(go, Heightmap.Biome.BlackForest, s_crowMax.Value, 2, day: true, night: false);
        }

        private static void RegisterOwl(GameObject crow)
        {
            var go = PrefabManager.Instance.CreateClonedPrefab(OwlPrefab, crow);
            go.transform.localScale *= 1.1f;
            var bird = go.GetComponent<RandomFlyingBird>();
            bird.m_flyRange = 30f;
            bird.m_minAlt = 4f;
            bird.m_maxAlt = 10f;
            bird.m_speed = 10f;
            bird.m_turnRate = 25f;
            bird.m_flapDuration = 0.8f;
            bird.m_sailDuration = 2.5f;  // long silent glides
            bird.m_landChance = 0.4f;
            bird.m_landDuration = 12f;
            bird.m_avoidDangerDistance = 8f;
            foreach (var d in go.GetComponentsInChildren<Destructible>(true))
            {
                d.m_health = 15f;
                d.m_destroyedEffect = OwnFeathers(d.m_destroyedEffect, OwlPrefab, new Color(0.50f, 0.36f, 0.22f));
            }
            if (!UseOwlModels(go))
            {
                // fallback: reshaped and painted crow
                Reshape(go, beak: 0.3f, tail: 0.45f, width: 1.3f, head: 1.25f);
                Look.Paint(go, Look.Mask("owl"), new Color(0.50f, 0.36f, 0.22f));
            }
            go.AddComponent<OwlHunter>();
            Finish(go, "$meadow_owl", "owl", restAtNight: false, restByDay: true);
            AddSpawn(go, Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest, s_owlMax.Value, 1, day: true, night: true);
        }

        /// <summary>Generated owl models (fal Trellis): a static perched owl, and a flying owl skinned to the crow's wings.</summary>
        private static bool UseOwlModels(GameObject go)
        {
            var perched = ModelData.Load("owl_perched");
            var flying = ModelData.Load("owl_flying");
            if (perched == null || flying == null)
                return false;
            var mfs = go.GetComponentsInChildren<MeshFilter>(true);
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (mfs.Length == 0 || smrs.Length == 0)
                return false;
            foreach (var mf in mfs)
            {
                if (!Models.OwlSitting(mf, perched))
                    return false;
                var r = mf.GetComponent<Renderer>();
                if (r != null)
                    Models.UseTexture(r, perched.Tex, go.name);
            }
            foreach (var smr in smrs)
            {
                if (!Models.OwlFlying(smr, flying, s_owlFlip.Value))
                    return false;
                Models.UseTexture(smr, flying.Tex, go.name);
            }
            return true;
        }

        private static void Finish(GameObject go, string hover, string voice, bool restAtNight, bool restByDay)
        {
            var bird = go.GetComponent<RandomFlyingBird>();
            bird.m_randomNoise = new EffectList();  // our own voices instead (BirdVoice)
            bird.m_noRandomFlightAtNight = false;    // rest periods are ours (see BirdBehaviour)
            var perch = go.AddComponent<PerchBird>();
            perch.RestAtNight = restAtNight;
            perch.RestByDay = restByDay;
            perch.LandDuration = bird.m_landDuration;
            go.AddComponent<BirdVoice>().Species = voice;
            var h = go.GetComponent<HoverText>();
            if (h != null)
                h.m_text = hover;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            Plugin.Log.LogInfo("Registered " + go.name + ", voice " + voice + " (" + Voices[voice].Clips.Length + " clips)");
        }

        // -------------------------------------------------------------- shape

        /// <summary>
        /// Reshapes the crow meshes (both readable): shorter beak and tail, stouter body and bigger head.
        /// Sitting mesh: y up, beak towards -z. Flying mesh: z up, beak towards +y, x is the wingspan.
        /// </summary>
        private static void Reshape(GameObject go, float beak, float tail, float width, float head)
        {
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null)
                    mf.sharedMesh = ReshapeMesh(mf.sharedMesh, go.name, false, beak, tail, width, head);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (smr.sharedMesh != null)
                    smr.sharedMesh = ReshapeMesh(smr.sharedMesh, go.name, true, beak, tail, width, head);
        }

        private static Mesh ReshapeMesh(Mesh src, string owner, bool flying, float beak, float tail, float width, float head)
        {
            if (!src.isReadable)
            {
                Plugin.Log.LogWarning("Reshape: " + src.name + " not readable, shape kept");
                return src;
            }
            var mesh = Object.Instantiate(src);
            mesh.name = src.name + "_" + owner;
            var v = mesh.vertices;
            Vector3 lo = src.bounds.min, size = src.bounds.size;
            int upAxis = flying ? 2 : 1, fwdAxis = flying ? 1 : 2;
            bool fwdNeg = !flying;
            float beakStart = flying ? 0.9f : 0.84f, tailEnd = flying ? 0.25f : 0.2f;
            for (int i = 0; i < v.Length; i++)
            {
                Vector3 n = new Vector3((v[i].x - lo.x) / size.x, (v[i].y - lo.y) / size.y, (v[i].z - lo.z) / size.z);
                float up = n[upAxis];
                float fwd = fwdNeg ? 1f - n[fwdAxis] : n[fwdAxis];
                float side = n.x - 0.5f;
                bool body = !flying || Mathf.Abs(side) < 0.12f;   // don't widen the spread wings
                if (fwd > beakStart && up > 0.6f)
                    fwd = beakStart + (fwd - beakStart) * beak;
                if (fwd < tailEnd)
                    fwd = tailEnd - (tailEnd - fwd) * tail;
                if (body)
                {
                    float w = width * (up > 0.68f && fwd > 0.5f ? head : 1f);
                    side *= w;
                }
                n.x = side + 0.5f;
                n[fwdAxis] = fwdNeg ? 1f - fwd : fwd;
                v[i] = new Vector3(lo.x + n.x * size.x, lo.y + n.y * size.y, lo.z + n.z * size.z);
            }
            mesh.vertices = v;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Our own copy of the death puff, its particles recoloured.</summary>
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
                    copy.m_inheritParentScale = true;
                }
                result.m_effectPrefabs[i] = copy;
            }
            return result;
        }

        private static void AddSpawn(GameObject prefab, Heightmap.Biome biome, int max, int group, bool day, bool night)
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
                m_spawnAtDay = day,
                m_spawnAtNight = night,
                m_minAltitude = 1f,
                m_inForest = true,
                m_outsideForest = true,
                m_groundOffset = 6f,      // appear in the air, never on the ground
                m_groundOffsetRandom = 3f,
            });
        }

        // -------------------------------------------------------------- voices

        internal class VoiceSpec
        {
            public readonly AudioClip[] Clips;
            public readonly System.Func<float> Interval;
            public readonly System.Func<float> Volume;
            public readonly float MaxDistance;
            public readonly bool Day, Night;

            public VoiceSpec(AudioClip[] clips, System.Func<float> interval, System.Func<float> volume, float maxDistance, bool day, bool night)
            {
                Clips = clips;
                Interval = interval;
                Volume = volume;
                MaxDistance = maxDistance;
                Day = day;
                Night = night;
            }
        }

        internal static readonly Dictionary<string, VoiceSpec> Voices = new Dictionary<string, VoiceSpec>();

        internal static bool IsNight()
        {
            return !EnvMan.IsDaylight();
        }
    }

    /// <summary>Marks our birds and holds their rest period.</summary>
    public class PerchBird : MonoBehaviour
    {
        public bool RestAtNight;
        public bool RestByDay;
        public float LandDuration = 10f;
        [System.NonSerialized] public float NextRestCheck;

        public bool Resting()
        {
            bool night = Birds.IsNight();
            return (RestAtNight && night) || (RestByDay && !night);
        }
    }

    /// <summary>Calls of a bird species (songs, croaks, hoots), mostly when perched, only in its active hours.</summary>
    public class BirdVoice : MonoBehaviour
    {
        public string Species;
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
            _source.dopplerLevel = 0f;
            _timer = Random.Range(1f, 8f);
        }

        private void Start()
        {
            if (AudioMan.instance != null)
                _source.outputAudioMixerGroup = AudioMan.instance.m_ambientMixer; // follows the ambient volume slider
        }

        private void Update()
        {
            long t = Perf.Begin();
            try { UpdateImpl(); }
            finally { Perf.End("BirdVoice.Update", t); }
        }

        private void UpdateImpl()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f || Species == null || !Birds.Voices.TryGetValue(Species, out var spec))
                return;
            bool landed = _nview != null && _nview.IsValid() && _nview.GetZDO().GetBool(ZDOVars.s_landed);
            float mean = spec.Interval() * (landed ? 1f : 2.5f);
            _timer = Random.Range(mean * 0.55f, mean * 1.45f);
            bool night = Birds.IsNight();
            if ((night && !spec.Night) || (!night && !spec.Day))
                return;
            if (spec.Clips.Length == 0 || _source.isPlaying)
                return;
            _source.maxDistance = spec.MaxDistance;
            _source.pitch = Random.Range(0.94f, 1.08f);
            _source.volume = spec.Volume();
            _source.PlayOneShot(spec.Clips[Random.Range(0, spec.Clips.Length)]);
        }
    }

    /// <summary>
    /// Owl hunting at night: picks a rabbit or field mouse nearby, glides down on it and strikes. Prey only
    /// flees from creatures, so a silent owl usually catches it unaware.
    /// </summary>
    public class OwlHunter : MonoBehaviour
    {
        private RandomFlyingBird _bird;
        private ZNetView _nview;
        private Character _prey;
        private float _huntTimer;
        private float _cooldown;
        private float _search;

        public bool Hunting => _prey != null;

        private void Awake()
        {
            _bird = GetComponent<RandomFlyingBird>();
            _nview = GetComponent<ZNetView>();
            _cooldown = Random.Range(5f, 15f);
        }

        private void Update()
        {
            long t = Perf.Begin();
            try { UpdateImpl(); }
            finally { Perf.End("OwlHunter.Update", t); }
        }

        private void UpdateImpl()
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
                return;
            float dt = Time.deltaTime;
            if (_prey == null)
            {
                _cooldown -= dt;
                _search -= dt;
                if (_cooldown > 0f || _search > 0f || !Birds.IsNight())
                    return;
                _search = 3f;
                _prey = FindPrey();
                if (_prey == null)
                    return;
                _huntTimer = 15f;
                _nview.GetZDO().Set(ZDOVars.s_landed, false);
                BirdBehaviour.SetWaypoint(_bird, _prey.transform.position, true);
                return;
            }
            _huntTimer -= dt;
            if (_prey.IsDead() || _huntTimer <= 0f)
            {
                StopHunt();
                return;
            }
            Vector3 target = _prey.GetCenterPoint();
            BirdBehaviour.SetWaypoint(_bird, target, true);
            if (Vector3.Distance(transform.position, target) < 1.2f)
            {
                var hit = new HitData();
                hit.m_damage.m_pierce = Birds.OwlStrikeDamage.Value;
                hit.m_point = target;
                hit.m_dir = transform.forward;
                hit.m_hitType = HitData.HitType.EnemyHit;
                _prey.Damage(hit);
                StopHunt();
            }
        }

        private void StopHunt()
        {
            _prey = null;
            _cooldown = Random.Range(20f, 40f);
            BirdBehaviour.TakeOff(_bird);
        }

        private Character FindPrey()
        {
            Character best = null;
            float bestD = 40f;
            foreach (var c in RabbitTag.All.Concat(MouseTag.All))
            {
                if (c == null || c.IsDead())
                    continue;
                float d = Vector3.Distance(transform.position, c.transform.position);
                if (d < bestD)
                {
                    best = c;
                    bestD = d;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// Replaces RandomFlyingBird.FindLandingPoint for our birds: look down at random spots around home and only
    /// accept the top of something (rock, trunk, roof, fence...), never the terrain or water. While resting
    /// (sparrows and crows at night, the owl by day) pick the highest perch found.
    /// </summary>
    [HarmonyPatch(typeof(RandomFlyingBird), "FindLandingPoint")]
    internal static class BirdPerch
    {
        private static readonly AccessTools.FieldRef<RandomFlyingBird, Vector3> s_spawnPoint =
            AccessTools.FieldRefAccess<RandomFlyingBird, Vector3>("m_spawnPoint");
        private static int s_mask;

        private static bool Prefix(RandomFlyingBird __instance, ref Vector3 waypoint, ref bool __result)
        {
            long t = Perf.Begin();
            try { return PrefixImpl(__instance, ref waypoint, ref __result); }
            finally { Perf.End("BirdPerch", t); }
        }

        private static bool PrefixImpl(RandomFlyingBird __instance, ref Vector3 waypoint, ref bool __result)
        {
            var perch = __instance.GetComponent<PerchBird>();
            if (perch == null)
                return true;
            if (s_mask == 0)
                s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");

            bool resting = perch.Resting();
            float minHeight = resting ? 2.5f : 0.5f;
            __result = false;
            waypoint = new Vector3(0f, -999f, 0f);
            float bestHeight = -1f;
            Vector3 home = s_spawnPoint(__instance);
            for (int i = 0; i < (resting ? 40 : 24); i++)
            {
                Vector2 c = Random.insideUnitCircle * __instance.m_flyRange;
                Vector3 origin = new Vector3(home.x + c.x, Mathf.Max(home.y, __instance.transform.position.y) + 40f, home.z + c.y);
                if (!Physics.Raycast(origin, Vector3.down, out var hit, 120f, s_mask, QueryTriggerInteraction.Ignore))
                    continue;
                if (hit.collider.GetComponentInParent<Heightmap>() != null)
                    continue; // ground
                if (hit.normal.y < 0.7f)
                    continue; // too steep to sit on
                float height = hit.point.y - ZoneSystem.instance.GetGroundHeight(hit.point);
                if (height < minHeight)
                    continue;
                if (hit.point.y < ZoneSystem.instance.m_waterLevel + 0.3f)
                    continue;
                if (Player.IsPlayerInRange(hit.point, __instance.m_avoidDangerDistance))
                    continue;
                if (height > bestHeight)
                {
                    bestHeight = height;
                    waypoint = hit.point;
                    __result = true;
                }
                if (!resting)
                    break; // active: first good perch
            }
            return false;
        }
    }

    /// <summary>
    /// Per fixed update for our birds: rest periods (stay perched unless disturbed; if flying, look for a high
    /// perch), and a minimum clearance above the ground except on the final approach to a perch or a prey.
    /// </summary>
    [HarmonyPatch(typeof(RandomFlyingBird), nameof(RandomFlyingBird.CustomFixedUpdate))]
    internal static class BirdBehaviour
    {
        private static readonly AccessTools.FieldRef<RandomFlyingBird, bool> s_groundwp =
            AccessTools.FieldRefAccess<RandomFlyingBird, bool>("m_groundwp");
        private static readonly AccessTools.FieldRef<RandomFlyingBird, Vector3> s_waypoint =
            AccessTools.FieldRefAccess<RandomFlyingBird, Vector3>("m_waypoint");
        private static readonly AccessTools.FieldRef<RandomFlyingBird, ZNetView> s_nview =
            AccessTools.FieldRefAccess<RandomFlyingBird, ZNetView>("m_nview");
        private static readonly MethodInfo s_randomize = AccessTools.Method(typeof(RandomFlyingBird), "RandomizeWaypoint");

        internal static void SetWaypoint(RandomFlyingBird bird, Vector3 point, bool ground)
        {
            s_waypoint(bird) = point;
            s_groundwp(bird) = ground;
        }

        internal static void TakeOff(RandomFlyingBird bird)
        {
            s_randomize.Invoke(bird, new object[] { false });
        }

        private static void Postfix(RandomFlyingBird __instance, float dt)
        {
            long t = Perf.Begin();
            try { PostfixImpl(__instance, dt); }
            finally { Perf.End("BirdBehaviour", t); }
        }

        private static void PostfixImpl(RandomFlyingBird bird, float dt)
        {
            var perch = bird.GetComponent<PerchBird>();
            if (perch == null)
                return;
            var nview = s_nview(bird);
            if (nview == null || !nview.IsValid() || !nview.IsOwner())
                return;

            var hunter = bird.GetComponent<OwlHunter>();
            bool hunting = hunter != null && hunter.Hunting;
            bool resting = perch.Resting() && !hunting;
            bird.m_landDuration = resting ? 1e6f : perch.LandDuration; // resting: only danger makes it fly
            bool landed = nview.GetZDO().GetBool(ZDOVars.s_landed);
            if (landed)
                return;

            if (resting && !s_groundwp(bird) && Time.time >= perch.NextRestCheck)
            {
                perch.NextRestCheck = Time.time + 2f;
                s_randomize.Invoke(bird, new object[] { true }); // look for a (high) perch
            }

            Vector3 p = bird.transform.position;
            if (s_groundwp(bird))
            {
                Vector3 wp = s_waypoint(bird);
                if (new Vector2(wp.x - p.x, wp.z - p.z).magnitude < 4f)
                    return; // landing on a perch, or striking a prey
            }
            float floor = Mathf.Max(ZoneSystem.instance.GetGroundHeight(p), ZoneSystem.instance.m_waterLevel);
            float min = floor + Birds.MinClearance.Value;
            if (p.y >= min)
                return;
            p.y = p.y < floor + 0.3f ? floor + 0.3f : p.y;
            p.y = Mathf.Min(min, p.y + 12f * dt); // climb back smoothly
            bird.transform.position = p;
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
