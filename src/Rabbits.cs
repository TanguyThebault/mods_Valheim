using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace ThrowingAxe
{
    /// <summary>
    /// Meadows rabbits: a re-skinned clone of the Mistlands Hare (Iron Gate's own low-poly model and hop
    /// animations), with skittish AnimalAI tuning (zig-zag fleeing), and their own hide, raw and cooked meat.
    /// </summary>
    internal static class Rabbits
    {
        public const string CreaturePrefab = "MeadowRabbit";
        public const string HidePrefab = "MeadowRabbitHide";
        public const string MeatPrefab = "MeadowRabbitMeat";
        public const string CookedPrefab = "MeadowRabbitCooked";

        private static ConfigEntry<Color> s_furTint;
        private static ConfigEntry<float> s_scale;
        private static ConfigEntry<float> s_speedFactor;
        private static ConfigEntry<float> s_spawnChance;
        private static ConfigEntry<int> s_maxSpawned;
        private static ConfigEntry<float> s_calmSpeed;
        private static ConfigEntry<float> s_idleInterval;
        private static ConfigEntry<float> s_idleRange;

        public static void BindConfig(ConfigFile config)
        {
            s_furTint = config.Bind("Rabbit", "FurTint", new Color(0.62f, 0.47f, 0.33f, 1f),
                "Colour multiplied into the hare's materials (restart to apply).");
            s_scale = config.Bind("Rabbit", "Scale", 0.85f, "Size relative to the vanilla hare (restart).");
            s_speedFactor = config.Bind("Rabbit", "SpeedFactor", 1.35f, "Run speed relative to the vanilla hare (restart).");
            s_calmSpeed = config.Bind("Rabbit", "CalmSpeed", 2.2f, "Speed when not frightened (vanilla hare: 4) (restart).");
            s_idleInterval = config.Bind("Rabbit", "IdleInterval", 9f, "Seconds between two small moves when calm (restart).");
            s_idleRange = config.Bind("Rabbit", "IdleRange", 4f, "Length of a calm move, m (restart).");
            s_spawnChance = config.Bind("Rabbit", "SpawnChance", 40f, "Spawn chance per spawn check, % (restart).");
            s_maxSpawned = config.Bind("Rabbit", "MaxSpawned", 3, "Max rabbits around a player (restart).");
        }

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "enemy_rabbit", "Rabbit" },
                { "item_rabbithide", "Rabbit hide" },
                { "item_rabbithide_desc", "Soft, light fur. Quick hands caught it; quicker legs nearly got away." },
                { "item_rabbitmeat", "Rabbit meat" },
                { "item_rabbitmeat_desc", "Lean meat from a meadow rabbit. Better cooked." },
                { "item_rabbitmeat_cooked", "Cooked rabbit" },
                { "item_rabbitmeat_cooked_desc", "Spit-roasted rabbit. Light, but it keeps you on your feet." },
                { "piece_rug_rabbit", "Small rabbit-fur rug" },
                { "piece_rug_rabbit_desc", "Soft under bare feet by the fire." },
                { "item_rabbitboots", "Rabbit-fur boots" },
                { "item_rabbitboots_desc", "Light fur boots. Your steps feel quicker, your jumps lighter." },
                { "se_rabbitfeet", "Rabbit feet" },
                { "se_rabbitfeet_tooltip", "Jump +10%, jump stamina -25%, fall damage -30%" },
            });
            loc.AddTranslation("French", new Dictionary<string, string>
            {
                { "enemy_rabbit", "Lapin" },
                { "item_rabbithide", "Peau de lapin" },
                { "item_rabbithide_desc", "Une fourrure douce et légère. Il a fallu des mains rapides, et des jambes plus rapides encore." },
                { "item_rabbitmeat", "Viande de lapin" },
                { "item_rabbitmeat_desc", "Une viande maigre de lapin des prairies. Meilleure cuite." },
                { "item_rabbitmeat_cooked", "Lapin rôti" },
                { "item_rabbitmeat_cooked_desc", "Du lapin rôti à la broche. Léger, mais il donne des jambes." },
                { "piece_rug_rabbit", "Petit tapis en peau de lapin" },
                { "piece_rug_rabbit_desc", "Tout doux sous les pieds, au coin du feu." },
                { "item_rabbitboots", "Bottes en peau de lapin" },
                { "item_rabbitboots_desc", "De légères bottes fourrées. Le pas est plus vif, le saut plus léger." },
                { "se_rabbitfeet", "Pattes de lapin" },
                { "se_rabbitfeet_tooltip", "Saut +10 %, endurance de saut -25 %, dégâts de chute -30 %" },
            });
        }

        public static void Register()
        {
            Dump("Hare");
            Dump("DeerHide");
            Dump("DeerMeat");
            Dump("CookedDeerMeat");
            Dump("rug_deer");

            RegisterItems();
            RegisterCreature();
            RegisterRug();
            RegisterBoots();
        }

        // ---------------------------------------------------------------- items

        private static void RegisterItems()
        {
            var hide = new CustomItem(HidePrefab, "DeerHide", new ItemConfig
            {
                Name = "$item_rabbithide",
                Description = "$item_rabbithide_desc",
                Weight = 0.5f,
            });
            Tint(hide.ItemPrefab, new Color(0.85f, 0.75f, 0.62f, 1f));
            hide.ItemPrefab.transform.localScale *= 0.7f;
            hide.ItemDrop.m_itemData.m_shared.m_maxStackSize = 50;
            SetIcon(hide);
            ItemManager.Instance.AddItem(hide);

            var meat = new CustomItem(MeatPrefab, "DeerMeat", new ItemConfig
            {
                Name = "$item_rabbitmeat",
                Description = "$item_rabbitmeat_desc",
                Weight = 0.5f,
            });
            meat.ItemPrefab.transform.localScale *= 0.7f;
            SetIcon(meat);
            ItemManager.Instance.AddItem(meat);

            var cooked = new CustomItem(CookedPrefab, "CookedDeerMeat", new ItemConfig
            {
                Name = "$item_rabbitmeat_cooked",
                Description = "$item_rabbitmeat_cooked_desc",
                Weight = 0.5f,
            });
            cooked.ItemPrefab.transform.localScale *= 0.7f;
            var food = cooked.ItemDrop.m_itemData.m_shared;
            // Meadows tier: a bit less health than cooked boar, more stamina (it's lean).
            food.m_food = 22f;
            food.m_foodStamina = 18f;
            food.m_foodBurnTime = 1200f;
            food.m_foodRegen = 2f;
            SetIcon(cooked);
            ItemManager.Instance.AddItem(cooked);

            ItemManager.Instance.AddItemConversion(new CustomItemConversion(new CookingConversionConfig
            {
                Station = "piece_cookingstation",
                FromItem = MeatPrefab,
                ToItem = CookedPrefab,
                CookTime = 20f,
            }));
        }

        private static void SetIcon(CustomItem item)
        {
            try
            {
                var sprite = RenderManager.Instance.Render(item.ItemPrefab, RenderManager.IsometricRotation);
                if (sprite != null)
                    item.ItemDrop.m_itemData.m_shared.m_icons = new[] { sprite };
                else
                    Plugin.Log.LogWarning("Icon render returned null for " + item.ItemPrefab.name);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Icon render failed for " + item.ItemPrefab.name + ": " + e.Message);
            }
        }

        // ------------------------------------------------------------- creature

        // ----------------------------------------------------------------- rug

        public const string RugPrefab = "rug_rabbit";

        /// <summary>A smaller, paler copy of the vanilla deer-hide rug, built with the hammer next to a workbench.</summary>
        private static void RegisterRug()
        {
            var rug = new CustomPiece(RugPrefab, "rug_deer", new PieceConfig
            {
                Name = "$piece_rug_rabbit",
                Description = "$piece_rug_rabbit_desc",
                PieceTable = PieceTables.Hammer,
                Category = PieceCategories.Furniture,
                CraftingStation = CraftingStations.Workbench,
                Requirements = new[] { new RequirementConfig(HidePrefab, 3, 0, true) },
            });
            rug.PiecePrefab.transform.localScale = Vector3.Scale(rug.PiecePrefab.transform.localScale, new Vector3(0.6f, 1f, 0.6f));
            Tint(rug.PiecePrefab, new Color(0.85f, 0.75f, 0.62f, 1f));
            try
            {
                var icon = RenderManager.Instance.Render(rug.PiecePrefab, RenderManager.IsometricRotation);
                if (icon != null)
                    rug.Piece.m_icon = icon;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Rug icon render failed: " + e.Message);
            }
            PieceManager.Instance.AddPiece(rug);
        }

        // --------------------------------------------------------------- boots

        public const string BootsPrefab = "MeadowRabbitBoots";

        /// <summary>
        /// Valheim has no feet slot, so the boots are leg armour (a re-tinted copy of the leather trousers),
        /// with rabbit agility: +5% move speed and an equip effect for jumps and falls.
        /// </summary>
        private static void RegisterBoots()
        {
            var boots = new CustomItem(BootsPrefab, "ArmorLeatherLegs", new ItemConfig
            {
                Name = "$item_rabbitboots",
                Description = "$item_rabbitboots_desc",
                CraftingStation = CraftingStations.Workbench,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig(HidePrefab, 4, 2, true),
                    new RequirementConfig("LeatherScraps", 2, 1, true),
                },
            });
            var fur = new Color(0.85f, 0.75f, 0.62f, 1f);
            Tint(boots.ItemPrefab, fur);
            var shared = boots.ItemDrop.m_itemData.m_shared;
            if (shared.m_armorMaterial != null)
            {
                var worn = new Material(shared.m_armorMaterial) { name = shared.m_armorMaterial.name + "_rabbit" };
                if (worn.HasProperty("_Color"))
                    worn.SetColor("_Color", worn.GetColor("_Color") * fur);
                shared.m_armorMaterial = worn;
            }
            shared.m_armor = 2f;
            shared.m_armorPerLevel = 1f;
            shared.m_weight = 1f;
            shared.m_movementModifier = 0.05f;
            SetIcon(boots);

            var se = ScriptableObject.CreateInstance<SE_Stats>();
            se.name = "SE_RabbitFeet";
            se.m_name = "$se_rabbitfeet";
            se.m_tooltip = "$se_rabbitfeet_tooltip";
            se.m_icon = shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null;
            se.m_jumpModifier = new Vector3(0f, 0.1f, 0f);
            se.m_jumpStaminaUseModifier = -0.25f;
            se.m_fallDamageModifier = -0.3f;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, false));
            shared.m_equipStatusEffect = se;

            ItemManager.Instance.AddItem(boots);
        }

        private static void RegisterCreature()
        {
            var config = new CreatureConfig
            {
                Name = "$enemy_rabbit",
                Faction = Character.Faction.AnimalsVeg,
            };
            config.AddDropConfig(new DropConfig { Item = HidePrefab, Chance = 100f, MinAmount = 1, MaxAmount = 1, LevelMultiplier = true });
            config.AddDropConfig(new DropConfig { Item = MeatPrefab, Chance = 100f, MinAmount = 1, MaxAmount = 2, LevelMultiplier = true });
            config.AddSpawnConfig(new SpawnConfig
            {
                Name = "MeadowRabbit_Meadows",
                Biome = Heightmap.Biome.Meadows,
                SpawnChance = s_spawnChance.Value,
                SpawnInterval = 90f,
                SpawnDistance = 30f,
                MaxSpawned = s_maxSpawned.Value,
                MinGroupSize = 1,
                MaxGroupSize = 3,
                GroupRadius = 6f,
                MinLevel = 1,
                MaxLevel = 2,
                SpawnInForest = false,    // open meadows
                SpawnOutsideForest = true,
                SpawnAtDay = true,
                SpawnAtNight = true,
                MinAltitude = 1f,
                MaxTilt = 25f,
                HuntPlayer = false,
            });

            var rabbit = new CustomCreature(CreaturePrefab, "Hare", config);
            var go = rabbit.Prefab;
            go.transform.localScale *= s_scale.Value;
            Tint(go, s_furTint.Value);

            var character = go.GetComponent<Character>();
            float speed = s_speedFactor.Value;
            character.m_health = 10f;
            character.m_runSpeed *= speed;
            character.m_speed = s_calmSpeed.Value;  // used while calm; fleeing uses m_runSpeed
            character.m_acceleration *= 1.5f;
            character.m_turnSpeed *= 1.6f;
            character.m_runTurnSpeed *= 1.8f;  // sharp cuts when fleeing

            var ai = EnsureAnimalAI(go);
            // Skittish: notices you early, bolts in zig-zags, settles back to grazing quickly.
            ai.m_viewRange = Mathf.Max(ai.m_viewRange, 25f);
            ai.m_hearRange = Mathf.Max(ai.m_hearRange, 20f);
            ai.m_fleeRange = 15f;
            ai.m_fleeAngle = 70f;
            ai.m_fleeInterval = 0.6f;
            // Calm: long pauses, short hops. (Far from home, RandomMovement runs; see SettleAfterFlee.)
            ai.m_randomMoveInterval = s_idleInterval.Value;
            ai.m_randomMoveRange = s_idleRange.Value;
            ai.m_afraidOfFire = true;
            ai.m_avoidWater = true;
            ai.m_timeToSafe = 6f;

            go.AddComponent<RabbitTag>();

            CreatureManager.Instance.AddCreature(rabbit);
            Plugin.Log.LogInfo("Registered " + CreaturePrefab + " (Hare clone): run " + character.m_runSpeed.ToString("F1") +
                               ", health " + character.m_health);
        }

        /// <summary>If the base prefab isn't an AnimalAI (it might be a passive MonsterAI), swap it, keeping BaseAI settings.</summary>
        private static AnimalAI EnsureAnimalAI(GameObject go)
        {
            var existing = go.GetComponent<BaseAI>();
            if (existing is AnimalAI animal)
                return animal;

            var replacement = go.AddComponent<AnimalAI>();
            if (existing != null)
            {
                foreach (var f in typeof(BaseAI).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    f.SetValue(replacement, f.GetValue(existing));
                Plugin.Log.LogInfo("Replaced " + existing.GetType().Name + " with AnimalAI on " + go.name);
                Object.DestroyImmediate(existing);
            }
            return replacement;
        }

        // -------------------------------------------------------------- helpers

        private static void Tint(GameObject go, Color tint)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                    continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                        continue;
                    var m = new Material(mats[i]) { name = mats[i].name + "_rabbit" };
                    if (m.HasProperty("_Color"))
                        m.SetColor("_Color", m.GetColor("_Color") * tint);
                    if (m.HasProperty("_EmissionColor"))
                        m.SetColor("_EmissionColor", Color.black);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }

        private static void Dump(string prefabName)
        {
            var go = PrefabManager.Cache.GetPrefab<GameObject>(prefabName);
            if (go == null)
            {
                Plugin.Log.LogWarning("Dump: prefab " + prefabName + " not found");
                return;
            }
            var sb = new StringBuilder("Dump " + prefabName + ": scale " + go.transform.localScale + "\n  components: ");
            sb.Append(string.Join(", ", go.GetComponents<Component>().Select(c => c.GetType().Name)));
            var ch = go.GetComponent<Character>();
            if (ch != null)
                sb.Append("\n  character: health " + ch.m_health + ", faction " + ch.m_faction + ", walk " + ch.m_walkSpeed +
                          ", speed " + ch.m_speed + ", run " + ch.m_runSpeed + ", turn " + ch.m_turnSpeed + "/" + ch.m_runTurnSpeed +
                          ", accel " + ch.m_acceleration);
            var ai = go.GetComponent<BaseAI>();
            if (ai != null)
                sb.Append("\n  ai: " + ai.GetType().Name + ", view " + ai.m_viewRange + ", hear " + ai.m_hearRange + ", flee " +
                          ai.m_fleeRange + "/" + ai.m_fleeAngle + "/" + ai.m_fleeInterval + ", randomMove " + ai.m_randomMoveInterval +
                          "/" + ai.m_randomMoveRange);
            var drop = go.GetComponent<CharacterDrop>();
            if (drop != null)
                sb.Append("\n  drops: " + string.Join(", ", drop.m_drops.Select(d => (d.m_prefab ? d.m_prefab.name : "null") +
                          " " + d.m_amountMin + "-" + d.m_amountMax + " @" + d.m_chance)));
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
                foreach (var m in r.sharedMaterials.Where(m => m != null))
                    sb.Append("\n  renderer " + r.name + " (" + r.GetType().Name + "): " + m.name + " / " + m.shader.name +
                              (m.HasProperty("_Color") ? " color " + m.GetColor("_Color") : "") +
                              (m.HasProperty("_EmissionColor") ? " emission " + m.GetColor("_EmissionColor") : ""));
            var item = go.GetComponent<ItemDrop>();
            if (item != null)
            {
                var s = item.m_itemData.m_shared;
                sb.Append("\n  item: " + s.m_name + ", food " + s.m_food + "/" + s.m_foodStamina + "/" + s.m_foodBurnTime + "s regen " +
                          s.m_foodRegen + ", weight " + s.m_weight + ", stack " + s.m_maxStackSize);
            }
            Plugin.Log.LogInfo(sb.ToString());
        }
    }
}

