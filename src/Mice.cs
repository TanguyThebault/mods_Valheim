using System.Collections.Generic;
using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Field mice: tiny creatures on the Hare's skeleton and animations, with their own proportions (small ears,
    /// long tail, bigger head, by scaling bones), own coat, own ragdoll and own squeaks. They scurry and flee in
    /// zig-zags, are prey for owls, and rarely leave a scrap of meat.
    /// </summary>
    internal static class Mice
    {
        public const string CreaturePrefab = "MeadowMouse";
        public const string MeatPrefab = "MeadowMouseMeat";
        public const string CookedPrefab = "MeadowMouseCooked";

        private static readonly Color MouseBrown = new Color(0.54f, 0.41f, 0.29f, 1f);
        private static ConfigEntry<float> s_scale;
        private static ConfigEntry<float> s_meatChance;
        private static ConfigEntry<int> s_maxSpawned;
        private static ConfigEntry<float> s_spawnChance;
        private static ConfigEntry<Vector3> s_earScale;
        private static ConfigEntry<Vector3> s_tailScale;
        private static ConfigEntry<float> s_fitScale, s_fitLift, s_fitForward, s_fitPitch;
        private static ConfigEntry<float> s_rigSize;
        private static ConfigEntry<bool> s_useRig;
        private static GameObject s_prefab, s_ragdoll;
        private static ModelData s_model;

        public static void BindConfig(ConfigFile config)
        {
            s_scale = config.Bind("Mouse", "Scale", 0.88f, "Size relative to the vanilla hare (restart).");
            s_meatChance = config.Bind("Mouse", "MeatChance", 15f, "Chance (%) that a mouse leaves meat (restart).");
            s_maxSpawned = config.Bind("Mouse", "MaxSpawned", 4, "Max mice around a player (restart).");
            s_spawnChance = config.Bind("Mouse", "SpawnChance", 25f, "Spawn chance per spawn check (every 60 s), % (restart).");
            s_earScale = config.Bind("Mouse", "EarScale", new Vector3(0.55f, 0.45f, 0.55f), "Scale of the ear bones (restart).");
            s_useRig = config.Bind("MouseRig", "Enabled", true,
                "Use our own mouse skeleton and procedural animation (off: the generated mouse on the hare skeleton) (restart).");
            s_rigSize = config.Bind("MouseRig", "Length", 0.55f, "Mouse length nose to tail tip at creature scale 1, m (restart).");
            s_fitScale = config.Bind("MouseFit", "Scale", 1f, "Size of the generated mouse on the hare skeleton (refit in the lab, or restart).");
            s_fitLift = config.Bind("MouseFit", "Lift", 0f, "Raise (+) or lower (-) the mouse, in hare body lengths.");
            s_fitForward = config.Bind("MouseFit", "Forward", 0f, "Move the mouse forward (+) or back (-), in hare body lengths.");
            s_fitPitch = config.Bind("MouseFit", "Pitch", 0f, "Tilt the mouse nose up (-) or down (+), degrees.");
            s_tailScale = config.Bind("Mouse", "TailScale", new Vector3(0.5f, 3f, 0.5f), "Scale of the tail bone (restart).");
        }

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "enemy_meadowmouse", "Field mouse" },
                { "item_mousemeat", "Mouse meat" },
                { "item_mousemeat_desc", "Barely a mouthful. Better grilled." },
                { "item_mousemeat_cooked", "Grilled mouse" },
                { "item_mousemeat_cooked_desc", "A crunchy snack for the hungry wanderer." },
            });
            loc.AddTranslation("French", new Dictionary<string, string>
            {
                { "enemy_meadowmouse", "Mulot" },
                { "item_mousemeat", "Viande de mulot" },
                { "item_mousemeat_desc", "À peine une bouchée. Meilleure grillée." },
                { "item_mousemeat_cooked", "Mulot grillé" },
                { "item_mousemeat_cooked_desc", "Un en-cas croustillant pour le voyageur affamé." },
            });
        }

        public static void Register()
        {
            RegisterItems();
            RegisterCreature();
        }

        private static void RegisterItems()
        {
            var meat = new CustomItem(MeatPrefab, "DeerMeat", new ItemConfig
            {
                Name = "$item_mousemeat",
                Description = "$item_mousemeat_desc",
                Weight = 0.1f,
            });
            meat.ItemPrefab.transform.localScale *= 0.35f;
            Rabbits.SetIcon(meat);
            ItemManager.Instance.AddItem(meat);

            var cooked = new CustomItem(CookedPrefab, "CookedDeerMeat", new ItemConfig
            {
                Name = "$item_mousemeat_cooked",
                Description = "$item_mousemeat_cooked_desc",
                Weight = 0.1f,
            });
            cooked.ItemPrefab.transform.localScale *= 0.35f;
            var food = cooked.ItemDrop.m_itemData.m_shared;
            food.m_food = 8f;
            food.m_foodStamina = 10f;
            food.m_foodBurnTime = 600f;
            food.m_foodRegen = 1f;
            Rabbits.SetIcon(cooked);
            ItemManager.Instance.AddItem(cooked);

            ItemManager.Instance.AddItemConversion(new CustomItemConversion(new CookingConversionConfig
            {
                Station = "piece_cookingstation",
                FromItem = MeatPrefab,
                ToItem = CookedPrefab,
                CookTime = 12f,
            }));
        }

        private static void RegisterCreature()
        {
            var config = new CreatureConfig
            {
                Name = "$enemy_meadowmouse",
                Faction = Character.Faction.AnimalsVeg,
            };
            config.AddDropConfig(new DropConfig { Item = MeatPrefab, Chance = s_meatChance.Value, MinAmount = 1, MaxAmount = 1 });
            config.AddSpawnConfig(new SpawnConfig
            {
                Name = CreaturePrefab + "_Meadows_BlackForest",
                Biome = Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest,
                SpawnChance = s_spawnChance.Value,
                SpawnInterval = 60f,
                SpawnDistance = 25f,
                MaxSpawned = s_maxSpawned.Value,
                MinGroupSize = 1,
                MaxGroupSize = 3,
                GroupRadius = 3f,
                MinLevel = 1,
                MaxLevel = 1,
                SpawnInForest = true,
                SpawnOutsideForest = true,
                SpawnAtDay = true,
                SpawnAtNight = true,
                MinAltitude = 1f,
                MaxTilt = 30f,
                HuntPlayer = false,
            });

            var mouse = new CustomCreature(CreaturePrefab, "Hare", config);
            var go = mouse.Prefab;
            go.transform.localScale *= s_scale.Value;
            var mask = Look.Mask("mouse");
            var model = ModelData.Load("mouse");
            s_prefab = go;
            s_model = model;
            bool rigged = model != null && s_useRig.Value && UseMouseRig(go, model);
            bool modelled = rigged || (model != null && UseMouseModel(go, model));
            if (!modelled)
            {
                Proportions(go);
                Look.Paint(go, mask, MouseBrown);
                Look.NaturalLevels(go);
            }

            var character = go.GetComponent<Character>();
            character.m_name = config.Name;   // shown above the health bar
            character.m_health = 3f;
            character.m_runSpeed = 6.5f;
            character.m_speed = 1.6f;
            character.m_acceleration *= 2f;
            character.m_turnSpeed *= 2f;
            character.m_runTurnSpeed *= 2.2f;
            if (rigged)
                MouseCorpse(character, model);
            else
                Look.OwnRagdolls(character.m_deathEffects, CreaturePrefab, mask, MouseBrown);
            foreach (var ed in character.m_deathEffects.m_effectPrefabs)
                if (ed?.m_prefab != null && ed.m_prefab.GetComponent<Ragdoll>() != null)
                {
                    if (!modelled || !UseMouseModel(ed.m_prefab, model))
                        Proportions(ed.m_prefab);
                    else
                        s_ragdoll = ed.m_prefab;
                }

            var ai = go.GetComponent<AnimalAI>();
            if (ai != null)
            {
                ai.m_viewRange = 12f;
                ai.m_hearRange = 10f;
                ai.m_fleeRange = 8f;
                ai.m_fleeAngle = 80f;
                ai.m_fleeInterval = 0.4f;
                ai.m_randomMoveInterval = 4f;
                ai.m_randomMoveRange = 2f;
                ai.m_timeToSafe = 4f;
            }

            var template = Look.FindSfxTemplate(character.m_hitEffects, character.m_deathEffects, ai?.m_alertedEffects, ai?.m_idleSound);
            var squeak = Look.MakeSfx(CreaturePrefab + "_sfx_squeak", template, Look.LoadClips("sfx_mouse", "mouse_squeak"), 0.95f, 1.15f);
            character.m_hitEffects = Look.Voice(character.m_hitEffects, squeak);
            character.m_critHitEffects = Look.Voice(character.m_critHitEffects, squeak);
            character.m_deathEffects = Look.Voice(character.m_deathEffects, squeak);
            if (ai != null)
            {
                ai.m_alertedEffects = Look.Voice(ai.m_alertedEffects, squeak);
                ai.m_idleSound = Look.Voice(ai.m_idleSound, null);
            }

            go.AddComponent<MouseTag>();
            Spawns.AddDespawn(go);
            CreatureManager.Instance.AddCreature(mouse);
            Plugin.Log.LogInfo("Registered " + CreaturePrefab + " (Hare skeleton), meat " + s_meatChance.Value + "%");
        }

        /// <summary>
        /// Our own mouse: the hare (AI, physics, network, Animator) stays underneath but invisible; the visible
        /// mouse is the generated model on a skeleton built from its own geometry, animated in code from its
        /// actual movement (trot, bound, sniff, ears, tail).
        /// </summary>
        private static bool UseMouseRig(GameObject go, ModelData model)
        {
            var hareRenderers = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (hareRenderers.Length == 0)
                return false;
            var template = hareRenderers[0].sharedMaterial;
            foreach (var r in hareRenderers) r.enabled = false;
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true)) lod.enabled = false;
            var bones = ProcRig.MouseBones(model, out var allowed);
            var rig = ProcRig.Build(go, model, bones, allowed, template, s_rigSize.Value, "mouse_rig");
            go.AddComponent<ProcQuadruped>().Init(rig);
            Plugin.Log.LogInfo("Mouse: own skeleton, " + bones.Count + " bones");
            return true;
        }

        /// <summary>No hare ragdoll for a rigged mouse: a small corpse of our own (loot then drops at once).</summary>
        private static void MouseCorpse(Character character, ModelData model)
        {
            var kept = new List<EffectList.EffectData>();
            foreach (var ed in character.m_deathEffects.m_effectPrefabs)
                if (ed?.m_prefab != null && ed.m_prefab.GetComponent<Ragdoll>() == null)
                    kept.Add(ed);
            var corpse = PrefabManager.Instance.CreateEmptyPrefab(CreaturePrefab + "_corpse", false);
            foreach (var c in corpse.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (var c in corpse.GetComponents<MeshRenderer>()) Object.DestroyImmediate(c);
            foreach (var c in corpse.GetComponents<MeshFilter>()) Object.DestroyImmediate(c);
            var body = new GameObject("body");
            body.transform.SetParent(corpse.transform, false);
            float s = s_rigSize.Value / model.Bounds.size.z;
            body.transform.localScale = Vector3.one * s;
            body.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);   // lying on its side
            var origin = new Vector3(model.Bounds.center.x, model.Bounds.center.y, model.Bounds.center.z);
            var mesh = new Mesh { name = "mouse_corpse" };
            var verts = new Vector3[model.Pos.Length];
            for (int i = 0; i < verts.Length; i++) verts[i] = model.Pos[i] - origin;
            mesh.vertices = verts;
            mesh.normals = model.Nrm;
            mesh.uv = model.Uv;
            mesh.triangles = model.Idx;
            mesh.RecalculateBounds();
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            var template = character.GetComponentInChildren<SkinnedMeshRenderer>(true).sharedMaterial;
            var mr = body.AddComponent<MeshRenderer>();
            Models.UseTexture(mr, model.Tex, "mouse_corpse");
            if (template != null && mr.sharedMaterial == null) mr.sharedMaterial = template;
            corpse.AddComponent<SelfDestruct>().Seconds = 12f;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(corpse, false));
            kept.Add(new EffectList.EffectData { m_prefab = corpse, m_enabled = true, m_inheritParentScale = true });
            character.m_deathEffects = new EffectList { m_effectPrefabs = kept.ToArray() };
        }

        /// <summary>Re-applies the generated mouse with the current [MouseFit] values (new spawns use it).</summary>
        internal static string Refit()
        {
            if (s_prefab == null || s_model == null)
                return "mouse: nothing to refit";
            bool ok = UseMouseModel(s_prefab, s_model);
            if (s_ragdoll != null)
                ok &= UseMouseModel(s_ragdoll, s_model);
            return "mouse refit " + (ok ? "ok" : "failed") + " (scale " + s_fitScale.Value + ", lift " + s_fitLift.Value +
                   ", forward " + s_fitForward.Value + ", pitch " + s_fitPitch.Value + ")";
        }

        /// <summary>The generated mouse model (fal Trellis), skinned to the hare skeleton, with its own texture.</summary>
        private static bool UseMouseModel(GameObject go, ModelData model)
        {
            var smrs = go.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (smrs.Length == 0)
                return false;
            foreach (var smr in smrs)
            {
                if (!Models.MouseOnHare(smr, model, s_fitScale.Value, s_fitLift.Value, s_fitForward.Value, s_fitPitch.Value))
                    return false;
                Models.UseTexture(smr, model.Tex, go.name);
            }
            return true;
        }

        /// <summary>Fallback mouse proportions on the hare skeleton: small round ears, long tail, bigger head.</summary>
        private static void Proportions(GameObject go)
        {
            int found = 0;
            foreach (var t in go.GetComponentsInChildren<Transform>(true))
            {
                switch (t.name)
                {
                    case "Ear.l":
                    case "Ear.r":
                        t.localScale = Vector3.Scale(t.localScale, s_earScale.Value);
                        found++;
                        break;
                    case "Tail":
                        t.localScale = Vector3.Scale(t.localScale, s_tailScale.Value);
                        found++;
                        break;
                    case "Head":
                        t.localScale *= 1.15f;
                        found++;
                        break;
                }
            }
            if (found < 4)
                Plugin.Log.LogWarning("Mouse proportions: only " + found + "/4 bones found on " + go.name);
        }
    }

    /// <summary>Marks mice (prey of owls; settle after fleeing like rabbits).</summary>
    public class MouseTag : MonoBehaviour
    {
        internal static readonly HashSet<Character> All = new HashSet<Character>();
        private Character _character;

        private void Awake()
        {
            _character = GetComponent<Character>();
            if (_character != null)
                All.Add(_character);
        }

        private void OnDestroy()
        {
            if (_character != null)
                All.Remove(_character);
        }
    }
}
