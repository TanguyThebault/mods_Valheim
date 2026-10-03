using System.Collections.Generic;
using System.IO;
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
    /// Meadow sparrows: tiny brown birds built on the vanilla RandomFlyingBird (the crow's flight, landing and
    /// "fly off when a player comes close" logic). They only perch on things (rocks, trunks, buildings,
    /// fences), never on the ground, sing short songs (WAVs next to the DLL), and drop nothing.
    /// </summary>
    internal static class Sparrows
    {
        public const string Prefab = "MeadowSparrow";
        private static readonly string[] BaseCandidates = { "Crow", "Seagal" };

        internal static readonly List<AudioClip> Songs = new List<AudioClip>();
        private static ConfigEntry<float> s_scale;
        private static ConfigEntry<int> s_maxSpawned;
        internal static ConfigEntry<float> SongVolume;
        internal static ConfigEntry<float> SongInterval;

        public static void BindConfig(ConfigFile config)
        {
            s_scale = config.Bind("Sparrow", "Scale", 0.3f, "Size relative to the vanilla crow (restart).");
            s_maxSpawned = config.Bind("Sparrow", "MaxSpawned", 4, "Max sparrows around a player (restart).");
            SongVolume = config.Bind("Sparrow", "SongVolume", 0.5f, "Song volume, 0-1 (live).");
            SongInterval = config.Bind("Sparrow", "SongInterval", 11f,
                "Average seconds between two songs of a perched sparrow; about 2.5x longer in flight (live).");
        }

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", "sparrow", "Sparrow");
            loc.AddTranslation("French", "sparrow", "Moineau");
        }

        public static void Register(string pluginDir)
        {
            var baseBird = FindBaseBird();
            if (baseBird == null)
            {
                Plugin.Log.LogError("No vanilla RandomFlyingBird prefab found: sparrows disabled");
                return;
            }
            Dump(baseBird);
            LoadSongs(Path.Combine(pluginDir, "sfx"));

            var go = PrefabManager.Instance.CreateClonedPrefab(Prefab, baseBird);
            go.transform.localScale *= s_scale.Value;

            var bird = go.GetComponent<RandomFlyingBird>();
            bird.m_flyRange = 18f;
            bird.m_minAlt = 2f;          // small birds stay low
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

            StripDrops(go);
            foreach (var d in go.GetComponentsInChildren<Destructible>(true))
            {
                d.m_health = 1f;
                if (d.m_spawnWhenDestroyed != null && HasDrops(d.m_spawnWhenDestroyed))
                {
                    var death = PrefabManager.Instance.CreateClonedPrefab(Prefab + "_death", d.m_spawnWhenDestroyed);
                    StripDrops(death);
                    PrefabManager.Instance.AddPrefab(new CustomPrefab(death, true));
                    d.m_spawnWhenDestroyed = death;
                }
            }

            Recolor(go);
            go.AddComponent<SparrowSong>();
            var hover = go.GetComponent<HoverText>();
            if (hover != null)
                hover.m_text = "$sparrow";

            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            AddSpawn(go);
            Plugin.Log.LogInfo("Registered " + Prefab + " (clone of " + baseBird.name + "), " + Songs.Count + " songs");
        }

        private static GameObject FindBaseBird()
        {
            foreach (var name in BaseCandidates)
            {
                var p = PrefabManager.Cache.GetPrefab<GameObject>(name);
                if (p != null && p.GetComponent<RandomFlyingBird>() != null)
                    return p;
            }
            var all = PrefabManager.Cache.GetPrefabs(typeof(RandomFlyingBird));
            foreach (var kv in all)
                Plugin.Log.LogInfo("RandomFlyingBird prefab available: " + kv.Key);
            return all.Values.OfType<Component>().Select(c => c.gameObject).FirstOrDefault();
        }

        private static void AddSpawn(GameObject prefab)
        {
            if (SpawnListPatch.List == null)
            {
                var holder = new GameObject("ThrowingAxe_spawnlist");
                Object.DontDestroyOnLoad(holder);
                SpawnListPatch.List = holder.AddComponent<SpawnSystemList>();
            }
            SpawnListPatch.List.m_spawners.Add(new SpawnSystem.SpawnData
            {
                m_name = Prefab + "_Meadows",
                m_prefab = prefab,
                m_biome = Heightmap.Biome.Meadows,
                m_maxSpawned = s_maxSpawned.Value,
                m_spawnInterval = 60f,
                m_spawnChance = 50f,
                m_spawnDistance = 20f,
                m_groupSizeMin = 1,
                m_groupSizeMax = 3,
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

        // ---------------------------------------------------------- drops, look

        private static bool HasDrops(GameObject go)
        {
            return go.GetComponentsInChildren<DropOnDestroyed>(true).Length > 0 ||
                   go.GetComponentsInChildren<CharacterDrop>(true).Length > 0 ||
                   go.GetComponentsInChildren<ItemDrop>(true).Length > 0;
        }

        private static void StripDrops(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<DropOnDestroyed>(true)) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponentsInChildren<CharacterDrop>(true)) Object.DestroyImmediate(c);
        }

        /// <summary>
        /// The crow is black, so a colour multiply can't make it brown. Instead each texture is copied through
        /// the GPU (works on non-readable textures) and its brightness remapped onto a sparrow-brown ramp, which
        /// keeps the feather detail.
        /// </summary>
        private static void Recolor(GameObject go)
        {
            var done = new Dictionary<Texture, Texture2D>();
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                    continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                        continue;
                    var m = new Material(mats[i]) { name = mats[i].name + "_sparrow" };
                    var src = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    if (src != null)
                    {
                        if (!done.TryGetValue(src, out var tex))
                            done[src] = tex = BrownRamp(src);
                        m.SetTexture("_MainTex", tex);
                    }
                    if (m.HasProperty("_Color"))
                        m.SetColor("_Color", Color.white);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }

        private static Texture2D BrownRamp(Texture src)
        {
            int w = Mathf.Min(src.width, 1024), h = Mathf.Min(src.height, 1024);
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            var px = tex.GetPixels();
            var lum = px.Select(c => 0.3f * c.r + 0.59f * c.g + 0.11f * c.b).OrderBy(v => v).ToArray();
            float lo = lum[(int)(lum.Length * 0.02f)], hi = Mathf.Max(lum[(int)(lum.Length * 0.98f)], lo + 0.01f);
            var dark = new Color(0.20f, 0.12f, 0.07f);
            var mid = new Color(0.50f, 0.34f, 0.20f);
            var light = new Color(0.82f, 0.72f, 0.58f);
            for (int i = 0; i < px.Length; i++)
            {
                float l = Mathf.Clamp01((0.3f * px[i].r + 0.59f * px[i].g + 0.11f * px[i].b - lo) / (hi - lo));
                var c = l < 0.5f ? Color.Lerp(dark, mid, l * 2f) : Color.Lerp(mid, light, (l - 0.5f) * 2f);
                c.a = px[i].a;
                px[i] = c;
            }
            tex.SetPixels(px);
            tex.Apply(true);
            tex.name = src.name + "_sparrow";
            return tex;
        }

        // --------------------------------------------------------------- songs

        private static void LoadSongs(string dir)
        {
            Songs.Clear();
            if (!Directory.Exists(dir))
            {
                Plugin.Log.LogWarning("No sfx folder at " + dir + ": sparrows will be silent");
                return;
            }
            foreach (var path in Directory.GetFiles(dir, "*.wav").OrderBy(p => p))
            {
                var clip = LoadWav(path);
                if (clip != null)
                    Songs.Add(clip);
            }
        }

        /// <summary>Minimal RIFF reader: 16-bit PCM, mono or stereo (stereo is downmixed).</summary>
        private static AudioClip LoadWav(string path)
        {
            try
            {
                var b = File.ReadAllBytes(path);
                int channels = 0, rate = 0, bits = 0, pos = 12;
                while (pos + 8 <= b.Length)
                {
                    string id = Encoding.ASCII.GetString(b, pos, 4);
                    int size = System.BitConverter.ToInt32(b, pos + 4);
                    int body = pos + 8;
                    if (id == "fmt ")
                    {
                        channels = System.BitConverter.ToInt16(b, body + 2);
                        rate = System.BitConverter.ToInt32(b, body + 4);
                        bits = System.BitConverter.ToInt16(b, body + 14);
                    }
                    else if (id == "data" && bits == 16 && channels > 0)
                    {
                        int frames = size / (2 * channels);
                        var data = new float[frames];
                        for (int f = 0; f < frames; f++)
                        {
                            float sum = 0f;
                            for (int c = 0; c < channels; c++)
                                sum += System.BitConverter.ToInt16(b, body + (f * channels + c) * 2) / 32768f;
                            data[f] = sum / channels;
                        }
                        var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), frames, 1, rate, false);
                        clip.SetData(data, 0);
                        return clip;
                    }
                    pos = body + size + (size & 1);
                }
                Plugin.Log.LogWarning("Unsupported WAV (need 16-bit PCM): " + path);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Failed to read " + path + ": " + e.Message);
            }
            return null;
        }

        private static void Dump(GameObject go)
        {
            var sb = new StringBuilder("Dump " + go.name + ": scale " + go.transform.localScale + "\n  components: ");
            sb.Append(string.Join(", ", go.GetComponents<Component>().Select(c => c.GetType().Name)));
            var bird = go.GetComponent<RandomFlyingBird>();
            sb.Append("\n  bird: range " + bird.m_flyRange + ", alt " + bird.m_minAlt + "-" + bird.m_maxAlt + ", speed " + bird.m_speed +
                      ", turn " + bird.m_turnRate + ", land " + bird.m_landChance + "/" + bird.m_landDuration + "s, singleModel " +
                      bird.m_singleModel);
            foreach (var d in go.GetComponentsInChildren<Destructible>(true))
                sb.Append("\n  destructible: health " + d.m_health + ", spawnWhenDestroyed " +
                          (d.m_spawnWhenDestroyed ? d.m_spawnWhenDestroyed.name + " (drops: " + HasDrops(d.m_spawnWhenDestroyed) + ")" : "none"));
            foreach (var d in go.GetComponentsInChildren<DropOnDestroyed>(true))
                sb.Append("\n  DropOnDestroyed on " + d.name);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials.Where(m => m != null))
                    sb.Append("\n  renderer " + r.name + ": " + m.name + " / " + m.shader.name +
                              (m.HasProperty("_MainTex") && m.GetTexture("_MainTex") ? " tex " + m.GetTexture("_MainTex").name : ""));
            Plugin.Log.LogInfo(sb.ToString());
        }
    }

    /// <summary>Sings the sparrow songs; also marks sparrows for the landing patch.</summary>
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
            _source.volume = Sparrows.SongVolume.Value;
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
            // Sings mostly when perched, now and then in flight, rarely at night.
            float mean = Sparrows.SongInterval.Value * (landed ? 1f : 2.5f);
            _timer = Random.Range(mean * 0.55f, mean * 1.45f);
            if (!EnvMan.IsDaylight() && Random.value > 0.15f)
                return;
            if (Sparrows.Songs.Count == 0 || _source.isPlaying)
                return;
            _source.pitch = Random.Range(0.92f, 1.1f);
            _source.volume = Sparrows.SongVolume.Value;
            _source.PlayOneShot(Sparrows.Songs[Random.Range(0, Sparrows.Songs.Count)]);
        }
    }

    /// <summary>
    /// Replaces RandomFlyingBird.FindLandingPoint for sparrows: look down at random spots around home and only
    /// accept the top of something (rock, trunk, roof, fence...), never the terrain or water. No perch found:
    /// the vanilla code just keeps flying.
    /// </summary>
    [HarmonyPatch(typeof(RandomFlyingBird), "FindLandingPoint")]
    internal static class SparrowPerch
    {
        private static readonly AccessTools.FieldRef<RandomFlyingBird, Vector3> s_spawnPoint =
            AccessTools.FieldRefAccess<RandomFlyingBird, Vector3>("m_spawnPoint");
        private static int s_mask;

        private static bool Prefix(RandomFlyingBird __instance, ref Vector3 waypoint, ref bool __result)
        {
            if (__instance.GetComponent<SparrowSong>() == null)
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

    /// <summary>Our own spawn list, added to every SpawnSystem (one per zone) as it wakes up.</summary>
    [HarmonyPatch(typeof(SpawnSystem), "Awake")]
    internal static class SpawnListPatch
    {
        internal static SpawnSystemList List;

        private static void Postfix(SpawnSystem __instance)
        {
            if (List != null && !__instance.m_spawnLists.Contains(List))
                __instance.m_spawnLists.Add(List);
        }
    }
}
