using System.Collections.Generic;
using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Ambient swarms: not creatures, a light networked object (spawned by the SpawnSystem, saved, despawned far
    /// away) showing a cloud of small glowing motes drifting and blinking, and a faint flickering light. They only
    /// show in their hours (night for fireflies) and the owner removes them when those are over. Catch them with the
    /// use key: a few of their items go to the player and the swarm is gone. Fireflies live over the Swamp's water;
    /// the Mistlands' spectral butterflies (wave 4) reuse the same Swarm with other looks.
    /// </summary>
    internal static class Fireflies
    {
        public const string SwarmPrefab = "Fireflies";
        public const string ItemPrefab = "Firefly";
        private static ConfigEntry<float> s_spawnChance;
        private static ConfigEntry<int> s_maxSpawned;
        private static Material s_soft;

        public static void BindConfig(ConfigFile config)
        {
            s_spawnChance = config.Bind("Fireflies", "SpawnChance", 35f, "Spawn chance per spawn check (every 60 s, at night), % (restart).");
            s_maxSpawned = config.Bind("Fireflies", "MaxSpawned", 4, "Max swarms around a player (restart).");
        }

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "wl_fireflies", "Fireflies" }, { "wl_catch", "Catch" },
                { "item_firefly", "Firefly" }, { "item_firefly_desc", "A little beetle with a glowing belly. It blinks in the palm of your hand." },
            });
            loc.AddTranslation("French", new Dictionary<string, string>
            {
                { "wl_fireflies", "Lucioles" }, { "wl_catch", "Attraper" },
                { "item_firefly", "Luciole" }, { "item_firefly_desc", "Un petit insecte au ventre lumineux. Il clignote au creux de la main." },
            });
        }

        /// <summary>A soft round particle texture (Sprites/Default alone draws hard squares).</summary>
        internal static Material Soft
        {
            get
            {
                if (s_soft != null)
                    return s_soft;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "wl_softdot", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                    float a = Mathf.Clamp01(1f - r);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));
                }
                tex.SetPixels(px);
                tex.Apply(true);
                s_soft = new Material(Shader.Find("Sprites/Default")) { name = "wl_soft", mainTexture = tex };
                return s_soft;
            }
        }

        public static void Register()
        {
            var item = new CustomItem(ItemPrefab, "Resin", new ItemConfig { Name = "$item_firefly", Description = "$item_firefly_desc", Weight = 0.05f });
            item.ItemPrefab.transform.localScale *= 0.5f;
            Rabbits.Tint(item.ItemPrefab, new Color(0.75f, 1f, 0.35f));
            foreach (var r in item.ItemPrefab.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials)
                    if (m != null && m.HasProperty("_EmissionColor"))
                    {
                        m.EnableKeyword("_EMISSION");
                        m.SetColor("_EmissionColor", new Color(0.6f, 0.9f, 0.2f) * 1.5f);
                    }
            item.ItemDrop.m_itemData.m_shared.m_maxStackSize = 50;
            Rabbits.SetIcon(item);
            ItemManager.Instance.AddItem(item);

            var go = Swarm.MakePrefab(SwarmPrefab, new Swarm.Look
            {
                Name = "$wl_fireflies", Item = ItemPrefab, Night = true, Day = false,
                ColorA = new Color(0.75f, 1f, 0.3f, 1f), ColorB = new Color(1f, 0.95f, 0.45f, 1f), LightColor = new Color(0.7f, 1f, 0.35f),
                Count = 22, Radius = 2.2f, Size = new Vector2(0.05f, 0.1f), Blink = true,
            });
            Spawns.AddDespawn(go);
            SpawnListPatch.Ensure();
            SpawnListPatch.List.m_spawners.Add(new SpawnSystem.SpawnData
            {
                m_name = SwarmPrefab + "_Swamp",
                m_prefab = go,
                m_biome = Heightmap.Biome.Swamp,
                m_maxSpawned = s_maxSpawned.Value,
                m_spawnInterval = 60f,
                m_spawnChance = s_spawnChance.Value,
                m_spawnDistance = 15f,
                m_groupSizeMin = 1,
                m_groupSizeMax = 2,
                m_groupRadius = 8f,
                m_spawnAtDay = false,
                m_spawnAtNight = true,
                m_minAltitude = -1f,      // over the water's edge
                m_maxAltitude = 2f,
                m_inForest = true,
                m_outsideForest = true,
                m_groundOffset = 1.2f,
                m_groundOffsetRandom = 0.6f,
            });
            Plugin.Log.LogInfo("Registered " + SwarmPrefab + " (Swamp, night)");
        }
    }

    /// <summary>A swarm of glowing motes: visuals, hours, and catching (Hoverable, Interactable).</summary>
    public class Swarm : MonoBehaviour, Hoverable, Interactable
    {
        [System.Serializable]
        public class Look
        {
            public string Name, Item;
            public bool Day, Night;
            public Color ColorA, ColorB, LightColor;
            public int Count;
            public float Radius;
            public Vector2 Size;
            public bool Blink;
        }

        public Look L = new Look();
        private ZNetView _nview;
        private ParticleSystem _ps;
        private Light _light;
        private bool _shown = true;
        private float _check;

        /// <summary>The networked prefab: a non-solid collider to hover and catch, the swarm's looks.</summary>
        internal static GameObject MakePrefab(string name, Look look)
        {
            var go = PrefabManager.Instance.CreateEmptyPrefab(name, true);
            foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<MeshRenderer>()) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<MeshFilter>()) Object.DestroyImmediate(c);
            int layer = LayerMask.NameToLayer("piece_nonsolid");
            go.layer = layer >= 0 ? layer : 0;
            var col = go.AddComponent<SphereCollider>();
            col.radius = look.Radius * 0.6f;
            go.AddComponent<Swarm>().L = look;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            return go;
        }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            BuildVisual();
        }

        private void BuildVisual()
        {
            var go = new GameObject("motes");
            go.transform.SetParent(transform, false);
            _ps = go.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(L.Size.x, L.Size.y);
            main.startColor = new ParticleSystem.MinMaxGradient(L.ColorA, L.ColorB);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = L.Count * 2;
            main.prewarm = true;
            var em = _ps.emission;
            em.rateOverTime = L.Count / 5.5f;
            var shape = _ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = L.Radius;
            var noise = _ps.noise;                       // lazy, wandering flight
            noise.enabled = true;
            noise.strength = 0.6f;
            noise.frequency = 0.35f;
            noise.scrollSpeed = 0.2f;
            var col = _ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            if (L.Blink)        // slow blinks: on, off, on...
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.08f), new GradientAlphaKey(0.05f, 0.22f), new GradientAlphaKey(0.9f, 0.4f),
                        new GradientAlphaKey(0.05f, 0.55f), new GradientAlphaKey(1f, 0.75f), new GradientAlphaKey(0f, 1f) });
            else
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(1f, 0.8f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fireflies.Soft;
            _ps.Play();
            var lg = new GameObject("glow");
            lg.transform.SetParent(transform, false);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = L.LightColor;
            _light.range = L.Radius * 2f;
            _light.intensity = 0.5f;
            _light.shadows = LightShadows.None;
        }

        private bool InHours()
        {
            bool night = EnvMan.IsNight();
            return night ? L.Night : L.Day;
        }

        private void Update()
        {
            if (_light != null && _shown)
                _light.intensity = 0.35f + 0.25f * Mathf.PerlinNoise(Time.time * 1.7f, transform.position.x);
            _check -= Time.deltaTime;
            if (_check > 0f)
                return;
            _check = 2f;
            bool show = InHours();
            if (show != _shown)
            {
                _shown = show;
                if (show) _ps.Play(); else _ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                if (_light != null) _light.enabled = show;
            }
            // out of their hours for good: the owner removes the swarm (its glow has faded)
            if (!show && _nview != null && _nview.IsValid() && _nview.IsOwner() && _ps.particleCount == 0)
                _nview.Destroy();
        }

        public string GetHoverText() =>
            _shown ? Localization.instance.Localize(L.Name + "\n[<color=yellow><b>$KEY_Use</b></color>] $wl_catch") : "";

        public string GetHoverName() => L.Name;

        public float GetHoverOffset() => 1.5f;        // catchable from a little further than a door

        /// <summary>Caught: a few of the swarm's items for the player, and the swarm is gone.</summary>
        public bool Interact(Humanoid user, bool hold, bool alt)
        {
            if (hold || !_shown || _nview == null || !_nview.IsValid() || !(user is Player p))
                return false;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(L.Item) : null;
            if (prefab == null)
                return false;
            int n = Random.Range(2, 5);
            if (p.GetInventory().CanAddItem(prefab, n))
                p.GetInventory().AddItem(prefab, n);
            else
                Instantiate(prefab, transform.position, Quaternion.identity).GetComponent<ItemDrop>().SetStack(n);
            p.Message(MessageHud.MessageType.TopLeft, Localization.instance.Localize("$msg_added " + prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name), n);
            _nview.ClaimOwnership();
            _nview.Destroy();
            return true;
        }

        public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;
    }
}
