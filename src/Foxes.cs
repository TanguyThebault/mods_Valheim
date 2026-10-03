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
    /// Meadow foxes: their own creature built on the Wolf's skeleton and animations, with their own painted
    /// coat (red, white chest and tail tip, black socks), own ragdoll and own voice (yelps, no howls). They
    /// hunt rabbits with a single bite per attack, keep away from players unless hit, and drop fox meat and
    /// fox pelt. The pelt makes a fox cape.
    /// </summary>
    internal static class Foxes
    {
        public const string CreaturePrefab = "MeadowFox";
        public const string MeatPrefab = "MeadowFoxMeat";
        public const string CookedPrefab = "MeadowFoxCooked";
        public const string PeltPrefab = "MeadowFoxPelt";
        public const string CapePrefab = "MeadowFoxCape";

        internal static readonly Color FoxRed = new Color(1.0f, 0.55f, 0.25f, 1f);

        private static ConfigEntry<float> s_scale;
        private static ConfigEntry<float> s_biteDamage;
        private static ConfigEntry<float> s_spawnChance;
        internal static ConfigEntry<float> KeepAwayDistance;

        public static void BindConfig(ConfigFile config)
        {
            s_scale = config.Bind("Fox", "Scale", 0.6f, "Size relative to the vanilla wolf (restart).");
            s_biteDamage = config.Bind("Fox", "BiteDamage", 8f, "Total damage of a fox bite (restart).");
            s_spawnChance = config.Bind("Fox", "SpawnChance", 20f, "Spawn chance per spawn check, % (restart).");
            KeepAwayDistance = config.Bind("Fox", "KeepAwayDistance", 10f, "Foxes back off from players closer than this, m (live).");
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
                { "item_foxcape", "Fox cape" },
                { "item_foxcape_desc", "A red fox-fur cape. Light on the shoulders and quick in a brawl." },
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
                { "item_foxcape", "Cape de renard" },
                { "item_foxcape_desc", "Une cape en fourrure de renard. Légère sur les épaules et vive au corps à corps." },
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
            Rabbits.DumpPrefab("CapeWolf");

            RegisterItems();
            RegisterCreature();
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
            var mask = Look.Mask("fox");
            Look.Paint(go, mask, FoxRed);

            // Not a pet, not a breeder.
            foreach (var c in go.GetComponents<Tameable>()) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<Procreation>()) Object.DestroyImmediate(c);

            var character = go.GetComponent<Character>();
            character.m_name = config.Name;   // shown above the health bar
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
                ai.m_afraidOfFire = true;
            }

            Look.OwnRagdolls(character.m_deathEffects, CreaturePrefab, mask, FoxRed);

            var humanoid = go.GetComponent<Humanoid>();
            if (humanoid != null)
                humanoid.m_defaultItems = SingleBite(humanoid.m_defaultItems);

            GiveVoice(character, ai, humanoid);

            go.AddComponent<FoxTag>();
            CreatureManager.Instance.AddCreature(fox);
            Plugin.Log.LogInfo("Registered " + CreaturePrefab + " (Wolf clone), AI " + (ai != null ? "MonsterAI" : "none") +
                               ", attacks " + (humanoid?.m_defaultItems?.Length ?? 0));
        }

        /// <summary>
        /// One plain bite: the wolf has several attacks and chains bites into combos. Keep a single attack item
        /// (the first that doesn't chain, else the first), with no chain and a pause between attacks.
        /// </summary>
        private static GameObject[] SingleBite(GameObject[] items)
        {
            if (items == null || items.Length == 0)
                return items;
            var attacks = items.Where(i => i != null && i.GetComponent<ItemDrop>() != null).ToList();
            foreach (var a in attacks)
            {
                var sh = a.GetComponent<ItemDrop>().m_itemData.m_shared;
                Plugin.Log.LogInfo("Wolf attack " + a.name + ": chain " + sh.m_attack.m_attackChainLevels +
                                   ", interval " + sh.m_aiAttackInterval + ", anim " + sh.m_attack.m_attackAnimation);
            }
            var pick = attacks.FirstOrDefault(a => a.GetComponent<ItemDrop>().m_itemData.m_shared.m_attack.m_attackChainLevels <= 1)
                       ?? attacks.FirstOrDefault();
            if (pick == null)
                return items;
            var bite = CloneWeakAttack(pick);
            var shared = bite.GetComponent<ItemDrop>().m_itemData.m_shared;
            shared.m_attack.m_attackChainLevels = 1;
            if (shared.m_secondaryAttack != null)
                shared.m_secondaryAttack.m_attackChainLevels = 1;
            shared.m_aiAttackInterval = 2.5f;
            return new[] { bite };
        }

        /// <summary>Yelps instead of the wolf's howls and growls: idle and alert calls, hurt, death, attack.</summary>
        private static void GiveVoice(Character character, MonsterAI ai, Humanoid humanoid)
        {
            var template = Look.FindSfxTemplate(character.m_hitEffects, character.m_deathEffects, ai?.m_alertedEffects, ai?.m_idleSound);
            if (template == null)
                Plugin.Log.LogWarning("Fox voice: no vanilla sound to clone, wolf sounds removed");
            var calls = Look.MakeSfx(CreaturePrefab + "_sfx_call", template, Look.LoadClips("sfx_fox", "fox_call"));
            var hurt = Look.MakeSfx(CreaturePrefab + "_sfx_hurt", template, Look.LoadClips("sfx_fox", "fox_hurt"), 0.95f, 1.12f);
            var death = Look.MakeSfx(CreaturePrefab + "_sfx_death", template, Look.LoadClips("sfx_fox", "fox_death"));
            var attack = Look.MakeSfx(CreaturePrefab + "_sfx_attack", template, Look.LoadClips("sfx_fox", "fox_attack"));

            character.m_hitEffects = Look.Voice(character.m_hitEffects, hurt);
            character.m_critHitEffects = Look.Voice(character.m_critHitEffects, hurt);
            character.m_backstabHitEffects = Look.Voice(character.m_backstabHitEffects, hurt);
            character.m_deathEffects = Look.Voice(character.m_deathEffects, death);
            if (ai != null)
            {
                ai.m_alertedEffects = Look.Voice(ai.m_alertedEffects, calls);
                ai.m_idleSound = Look.Voice(new EffectList(), calls);
                ai.m_idleSoundInterval = 25f;
                ai.m_idleSoundChance = 0.3f;
            }
            if (humanoid?.m_defaultItems != null)
                foreach (var item in humanoid.m_defaultItems)
                {
                    var shared = item?.GetComponent<ItemDrop>()?.m_itemData.m_shared;
                    if (shared == null)
                        continue;
                    shared.m_startEffect = Look.Voice(shared.m_startEffect, null);
                    shared.m_triggerEffect = Look.Voice(shared.m_triggerEffect, null);
                    shared.m_attack.m_startEffect = Look.Voice(shared.m_attack.m_startEffect, attack);
                    shared.m_attack.m_triggerEffect = Look.Voice(shared.m_attack.m_triggerEffect, null);
                }
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

        /// <summary>
        /// Red fur, but the metal chain and clasp keep their colour (tinting metal orange made a white-hot
        /// highlight on the shoulder). Logs each material's shine settings for checking.
        /// </summary>
        private static void TintCape(GameObject go)
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
                    var m = new Material(mats[i]) { name = mats[i].name + "_fox" };
                    var info = new System.Text.StringBuilder("Cape material " + r.name + "/" + m.name + " (" + m.shader.name + "):");
                    for (int p = 0; p < m.shader.GetPropertyCount(); p++)
                    {
                        var type = m.shader.GetPropertyType(p);
                        string n = m.shader.GetPropertyName(p);
                        if (type == UnityEngine.Rendering.ShaderPropertyType.Float || type == UnityEngine.Rendering.ShaderPropertyType.Range)
                            info.Append(" " + n + "=" + m.GetFloat(n).ToString("F2"));
                    }
                    Plugin.Log.LogInfo(info.ToString());
                    bool metal = m.name.IndexOf("Chain", System.StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!metal && m.HasProperty("_Color"))
                        m.SetColor("_Color", m.GetColor("_Color") * FoxRed);
                    if (m.HasProperty("_EmissionColor"))
                        m.SetColor("_EmissionColor", Color.black);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
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
            TintCape(cape.ItemPrefab);
            var shared = cape.ItemDrop.m_itemData.m_shared;
            MakeCosmetic(shared);
            shared.m_armor = 1f;
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

    /// <summary>
    /// Who fights whom: a fox only goes after rabbits (and, for a while, whoever hit it); rabbits flee foxes.
    /// Others judging a fox keep the vanilla answer (players can hunt foxes).
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character), typeof(Character) })]
    internal static class FoxEnemies
    {
        private static void Postfix(Character a, Character b, ref bool __result)
        {
            long t = Perf.Begin();
            try { PostfixImpl(a, b, ref __result); }
            finally { Perf.End("FoxEnemies", t); }
        }

        private static void PostfixImpl(Character a, Character b, ref bool __result)
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
            long t = Perf.Begin();
            try { PrefixImpl(__instance, attacker); }
            finally { Perf.End("FoxProvoked", t); }
        }

        private static void PrefixImpl(MonsterAI __instance, Character attacker)
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
            long t = Perf.Begin();
            try { PostfixImpl(__instance, dt, __result); }
            finally { Perf.End("FoxKeepAway", t); }
        }

        private static void PostfixImpl(MonsterAI __instance, float dt, bool __result)
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
