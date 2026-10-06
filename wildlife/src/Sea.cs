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
    /// and the sound of the breath, seen and heard by everyone (RPC). They sing (whale) and call (orca) now and
    /// then. They can be hunted like the Serpent: a lot of health, a dive and a sprint away when hit, and their
    /// meat, blubber, baleen or teeth float up when they die (see Crafts).
    /// </summary>
    internal static class Sea
    {
        public const string WhalePrefab = "Whale";
        public const string OrcaPrefab = "Orca";

        private static ConfigEntry<int> s_whaleMax, s_orcaMax;
        private static ConfigEntry<float> s_whaleLength, s_orcaLength, s_whaleChance, s_orcaChance, s_blowVolume;
        private static ConfigEntry<float> s_lobtailChance, s_glideChance, s_breachChance, s_callVolume, s_whaleHealth, s_orcaHealth;
        internal static readonly Dictionary<string, AudioClip[]> Blows = new Dictionary<string, AudioClip[]>();
        internal static float BlowVolume => s_blowVolume.Value;
        internal static float CallVolume => s_callVolume.Value;
        internal static float SlapVolume => s_slapVolume.Value;
        private static ConfigEntry<float> s_cryVolume, s_slapVolume;

        /// <summary>What floats up when a whale or an orca dies: prefab, min, max.</summary>
        internal static readonly Dictionary<string, (string prefab, int min, int max)[]> Loot = new Dictionary<string, (string, int, int)[]>
        {
            { "whale", new[] { (Crafts.WhaleMeat, 8, 12), (Crafts.WhaleBlubber, 6, 10), (Crafts.Baleen, 3, 5) } },
            { "orca", new[] { (Crafts.OrcaMeat, 4, 7), (Crafts.OrcaTooth, 2, 4), (Crafts.WhaleBlubber, 1, 3) } },
        };

        public static void BindConfig(ConfigFile config)
        {
            s_whaleMax = config.Bind("Whale", "MaxSpawned", 1, "Max whales around a player (restart).");
            s_whaleChance = config.Bind("Whale", "SpawnChance", 5f, "Chance per spawn check (every 15 min), % (restart).");
            s_whaleLength = config.Bind("Whale", "Length", 14f, "Whale length, m (restart).");
            s_orcaMax = config.Bind("Orca", "MaxSpawned", 3, "Max orcas around a player (restart).");
            s_orcaChance = config.Bind("Orca", "SpawnChance", 6f, "Chance per spawn check (every 15 min), % (restart).");
            s_orcaLength = config.Bind("Orca", "Length", 7f, "Orca length, m (restart).");
            s_blowVolume = config.Bind("Sea", "BlowVolume", 0.9f, "Volume of the blowhole spout, 0-1 (live).");
            s_lobtailChance = config.Bind("Whale", "LobtailChance", 0.5f,
                "Chance (0-1) that a whale slaps its tail on the water at the end of a surfacing (restart).");
            s_glideChance = config.Bind("Orca", "GlideChance", 0.45f,
                "Chance (0-1) that an orca glides with its dorsal fin out at the end of a surfacing (restart).");
            s_breachChance = config.Bind("Orca", "BreachChance", 0.25f,
                "Chance (0-1) that an orca leaps out of the water at the end of a surfacing (restart).");
            s_callVolume = config.Bind("Sea", "CallVolume", 0.2f, "Volume of whale songs and orca calls, 0-1 (live).");
            s_cryVolume = config.Bind("Sea", "CryVolume", 0.25f, "Volume of the cries when hit or killed, 0-1 (restart).");
            s_slapVolume = config.Bind("Sea", "SlapVolume", 0.27f, "Volume of the tail slaps, 0-1 (live).");
            s_whaleHealth = config.Bind("Whale", "Health", 1500f, "Hit points (restart).");
            s_orcaHealth = config.Bind("Orca", "Health", 600f, "Hit points (restart).");
        }

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", new Dictionary<string, string> { { "whale", "Humpback whale" }, { "orca", "Orca" } });
            loc.AddTranslation("French", new Dictionary<string, string> { { "whale", "Baleine à bosse" }, { "orca", "Orque" } });
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
            Blows["whale_slap"] = Look.LoadClips("sfx_sea", "whale_slap");
            Blows["whale_call"] = Look.LoadClips("sfx_sea", "whale_song");
            Blows["orca_call"] = Look.LoadClips("sfx_sea", "orca_call");
            var mist = FindMistMaterial();

            Make(WhalePrefab, "whale", "$whale", template, mist, s_whaleLength.Value, new SeaSwimmer.Settings
            {
                Speed = 2.4f, TurnRate = 12f, Depth = 7f, MinDepth = 14f, SurfaceEvery = 50f, BlowsMin = 2, BlowsMax = 4, BlowGap = 6f,
                BreachChance = 0f, Wander = 25f, Species = "whale", LobtailChance = s_lobtailChance.Value, CallEvery = 35f,
            }, s_whaleHealth.Value, finZ: new Vector2(0.45f, 0.8f), anim: a =>
            {
                a.TipAmplitude = 0.10f; a.FlukePitch = 22f; a.WaveNumber = 6f; a.Strouhal = 0.37f; a.MinFrequency = 0.14f; a.LengthMeters = s_whaleLength.Value; a.Horizontal = 3f; a.RigidFront = 0.45f; a.HeadHeave = 0.015f;
            }, spout: new Vector2(6.5f, 1.4f));
            Make(OrcaPrefab, "orca", "$orca", template, mist, s_orcaLength.Value, new SeaSwimmer.Settings
            {
                Speed = 5.5f, TurnRate = 32f, Depth = 4f, MinDepth = 8f, SurfaceEvery = 28f, BlowsMin = 1, BlowsMax = 2, BlowGap = 3.5f,
                BreachChance = s_breachChance.Value, Wander = 35f, Species = "orca", GlideChance = s_glideChance.Value, CallEvery = 14f,
            }, s_orcaHealth.Value, finZ: new Vector2(0.48f, 0.75f), anim: a =>
            {
                a.TipAmplitude = 0.15f; a.FlukePitch = 22f; a.WaveNumber = 6.5f; a.Strouhal = 0.37f; a.MinFrequency = 0.35f; a.LengthMeters = s_orcaLength.Value; a.Horizontal = 3f; a.RigidFront = 0.5f; a.HeadHeave = 0.015f;
            }, spout: new Vector2(3.5f, 0.7f));
            AddSpawn(WhalePrefab, 16f, s_whaleMax.Value, 1, 1, s_whaleChance.Value);
            AddSpawn(OrcaPrefab, 9f, s_orcaMax.Value, 2, 3, s_orcaChance.Value);
        }

        private static int s_sheetX = 1, s_sheetY = 1;

        /// <summary>One random puff of the mist atlas per particle, playing through the sheet over its life.</summary>
        internal static void UseMistSheet(ParticleSystem ps)
        {
            if (s_sheetX * s_sheetY <= 1)
                return;
            var tsa = ps.textureSheetAnimation;
            tsa.enabled = true;
            tsa.mode = ParticleSystemAnimationMode.Grid;
            tsa.numTilesX = s_sheetX;
            tsa.numTilesY = s_sheetY;
            tsa.animation = ParticleSystemAnimationType.WholeSheet;
            tsa.frameOverTime = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0f, 1f, 1f));
            tsa.cycleCount = 1;
        }

        /// <summary>A soft smoke/steam particle material from a vanilla effect (the steam of cooked food).</summary>
        private static Material FindMistMaterial()
        {
            foreach (var name in new[] { "CookedDeerMeat", "CookedMeat", "NeckTailGrilled" })
            {
                var p = PrefabManager.Cache.GetPrefab<GameObject>(name);
                var r = p != null ? p.GetComponentsInChildren<ParticleSystemRenderer>(true).FirstOrDefault(x => x.name.Contains("Steam") && x.sharedMaterial != null) : null;
                if (r != null)
                {
                    // the texture is a flipbook atlas: without the source's sheet settings each particle shows the
                    // whole grid of puffs (seen in game as a cloud of little squares)
                    var src = r.GetComponent<ParticleSystem>();
                    if (src != null && src.textureSheetAnimation.enabled)
                    {
                        s_sheetX = src.textureSheetAnimation.numTilesX;
                        s_sheetY = src.textureSheetAnimation.numTilesY;
                    }
                    Plugin.Log.LogInfo("Sea mist: " + r.sharedMaterial.name + " from " + name + ", sheet " + s_sheetX + "x" + s_sheetY);
                    return r.sharedMaterial;
                }
            }
            Plugin.Log.LogWarning("Sea: no mist material found, spouts use the default particle material");
            return null;
        }

        /// <summary>
        /// A reusable splash (world space, not parented, so it stays where the flukes hit): a burst of spray thrown up
        /// and out, and a flat ring of foam spreading on the water. Same soft mist material as the spout.
        /// </summary>
        internal static ParticleSystem MakeSplash(Material mist)
        {
            var go = new GameObject("WL_Splash");
            go.transform.rotation = Quaternion.LookRotation(Vector3.up);   // cones open along +z: point them up
            var spray = Burst(go.transform, mist, speed: new Vector2(5f, 11f), size: new Vector2(0.5f, 1.4f), angle: 30f,
                count: 120, life: new Vector2(0.8f, 1.6f), gravity: 1.1f);
            var foam = Burst(spray.transform, mist, speed: new Vector2(3f, 6f), size: new Vector2(1.2f, 2.6f), angle: 88f,
                count: 60, life: new Vector2(1.2f, 2.2f), gravity: 0.05f);
            var drag = foam.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.limit = 1.5f;
            drag.dampen = 0.25f;
            return spray;
        }

        private static ParticleSystem Burst(Transform parent, Material mist, Vector2 speed, Vector2 size, float angle, int count,
            Vector2 life, float gravity)
        {
            var go = new GameObject("burst");
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life.x, life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed.x, speed.y);
            main.startSize = new ParticleSystem.MinMaxCurve(size.x, size.y);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = gravity;
            main.startColor = new Color(0.95f, 0.97f, 1f, 0.85f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = count * 2;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(count * 0.7f)), new ParticleSystem.Burst(0.08f, (short)(count * 0.3f)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = angle;
            shape.radius = 1.2f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.85f, 0.9f, 0.95f), 1f) },
                new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.5f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var sz = ps.sizeOverLifetime;
            sz.enabled = true;
            sz.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 1.8f)));
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Billboard;
            if (mist != null) r.sharedMaterial = mist;
            UseMistSheet(ps);
            return ps;
        }

        private static void Make(string prefabName, string model, string hover, Material template, Material mist, float length,
            SeaSwimmer.Settings settings, float health, Vector2 finZ, System.Action<ProcSwimmer> anim, Vector2 spout)
        {
            var d = ModelData.Load(model);
            if (d == null)
                return;
            var go = PrefabManager.Instance.CreateEmptyPrefab(prefabName, true);
            foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<MeshRenderer>()) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<MeshFilter>()) Object.DestroyImmediate(c);
            go.AddComponent<ZSyncTransform>();

            // a Blender rig (models/<model>.rig: bones, skin, swim cycle) wins over the procedural one
            var rigFile = RigFile.Get(model);
            List<ProcRig.Bone> bones;
            System.Func<int, int[]> allowed;
            System.Func<int, BoneWeight> weights;
            if (rigFile != null && rigFile.Weights.Length == d.Pos.Length)
            {
                bones = rigFile.Bones;
                allowed = v => null;
                weights = v => rigFile.Weights[v];
                Plugin.Log.LogInfo(prefabName + ": Blender rig, " + bones.Count + " bones, cycle of " + rigFile.Phases + " samples");
            }
            else
            {
                rigFile = null;
                bones = ProcRig.SwimmerBones(d, finZ.x, finZ.y, out allowed, out weights);
            }
            var rig = ProcRig.Build(go, d, bones, allowed, template, length, model + "_rig", weights, glossiness: 0.3f);   // wet skin, but not a mirror for the sky
            // centre the body on the object so it swims around its own position, not its belly
            var visual = go.transform.Find("Visual_rig");
            visual.localPosition = new Vector3(0f, -length * d.Bounds.size.y / d.Bounds.size.z * 0.5f, 0f);

            AddBlowhole(go, d, visual, rig, mist, spout.x, spout.y);
            var swimmer = go.AddComponent<ProcSwimmer>();
            swimmer.RigModel = rigFile != null ? model : null;
            anim(swimmer);
            swimmer.Init(rig);
            AddHunting(go, model, length, health, d);
            go.AddComponent<SeaSwimmer>().Set(settings);
            var h = go.AddComponent<HoverText>();
            h.m_text = hover;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            Plugin.Log.LogInfo("Registered " + prefabName + " (" + length + " m, " + bones.Count + " bones)");
        }

        /// <summary>
        /// Huntable like the Serpent: a capsule hitbox along the body (on the "hitbox" layer, kinematic, so it takes
        /// arrows and spears without shoving boats), a Destructible with the Serpent's blood and our own cries.
        /// SeaSwimmer reacts to the hits (flees) and to the death (loot floats up).
        /// </summary>
        private static void AddHunting(GameObject go, string model, float length, float health, ModelData d)
        {
            var box = new GameObject("Hitbox");
            box.transform.SetParent(go.transform, false);
            int layer = LayerMask.NameToLayer("hitbox");
            box.layer = layer >= 0 ? layer : 0;
            var col = box.AddComponent<CapsuleCollider>();
            col.direction = 2;
            col.radius = length * d.Bounds.size.y / d.Bounds.size.z * 0.5f;
            col.height = length * 0.95f;
            var body = box.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;

            var serpent = PrefabManager.Cache.GetPrefab<GameObject>("Serpent")?.GetComponent<Character>();
            var hurt = Look.LoadClips("sfx_sea", model + "_hurt");
            var template = serpent != null ? Look.FindSfxTemplate(serpent.m_hitEffects, serpent.m_deathEffects) : null;
            var hurtSfx = Look.MakeSfx("sfx_" + model + "_hurt", template, hurt, 0.95f, 1.05f);
            var deathSfx = Look.MakeSfx("sfx_" + model + "_death", template, hurt, 0.7f, 0.78f);
            foreach (var sfx in new[] { hurtSfx, deathSfx })
            {
                if (sfx == null) continue;
                var z = sfx.GetComponent<ZSFX>();
                z.m_minVol *= s_cryVolume.Value;
                z.m_maxVol *= s_cryVolume.Value;
            }

            var dst = go.AddComponent<Destructible>();
            dst.m_destructibleType = DestructibleType.Character;
            dst.m_health = health;
            if (serpent != null)
            {
                dst.m_hitEffect = Look.Voice(serpent.m_hitEffects, hurtSfx);
                // no ragdoll: the body is ours, it just goes (ZNetScene.Destroy) in a cloud of blood
                var death = new List<EffectList.EffectData>();
                foreach (var ed in Look.Voice(serpent.m_deathEffects, deathSfx).m_effectPrefabs)
                    if (ed?.m_prefab != null && ed.m_prefab.GetComponentInChildren<Ragdoll>(true) == null)
                        death.Add(ed);
                dst.m_destroyedEffect = new EffectList { m_effectPrefabs = death.ToArray() };
            }
            if (layer >= 0)
                Plugin.Log.LogInfo(go.name + ": " + health + " hp, hitbox layer " + layer + " ignores character " +
                                   Physics.GetIgnoreLayerCollision(layer, LayerMask.NameToLayer("character")) + ", vehicle " +
                                   Physics.GetIgnoreLayerCollision(layer, LayerMask.NameToLayer("vehicle")));
        }

        /// <summary>The loot of a dead whale or orca, on the surface above it (items float).</summary>
        internal static void DropLoot(string species, Vector3 at)
        {
            if (!Loot.TryGetValue(species, out var loot) || ZNetScene.instance == null)
                return;
            at.y = SeaSwimmer.Surface(at) + 0.3f;
            foreach (var (name, min, max) in loot)
            {
                var prefab = ZNetScene.instance.GetPrefab(name);
                if (prefab == null)
                {
                    Plugin.Log.LogWarning("Loot prefab missing: " + name);
                    continue;
                }
                for (int n = Random.Range(min, max + 1); n > 0;)
                {
                    Vector2 c = Random.insideUnitCircle * 2.5f;
                    var go = Object.Instantiate(prefab, at + new Vector3(c.x, 0f, c.y), Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                    var item = go.GetComponent<ItemDrop>();
                    int stack = Mathf.Min(n, item.m_itemData.m_shared.m_maxStackSize);
                    item.SetStack(stack);
                    ItemDrop.OnCreateNew(item);
                    n -= stack;
                }
            }
            Plugin.Log.LogInfo(species + " killed: loot at " + at.ToString("F0"));
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
            UseMistSheet(ps);
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
    /// turn away from shallows, never leave the sea. Every SurfaceEvery seconds a surfacing runs as explicit steps:
    /// ascend until the blowhole breaks the surface, blow BlowsMin-BlowsMax times a few seconds apart while
    /// cruising slowly at the surface, then one finale: an orca leaps, or glides with its dorsal fin out, a whale
    /// slaps its tail on the water (lobtail), or it simply dives. Two plain dives in a row force a show, so a player
    /// watching sees one. At the surface everything follows the real wave height under the animal (the game's water
    /// volume, not the flat sea level), so the blowhole breaks the surface on a crest as in a trough. The glide and
    /// lobtail are clips made in Blender that also give the depth to hold (ProcSwimmer). Hit, the animal sprints away
    /// deep for a while (a whole orca pod with it); killed, its loot floats up. Whales sing, orcas call (local).
    /// </summary>
    public class SeaSwimmer : MonoBehaviour
    {
        [System.Serializable]
        public class Settings
        {
            public float Speed = 3f, TurnRate = 20f, Depth = 6f, MinDepth = 12f, SurfaceEvery = 40f, BreachChance;
            public int BlowsMin = 1, BlowsMax = 2;
            public float BlowGap = 5f;                 // seconds between two blows of one surfacing
            public float Wander = 25f;                 // degrees of weaving either side of the course
            public float GlideChance, LobtailChance;   // finale of a surfacing
            public float CallEvery = 30f;              // mean seconds between two songs / calls
            public string Species = "whale";
        }

        private const string ZdoMode = "wl_sea_mode";
        private static readonly int s_modeHash = ZdoMode.GetStableHashCode();
        private const int Cruise = 0, Ascend = 1, Breathe = 2;

        public Settings S = new Settings();
        private ZNetView _nview;
        private ParticleSystem _spout, _splash;
        private ProcSwimmer _anim;
        private AudioSource _audio;
        private Transform _blowhole, _fluke;
        private Vector3 _target;
        private float _retarget, _surfaceTimer, _vy, _seed;
        private float _modeTimer, _breathTimer, _breath;
        private float _stateTime, _nextBlow, _flee, _callTimer;
        private int _mode, _state, _blowsLeft, _plainDives;
        private bool _breaching;

        internal void Set(Settings s) { S = s; }

        internal string State => _breaching ? "breach" : _mode == ProcSwimmer.Glide ? "glide" : _mode == ProcSwimmer.Lobtail ? "lobtail" :
            _flee > 0f ? "flee" : _state == Ascend ? "ascend" : _state == Breathe ? "breathe" : "cruise";

        private void OnEnable() { SeaSwimmerRegistry.All.Add(this); }

        private void OnDisable() { SeaSwimmerRegistry.All.Remove(this); }

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _anim = GetComponent<ProcSwimmer>();
            foreach (var t in GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Blowhole") _blowhole = t;
                else if (t.name == "Fluke") _fluke = t;
            }
            _spout = _blowhole != null ? _blowhole.GetComponent<ParticleSystem>() : null;
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 8f;
            _audio.maxDistance = 140f;
            _audio.dopplerLevel = 0f;
            _seed = Random.Range(0f, 100f);
            _surfaceTimer = Random.Range(S.SurfaceEvery * 0.3f, S.SurfaceEvery);
            _callTimer = Random.Range(3f, S.CallEvery);
            if (_anim != null)
                _anim.OnClipEvent = OnClipEvent;
            var hp = GetComponent<Destructible>();
            if (hp != null)
            {
                hp.m_onDamaged += OnDamaged;
                hp.m_onDestroyed += OnKilled;
            }
        }

        private void Start()
        {
            if (AudioMan.instance != null)
                _audio.outputAudioMixerGroup = AudioMan.instance.m_ambientMixer;
            if (_nview == null || !_nview.IsValid())
                return;
            _nview.Register("WL_Spout", RPC_Spout);
            _nview.Register("WL_Lobtail", RPC_Lobtail);
            if (_nview.IsOwner() && ZoneSystem.instance != null)
            {
                var p = transform.position;
                p.y = Water() - S.Depth;
                transform.position = p;
                PickTarget();
            }
        }

        private void OnDestroy()
        {
            if (_splash != null)
                Destroy(_splash.transform.parent != null ? _splash.transform.parent.gameObject : _splash.gameObject);
        }

        /// <summary>Everyone sees and hears the blow.</summary>
        private void RPC_Spout(long sender)
        {
            if (_spout != null)
                _spout.Play(true);
            _anim?.Spout();
            Play(S.Species, Sea.BlowVolume);
        }

        /// <summary>Everyone sees the tail slaps (the splashes come from the clip's events, on each client).</summary>
        private void RPC_Lobtail(long sender)
        {
            _anim?.StartLobtail();
        }

        private void Play(string key, float volume)
        {
            if (Sea.Blows.TryGetValue(key, out var clips) && clips.Length > 0)
            {
                _audio.pitch = Random.Range(0.93f, 1.07f);
                _audio.PlayOneShot(clips[Random.Range(0, clips.Length)], volume);
            }
        }

        private void OnClipEvent(string ev)
        {
            if (ev != "slap")
                return;
            Vector3 at = _fluke != null ? _fluke.position : transform.position - transform.forward * 5f;
            at.y = Surface(at);
            Splash(at);
            Play("whale_slap", Sea.SlapVolume);
        }

        /// <summary>White water thrown up by the flukes: a burst of spray and a ring of foam spreading on the surface.</summary>
        private void Splash(Vector3 at)
        {
            if (_splash == null)
                _splash = Sea.MakeSplash(_spout != null ? _spout.GetComponent<ParticleSystemRenderer>().sharedMaterial : null);
            if (_splash == null)
                return;
            _splash.transform.position = at;
            _splash.Play(true);
        }

        private static float Water() => ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : 30f;

        /// <summary>The water surface at a point, waves included (flat sea level where no water volume is loaded).</summary>
        internal static float Surface(Vector3 p)
        {
            float h = Floating.GetLiquidLevel(p, 1f, LiquidType.Water);
            return h > -1000f ? h : Water();
        }

        private static float Ground(Vector3 p) => ZoneSystem.instance.GetGroundHeight(p);

        private bool Deep(Vector3 p, float depth) => Ground(p) < Water() - depth;

        /// <summary>
        /// Waypoints stay within ~70 m of the nearest player: only the owner moves the animal, and the game drops
        /// ownership of objects outside the players' active area, which left whales frozen in the distance.
        /// </summary>
        private void PickTarget()
        {
            var player = Player.GetClosestPlayer(transform.position, 250f);
            Vector3 centre = player != null ? player.transform.position : transform.position;
            for (int i = 0; i < 16; i++)
            {
                Vector2 c = Random.insideUnitCircle * 70f;
                var t = centre + new Vector3(c.x, 0f, c.y);
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

        // ------------------------------------------------------------ hunting

        /// <summary>Hit (on the owner): drop whatever show is on and sprint away from the closest player, deep.</summary>
        private void OnDamaged()
        {
            Scare(true);
            if (S.Species != "orca")
                return;
            foreach (var other in SeaSwimmerRegistry.All)     // the pod flees together
                if (other != null && other != this && other.S.Species == S.Species &&
                    Vector3.Distance(other.transform.position, transform.position) < 50f)
                    other.Scare(false);
        }

        internal void Scare(bool hit)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
                return;
            if (_flee <= 0f)
                Plugin.Log.LogInfo(name + (hit ? " hit" : " alarmed") + ": flees");
            _flee = Random.Range(20f, 30f);
            _state = Cruise;
            _surfaceTimer = S.SurfaceEvery;
            if (_mode != ProcSwimmer.Swim)
                SetMode(ProcSwimmer.Swim, 0f);
            var player = Player.GetClosestPlayer(transform.position, 200f);
            if (player == null)
                return;
            Vector3 away = transform.position - player.transform.position;
            away.y = 0f;
            for (int i = 0; i < 8; i++)
            {
                var t = transform.position + Quaternion.Euler(0f, (i % 2 == 0 ? 1 : -1) * i * 20f, 0f) * away.normalized * 80f;
                if (Deep(t, S.MinDepth))
                {
                    _target = t;
                    _retarget = 30f;
                    return;
                }
            }
        }

        /// <summary>Killed (on the owner, just before the object goes): the loot floats up.</summary>
        private void OnKilled()
        {
            Sea.DropLoot(S.Species, transform.position);
        }

        // ------------------------------------------------------- surfacing

        /// <summary>Test bench (ta_sea act): surface now, glide, slap the tail or leap, on the owner.</summary>
        internal string Force(string what)
        {
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner())
                return "not the owner";
            switch (what)
            {
                case "blow":
                    SetMode(ProcSwimmer.Swim, 0f);
                    StartSurfacing();
                    return "surfacing to blow " + _blowsLeft + " times";
                case "glide":
                case "lobtail":
                case "breach":
                    return Finale(what) ? what : "can't " + what;
                case "flee":
                    Scare(true);
                    return "fleeing";
            }
            return "unknown: " + what;
        }

        private void SetMode(int mode, float seconds)
        {
            _mode = mode;
            _modeTimer = seconds;
            if (_nview.GetZDO().GetInt(s_modeHash) != mode)
                _nview.GetZDO().Set(s_modeHash, mode);
        }

        private void StartSurfacing()
        {
            _state = Ascend;
            _stateTime = 0f;
            _blowsLeft = Random.Range(S.BlowsMin, S.BlowsMax + 1);
        }

        private void EndSurfacing()
        {
            _state = Cruise;
            _surfaceTimer = Random.Range(S.SurfaceEvery * 0.7f, S.SurfaceEvery * 1.3f);
        }

        private void Blow(float holeOut)
        {
            _blowsLeft--;
            _nextBlow = S.BlowGap * Random.Range(0.75f, 1.3f);
            Plugin.Log.LogDebug(name + " blows (" + _blowsLeft + " left): blowhole " + holeOut.ToString("F2") + " m from the surface");
            _nview.InvokeRPC(ZNetView.Everybody, "WL_Spout");
        }

        /// <summary>The end of a surfacing: a show (leap, glide, lobtail) or a plain dive. Logged, to check rates.</summary>
        private void PickFinale()
        {
            var p = transform.position;
            bool deep = Deep(p, S.MinDepth * 0.6f);
            bool canGlide = _anim != null && !float.IsNaN(_anim.ClipWater(ProcSwimmer.Glide));
            bool canLobtail = _anim != null && _anim.LobtailLength > 0f;
            bool force = _plainDives >= 2;
            float r = Random.value;
            string what = "dive";
            if (deep)
            {
                if (S.BreachChance > 0f && r < S.BreachChance) what = "breach";
                else if (canGlide && (r < S.BreachChance + S.GlideChance || force)) what = "glide";
                else if (canLobtail && (r < S.LobtailChance || force)) what = "lobtail";
            }
            var player = Player.GetClosestPlayer(p, 500f);
            Plugin.Log.LogInfo(name + " surfacing over: " + what + (deep ? "" : " (too shallow for a show)") + (force ? " (forced)" : "") +
                               ", closest player " + (player != null ? Vector3.Distance(p, player.transform.position).ToString("F0") + " m" : "none"));
            _plainDives = what == "dive" ? _plainDives + 1 : 0;
            if (what == "dive" || !Finale(what))
                EndSurfacing();
        }

        private bool Finale(string what)
        {
            switch (what)
            {
                case "breach":
                    SetMode(ProcSwimmer.Swim, 0f);
                    _state = Cruise;
                    _breaching = true;
                    _vy = Random.Range(7f, 9f);
                    return true;
                case "glide":
                    if (_anim == null || float.IsNaN(_anim.ClipWater(ProcSwimmer.Glide))) return false;
                    _state = Cruise;
                    SetMode(ProcSwimmer.Glide, Random.Range(7f, 11f));
                    _breathTimer = Random.Range(3f, 5f);
                    return true;
                case "lobtail":
                    if (_anim == null || _anim.LobtailLength <= 0f) return false;
                    _state = Cruise;
                    SetMode(ProcSwimmer.Lobtail, _anim.LobtailLength);
                    _nview.InvokeRPC(ZNetView.Everybody, "WL_Lobtail");
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------ update

        private void Update()
        {
            if (_nview == null || !_nview.IsValid() || ZoneSystem.instance == null)
                return;
            if (_anim != null)
                _anim.Mode = _nview.GetZDO().GetInt(s_modeHash);
            Calls();
            if (!_nview.IsOwner())
            {
                // nobody owns it any more (it drifted out of every active area): the closest player takes it over
                if (_nview.GetZDO().GetOwner() == 0L && Player.m_localPlayer != null &&
                    Player.GetClosestPlayer(transform.position, 300f) == Player.m_localPlayer)
                    _nview.ClaimOwnership();
                return;
            }
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
                if (_vy < 0f && p.y < Surface(p) - 1.5f)
                {
                    _breaching = false;
                    EndSurfacing();
                }
                return;
            }

            if (_mode != ProcSwimmer.Swim)
            {
                SurfaceBehaviour(dt, p);
                return;
            }

            _flee = Mathf.Max(0f, _flee - dt);
            _retarget -= dt;
            if (_retarget <= 0f || Vector3.Distance(new Vector3(p.x, 0, p.z), new Vector3(_target.x, 0, _target.z)) < 12f)
                PickTarget();
            if (!Deep(p + transform.forward * 25f, S.MinDepth * 0.6f))
                PickTarget();   // shallows ahead
            if (_state == Cruise && _flee <= 0f)
            {
                _surfaceTimer -= dt;
                if (_surfaceTimer <= 0f)
                    StartSurfacing();
            }

            float speed = S.Speed * (_flee > 0f ? 1.8f : 1f);
            float wantY = water - (_flee > 0f ? S.Depth * 2f : S.Depth);
            bool atSurface = false;
            if (_state != Cruise)
            {
                _stateTime += dt;
                // the blowhole just clears the water where it is, crest or trough
                Vector3 hole = _blowhole != null ? _blowhole.position : p + transform.forward * 3f;
                float surface = Surface(hole);
                wantY = surface - (hole.y - p.y) + 0.15f;
                atSurface = Mathf.Abs(p.y - wantY) < 1.5f;
                float holeOut = hole.y - surface;
                if (_state == Ascend)
                {
                    if (holeOut > -0.2f || (_stateTime > 25f && atSurface))
                    {
                        Blow(holeOut);
                        _state = Breathe;
                    }
                    else if (_stateTime > 45f)
                    {
                        Plugin.Log.LogInfo(name + " gave up surfacing (blowhole " + holeOut.ToString("F1") + " m under)");
                        EndSurfacing();
                    }
                }
                else
                {
                    speed *= 0.6f;                         // breathing: slow, the back rolling at the surface
                    _nextBlow -= dt;
                    if (_nextBlow <= 0f && _blowsLeft > 0 && (holeOut > -0.2f || _nextBlow < -4f))
                        Blow(holeOut);
                    else if (_nextBlow <= 0f && _blowsLeft <= 0)
                    {
                        PickFinale();
                        return;
                    }
                }
            }
            wantY = Mathf.Max(wantY, Ground(p) + 3f);

            // weave gently around the course (two slow sines), so the path is never a straight line
            float weave = S.Wander * (Mathf.Sin(Time.time * 0.11f + _seed) * 0.7f + Mathf.Sin(Time.time * 0.047f + _seed * 2f) * 0.3f);
            if (_flee > 0f) weave *= 0.3f;
            Vector3 flat = Quaternion.Euler(0f, weave, 0f) * new Vector3(_target.x - p.x, 0f, _target.z - p.z).normalized;
            float climb = atSurface ? 0f : Mathf.Clamp((wantY - p.y) * 0.25f, -0.45f, 0.45f);
            var wanted = Quaternion.LookRotation((flat + Vector3.up * climb).normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, S.TurnRate * (_flee > 0f ? 1.6f : 1f) * dt);
            p += transform.forward * speed * dt;
            if (atSurface)
                p.y = Mathf.Lerp(p.y, wantY, 1f - Mathf.Exp(-dt * 4f));   // ride the waves
            p.y = Mathf.Min(p.y, Surface(p) - 0.3f);
            transform.position = p;
        }

        /// <summary>
        /// Songs and calls, on every client (ambient, not synced): a whale phrase every ~CallEvery s, an orca call
        /// more often, only with the local player within earshot.
        /// </summary>
        private void Calls()
        {
            _callTimer -= Time.deltaTime;
            if (_callTimer > 0f)
                return;
            _callTimer = Random.Range(S.CallEvery * 0.5f, S.CallEvery * 1.5f);
            var me = Player.m_localPlayer;
            if (me == null || Vector3.Distance(me.transform.position, transform.position) > _audio.maxDistance)
                return;
            Play(S.Species + "_call", Sea.CallVolume);
        }

        /// <summary>
        /// Glide and lobtail: the body centre held at the clip's depth under the waves (ProcSwimmer.ClipWater), level,
        /// slow (glide) or nearly still (lobtail). A gliding orca rises to breathe every few seconds.
        /// </summary>
        private void SurfaceBehaviour(float dt, Vector3 p)
        {
            _modeTimer -= dt;
            float depth = _anim.ClipWater(_mode);
            bool shallow = !Deep(p + transform.forward * 20f, S.MinDepth * 0.6f);
            if (_modeTimer <= 0f || float.IsNaN(depth) || (shallow && _mode == ProcSwimmer.Glide))
            {
                SetMode(ProcSwimmer.Swim, 0f);
                EndSurfacing();
                PickTarget();
                return;
            }
            float speed = _mode == ProcSwimmer.Glide ? S.Speed * 0.7f : 0.3f;
            float lift = 0f;
            if (_mode == ProcSwimmer.Glide)
            {
                // a breath: the back and blowhole come up through the surface for a moment, with a blow
                _breathTimer -= dt;
                if (_breathTimer <= 0f)
                {
                    _breathTimer = Random.Range(5.5f, 7.5f);
                    _breath = 1.5f;
                    _nview.InvokeRPC(ZNetView.Everybody, "WL_Spout");
                }
                if (_breath > 0f)
                {
                    _breath -= dt;
                    float u = Mathf.Sin(Mathf.Clamp01(1f - _breath / 1.5f) * Mathf.PI);
                    lift = 0.022f * _anim.LengthMeters * u * u;     // eased in and out: no pop
                }
                // keep weaving toward the waypoint, gently
                Vector3 flat = new Vector3(_target.x - p.x, 0f, _target.z - p.z);
                if (flat.sqrMagnitude > 1f)
                    transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(flat.normalized),
                        S.TurnRate * 0.5f * dt);
            }
            // level, and the centre at the clip's depth under the surface at the middle of the back
            var level = Quaternion.LookRotation(Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, level, 30f * dt);
            p += transform.forward * speed * dt;
            float wantY = Surface(p) - depth + lift;
            p.y = Mathf.Lerp(p.y, wantY, 1f - Mathf.Exp(-dt * 4f));
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

        private float _born;
        public float Age => Time.time - _born;

        private void Start()
        {
            _born = Time.time;
            Destroy(gameObject, Seconds);
        }
    }
}
