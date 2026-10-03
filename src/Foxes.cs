using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ThrowingAxe
{
    /// <summary>
    /// Meadow foxes: a small, red-tinted clone of the Wolf (MonsterAI, run and bite animations) that hunts
    /// rabbits, keeps away from players unless hit, and drops fox meat and fox pelt. The pelt makes a shaman
    /// set: a headdress (the wolf trophy head with its lower jaw cut off, worn on the head) and a cape.
    /// </summary>
    internal static class Foxes
    {
        public const string CreaturePrefab = "MeadowFox";
        public const string MeatPrefab = "MeadowFoxMeat";
        public const string CookedPrefab = "MeadowFoxCooked";
        public const string PeltPrefab = "MeadowFoxPelt";
        public const string HeaddressPrefab = "MeadowFoxHeaddress";
        public const string CapePrefab = "MeadowFoxCape";

        internal static readonly Color FoxRed = new Color(1.0f, 0.55f, 0.25f, 1f);

        private static ConfigEntry<float> s_scale;
        private static ConfigEntry<float> s_biteDamage;
        private static ConfigEntry<float> s_spawnChance;
        internal static ConfigEntry<float> KeepAwayDistance;
        internal static ConfigEntry<Vector3> HeadOffset;
        internal static ConfigEntry<Vector3> HeadRotation;
        internal static ConfigEntry<Vector3> HeadScale;

        public static void BindConfig(ConfigFile config)
        {
            s_scale = config.Bind("Fox", "Scale", 0.5f, "Size relative to the vanilla wolf (restart).");
            s_biteDamage = config.Bind("Fox", "BiteDamage", 8f, "Total damage of a fox bite (restart).");
            s_spawnChance = config.Bind("Fox", "SpawnChance", 20f, "Spawn chance per spawn check, % (restart).");
            KeepAwayDistance = config.Bind("Fox", "KeepAwayDistance", 10f, "Foxes back off from players closer than this, m (live).");
            HeadOffset = config.Bind("FoxHeaddress", "Offset", new Vector3(0f, 0.12f, 0.02f),
                "Position of the fox head relative to the head joint, m (live).");
            HeadRotation = config.Bind("FoxHeaddress", "Rotation", Vector3.zero, "Rotation of the fox head, degrees (live).");
            HeadScale = config.Bind("FoxHeaddress", "Scale", new Vector3(0.8f, 0.85f, 0.9f),
                "Scale of the fox head (narrower than the wolf trophy) (live).");
        }

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "enemy_meadowfox", "Fox" },
                { "item_foxmeat", "Fox meat" },
                { "item_foxmeat_desc", "Dark, gamey meat. Cook it first." },
                { "item_foxmeat_cooked", "Roasted fox" },
                { "item_foxmeat_cooked_desc", "Strong-tasting, but filling." },
                { "item_foxpelt", "Fox pelt" },
                { "item_foxpelt_desc", "A thick red pelt. Some say it still remembers how to hunt." },
                { "item_foxheaddress", "Fox headdress" },
                { "item_foxheaddress_desc", "A fox head worn over your own. No protection, but its spirit guides your breath and your fists." },
                { "item_foxcape", "Fox cape" },
                { "item_foxcape_desc", "A red fox-fur cape. No protection, but light on the shoulders and quick in a brawl." },
                { "se_foxheaddress", "Fox spirit" },
                { "se_foxcape", "Fox fur" },
                { "se_fox_tooltip", "Stamina regen +10%, unarmed +10, unarmed damage +10%" },
            });
            loc.AddTranslation("French", new Dictionary<string, string>
            {
                { "enemy_meadowfox", "Renard" },
                { "item_foxmeat", "Viande de renard" },
                { "item_foxmeat_desc", "Une viande sombre au goût de gibier. À cuire d'abord." },
                { "item_foxmeat_cooked", "Renard rôti" },
                { "item_foxmeat_cooked_desc", "Un goût fort, mais ça cale." },
                { "item_foxpelt", "Peau de renard" },
                { "item_foxpelt_desc", "Une épaisse fourrure rousse. On dit qu'elle se souvient encore de la chasse." },
                { "item_foxheaddress", "Coiffe de renard" },
                { "item_foxheaddress_desc", "Une tête de renard portée sur la tienne. Aucune protection, mais son esprit guide ton souffle et tes poings." },
                { "item_foxcape", "Cape de renard" },
                { "item_foxcape_desc", "Une cape en fourrure de renard. Aucune protection, mais légère et vive au corps à corps." },
                { "se_foxheaddress", "Esprit du renard" },
                { "se_foxcape", "Fourrure de renard" },
                { "se_fox_tooltip", "Régén. d'endurance +10 %, main nue +10, dégâts à main nue +10 %" },
            });
        }

        public static void Register()
        {
            Rabbits.DumpPrefab("Wolf");
            Rabbits.DumpPrefab("WolfMeat");
            Rabbits.DumpPrefab("CookedWolfMeat");
            Rabbits.DumpPrefab("WolfPelt");
            Rabbits.DumpPrefab("HelmetBronze");
            Rabbits.DumpPrefab("CapeWolf");
            Rabbits.DumpPrefab("TrophyWolf");

            RegisterItems();
            RegisterCreature();
            RegisterHeaddress();
            RegisterCape();
        }

        // ---------------------------------------------------------------- items

        private static void RegisterItems()
        {
            var meat = new CustomItem(MeatPrefab, "WolfMeat", new ItemConfig
            {
                Name = "$item_foxmeat",
                Description = "$item_foxmeat_desc",
                Weight = 0.7f,
            });
            meat.ItemPrefab.transform.localScale *= 0.75f;
            Rabbits.SetIcon(meat);
            ItemManager.Instance.AddItem(meat);

            var cooked = new CustomItem(CookedPrefab, "CookedWolfMeat", new ItemConfig
            {
                Name = "$item_foxmeat_cooked",
                Description = "$item_foxmeat_cooked_desc",
                Weight = 0.7f,
            });
            cooked.ItemPrefab.transform.localScale *= 0.75f;
            var food = cooked.ItemDrop.m_itemData.m_shared;
            // Meadows tier, richer than rabbit (more health, less stamina).
            food.m_food = 28f;
            food.m_foodStamina = 10f;
            food.m_foodBurnTime = 1200f;
            food.m_foodRegen = 2f;
            Rabbits.SetIcon(cooked);
            ItemManager.Instance.AddItem(cooked);

            ItemManager.Instance.AddItemConversion(new CustomItemConversion(new CookingConversionConfig
            {
                Station = "piece_cookingstation",
                FromItem = MeatPrefab,
                ToItem = CookedPrefab,
                CookTime = 25f,
            }));

            string peltBase = PrefabManager.Cache.GetPrefab<ItemDrop>("WolfPelt") != null ? "WolfPelt" : "DeerHide";
            var pelt = new CustomItem(PeltPrefab, peltBase, new ItemConfig
            {
                Name = "$item_foxpelt",
                Description = "$item_foxpelt_desc",
                Weight = 0.8f,
            });
            Rabbits.Tint(pelt.ItemPrefab, FoxRed);
            pelt.ItemDrop.m_itemData.m_shared.m_maxStackSize = 50;
            Rabbits.SetIcon(pelt);
            ItemManager.Instance.AddItem(pelt);
        }

        // ------------------------------------------------------------- creature

        private static void RegisterCreature()
        {
            var config = new CreatureConfig
            {
                Name = "$enemy_meadowfox",
                Faction = Character.Faction.AnimalsVeg,
            };
            config.AddDropConfig(new DropConfig { Item = MeatPrefab, Chance = 100f, MinAmount = 1, MaxAmount = 2, LevelMultiplier = true });
            config.AddDropConfig(new DropConfig { Item = PeltPrefab, Chance = 100f, MinAmount = 1, MaxAmount = 1, LevelMultiplier = true });
            config.AddSpawnConfig(new SpawnConfig
            {
                Name = CreaturePrefab + "_Meadows",
                Biome = Heightmap.Biome.Meadows,
                SpawnChance = s_spawnChance.Value,
                SpawnInterval = 240f,
                SpawnDistance = 40f,
                MaxSpawned = 1,
                MinGroupSize = 1,
                MaxGroupSize = 1,
                MinLevel = 1,
                MaxLevel = 2,
                SpawnInForest = true,
                SpawnOutsideForest = true,
                SpawnAtDay = true,
                SpawnAtNight = true,
                MinAltitude = 1f,
                HuntPlayer = false,
            });

            var fox = new CustomCreature(CreaturePrefab, "Wolf", config);
            var go = fox.Prefab;
            go.transform.localScale *= s_scale.Value;
            Rabbits.Tint(go, FoxRed);

            // Not a pet, not a breeder.
            foreach (var c in go.GetComponents<Tameable>()) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<Procreation>()) Object.DestroyImmediate(c);

            var character = go.GetComponent<Character>();
            character.m_health = 20f;
            character.m_runSpeed = 9f;      // a hair slower than a rabbit (9.45), which also zig-zags
            character.m_speed = 3f;
            character.m_acceleration *= 1.3f;
            character.m_runTurnSpeed *= 1.4f;
            character.m_group = "";

            var ai = go.GetComponent<MonsterAI>();
            if (ai != null)
            {
                ai.m_viewRange = 30f;
                ai.m_attackPlayerObjects = false;   // never bites buildings
                ai.m_enableHuntPlayer = false;
                ai.m_fleeIfLowHealth = 0.4f;
                ai.m_idleSound = new EffectList();  // no wolf howls from a fox
                ai.m_afraidOfFire = true;
            }

            var humanoid = go.GetComponent<Humanoid>();
            if (humanoid != null)
                humanoid.m_defaultItems = humanoid.m_defaultItems?.Select(CloneWeakAttack).ToArray();

            go.AddComponent<FoxTag>();
            CreatureManager.Instance.AddCreature(fox);
            Plugin.Log.LogInfo("Registered " + CreaturePrefab + " (Wolf clone), AI " + (ai != null ? "MonsterAI" : "none") +
                               ", attacks " + (humanoid?.m_defaultItems?.Length ?? 0));
        }

        /// <summary>The wolf's bite is mountain-tier; give the fox its own, weaker copy (wolves keep theirs).</summary>
        private static GameObject CloneWeakAttack(GameObject attack)
        {
            if (attack == null || attack.GetComponent<ItemDrop>() == null)
                return attack;
            var clone = PrefabManager.Instance.CreateClonedPrefab(CreaturePrefab + "_" + attack.name, attack);
            var shared = clone.GetComponent<ItemDrop>().m_itemData.m_shared;
            float total = shared.m_damages.GetTotalDamage();
            if (total > 0f)
                shared.m_damages.Modify(s_biteDamage.Value / total);
            shared.m_attackForce *= 0.3f;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(clone, true));
            Plugin.Log.LogInfo("Fox attack " + clone.name + ": damage " + total.ToString("F0") + " -> " + shared.m_damages.GetTotalDamage().ToString("F0"));
            return clone;
        }

        // ---------------------------------------------------------- shaman set

        private static SE_Stats MakeFoxEffect(string name, string token, Sprite icon)
        {
            var se = ScriptableObject.CreateInstance<SE_Stats>();
            se.name = name;
            se.m_name = token;
            se.m_tooltip = "$se_fox_tooltip";
            se.m_icon = icon;
            se.m_staminaRegenMultiplier = 1.1f;
            se.m_skillLevel = Skills.SkillType.Unarmed;
            se.m_skillLevelModifier = 10f;
            se.m_modifyAttackSkill = Skills.SkillType.Unarmed;
            se.m_damageModifier = 1.1f;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, false));
            return se;
        }

        private static void MakeCosmetic(ItemDrop.ItemData.SharedData shared)
        {
            shared.m_armor = 0f;
            shared.m_armorPerLevel = 0f;
            shared.m_maxQuality = 1;
            shared.m_movementModifier = 0f;
            shared.m_damageModifiers = new List<HitData.DamageModPair>();
            shared.m_setName = "";
            shared.m_setSize = 0;
            shared.m_setStatusEffect = null;
        }

        // Lower jaw of the wolf trophy mesh (model space): below the mouth line, in front of the neck.
        private const float JawMaxY = -0.03f;
        private const float JawMinZ = 0.02f;

        private static void RegisterHeaddress()
        {
            var helm = new CustomItem(HeaddressPrefab, "HelmetBronze", new ItemConfig
            {
                Name = "$item_foxheaddress",
                Description = "$item_foxheaddress_desc",
                CraftingStation = CraftingStations.Workbench,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig(PeltPrefab, 3, 0, true),
                    new RequirementConfig("LeatherScraps", 2, 0, true),
                },
            });
            var root = helm.ItemPrefab.transform;
            var trophy = PrefabManager.Cache.GetPrefab<GameObject>("TrophyWolf");
            var srcAttach = trophy != null ? trophy.transform.Find("attach") : null;
            var oldAttach = root.Find("attach");
            if (srcAttach == null || oldAttach == null)
            {
                Plugin.Log.LogError("Headdress: TrophyWolf/attach or HelmetBronze/attach missing");
                return;
            }

            // The ground model: hide the helmet's own renderers (keep its colliders so it doesn't fall through).
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (!(r is ParticleSystemRenderer))
                    r.enabled = false;

            var attach = Object.Instantiate(srcAttach.gameObject, root, false);
            attach.name = "attach";
            attach.transform.localPosition = oldAttach.localPosition;
            attach.transform.localRotation = oldAttach.localRotation;
            attach.transform.localScale = Vector3.one;
            Object.DestroyImmediate(oldAttach.gameObject);
            foreach (var col in attach.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(col);

            foreach (var mf in attach.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null)
                    continue;
                if (mf.sharedMesh.isReadable)
                    mf.sharedMesh = CutJaw(mf.sharedMesh);
                else
                    Plugin.Log.LogWarning("Headdress: mesh " + mf.sharedMesh.name + " not readable, jaw kept");
                mf.gameObject.AddComponent<FoxHeadTuner>();
            }
            Rabbits.Tint(attach, FoxRed);

            var shared = helm.ItemDrop.m_itemData.m_shared;
            MakeCosmetic(shared);
            shared.m_weight = 1f;
            Rabbits.SetIcon(helm);
            shared.m_equipStatusEffect = MakeFoxEffect("SE_FoxHeaddress", "$se_foxheaddress",
                shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null);
            ItemManager.Instance.AddItem(helm);
        }

        private static Mesh CutJaw(Mesh src)
        {
            var mesh = Object.Instantiate(src);
            mesh.name = src.name + "_nojaw";
            var v = mesh.vertices;
            int removed = 0;
            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                var tris = mesh.GetTriangles(sub);
                var keep = new List<int>(tris.Length);
                for (int i = 0; i < tris.Length; i += 3)
                {
                    Vector3 c = (v[tris[i]] + v[tris[i + 1]] + v[tris[i + 2]]) / 3f;
                    if (c.y < JawMaxY && c.z > JawMinZ)
                    {
                        removed++;
                        continue;
                    }
                    keep.Add(tris[i]);
                    keep.Add(tris[i + 1]);
                    keep.Add(tris[i + 2]);
                }
                mesh.SetTriangles(keep, sub);
            }
            mesh.RecalculateBounds();
            Plugin.Log.LogInfo("Headdress: cut " + removed + " jaw triangles from " + src.name);
            return mesh;
        }

        private static void RegisterCape()
        {
            var cape = new CustomItem(CapePrefab, "CapeWolf", new ItemConfig
            {
                Name = "$item_foxcape",
                Description = "$item_foxcape_desc",
                CraftingStation = CraftingStations.Workbench,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig(PeltPrefab, 5, 0, true),
                    new RequirementConfig("LeatherScraps", 2, 0, true),
                },
            });
            Rabbits.Tint(cape.ItemPrefab, FoxRed);
            var shared = cape.ItemDrop.m_itemData.m_shared;
            MakeCosmetic(shared);
            shared.m_weight = 2f;
            shared.m_equipStatusEffect = null;
            Rabbits.SetIcon(cape);
            shared.m_equipStatusEffect = MakeFoxEffect("SE_FoxCape", "$se_foxcape",
                shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null);
            ItemManager.Instance.AddItem(cape);
        }
    }

    /// <summary>Marks foxes; remembers who provoked them.</summary>
    public class FoxTag : MonoBehaviour
    {
        internal static readonly HashSet<Character> All = new HashSet<Character>();
        private static readonly Dictionary<Character, KeyValuePair<Character, float>> s_provoked =
            new Dictionary<Character, KeyValuePair<Character, float>>();
        private const float ProvokedTime = 20f;
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
            {
                All.Remove(_character);
                s_provoked.Remove(_character);
            }
        }

        internal static void Provoke(Character fox, Character attacker)
        {
            if (fox != null && attacker != null)
                s_provoked[fox] = new KeyValuePair<Character, float>(attacker, Time.time);
        }

        internal static bool IsProvokedBy(Character fox, Character other)
        {
            return s_provoked.TryGetValue(fox, out var p) && p.Key == other && Time.time - p.Value < ProvokedTime;
        }

        internal static bool IsProvoked(Character fox)
        {
            return s_provoked.TryGetValue(fox, out var p) && p.Key != null && Time.time - p.Value < ProvokedTime;
        }
    }

    /// <summary>Applies the live-tunable pose of the fox head on the headdress model.</summary>
    public class FoxHeadTuner : MonoBehaviour
    {
        private void LateUpdate()
        {
            transform.localPosition = Foxes.HeadOffset.Value;
            transform.localRotation = Quaternion.Euler(Foxes.HeadRotation.Value);
            transform.localScale = Foxes.HeadScale.Value;
        }
    }

    /// <summary>
    /// Who fights whom: a fox only goes after rabbits (and, for a while, whoever hit it); rabbits flee foxes.
    /// Others judging a fox keep the vanilla answer (players can hunt foxes).
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character), typeof(Character) })]
    internal static class FoxEnemies
    {
        private static void Postfix(Character a, Character b, ref bool __result)
        {
            if (a == null || b == null)
                return;
            if (FoxTag.All.Contains(a))
                __result = RabbitTag.All.Contains(b) || FoxTag.IsProvokedBy(a, b);
            else if (RabbitTag.All.Contains(a) && FoxTag.All.Contains(b))
                __result = true;
        }
    }

    [HarmonyPatch(typeof(MonsterAI), "OnDamaged")]
    internal static class FoxProvoked
    {
        private static void Prefix(MonsterAI __instance, Character attacker)
        {
            var c = __instance.GetComponent<Character>();
            if (c != null && FoxTag.All.Contains(c))
                FoxTag.Provoke(c, attacker);
        }
    }

    /// <summary>An unprovoked fox backs away from a nearby player (overrides whatever MonsterAI chose to do).</summary>
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    internal static class FoxKeepAway
    {
        private static readonly MethodInfo s_flee = AccessTools.Method(typeof(BaseAI), "Flee");
        private static readonly object[] s_args = new object[2];

        private static void Postfix(MonsterAI __instance, float dt, bool __result)
        {
            if (!__result)
                return;
            var c = __instance.GetComponent<Character>();
            if (c == null || !FoxTag.All.Contains(c) || FoxTag.IsProvoked(c))
                return;
            var player = Player.GetClosestPlayer(c.transform.position, Foxes.KeepAwayDistance.Value);
            if (player == null)
                return;
            s_args[0] = dt;
            s_args[1] = player.transform.position;
            s_flee.Invoke(__instance, s_args);
        }
    }
}