namespace ThrowingAxe
{
    /// <summary>Marks rabbit instances for the patches below.</summary>
    public class RabbitTag : MonoBehaviour
    {
    }

    /// <summary>
    /// BaseAI.RandomMovement makes an animal RUN back when it is more than 2 x m_randomMoveRange from its
    /// spawn point. After a flee that would send a calmed rabbit sprinting home; instead it settles where it
    /// stopped: its home becomes the current position when it stops being alerted.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(AnimalAI), "SetAlerted")]
    internal static class SettleAfterFlee
    {
        private static readonly HarmonyLib.AccessTools.FieldRef<BaseAI, Vector3> s_spawnPoint =
            HarmonyLib.AccessTools.FieldRefAccess<BaseAI, Vector3>("m_spawnPoint");
        private static readonly HarmonyLib.AccessTools.FieldRef<BaseAI, ZNetView> s_nview =
            HarmonyLib.AccessTools.FieldRefAccess<BaseAI, ZNetView>("m_nview");

        private static void Postfix(AnimalAI __instance, bool alert)
        {
            if (alert || __instance.GetComponent<RabbitTag>() == null)
                return;
            Vector3 here = __instance.transform.position;
            s_spawnPoint(__instance) = here;
            var nview = s_nview(__instance);
            if (nview != null && nview.IsValid() && nview.IsOwner())
                nview.GetZDO().Set(ZDOVars.s_spawnPoint, here);
        }
    }
}
