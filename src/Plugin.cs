using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace ThrowingAxe
{
    [BepInPlugin(Guid, "Throwing Axe", Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "lekinox.throwingaxe";
        public const string Version = "0.1.0";
        public const string ItemPrefab = "AxeThrowing";
        public const string ItemToken = "$item_axethrowing";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static Plugin Instance;
        internal static ConfigEntry<float> MaxRange;
        internal static ConfigEntry<float> OutSpeed;
        internal static ConfigEntry<float> ReturnSpeed;
        internal static ConfigEntry<float> SpinSpeed;
        internal static ConfigEntry<float> HitRadius;
        internal static ConfigEntry<float> CurveWidth;
        internal static ConfigEntry<float> CurveSide;
        internal static ConfigEntry<Vector3> VisualTilt;
        internal static ConfigEntry<bool> Craftable;
        internal static ConfigEntry<float> CryptChestChance;
        internal static ConfigEntry<bool> PerfLog;

        private System.DateTime _configStamp;
        private float _nextConfigCheck;

        private GameObject _projectileTemplate;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            MaxRange = Config.Bind("Throw", "MaxRange", 20f, "Distance (m) before the axe turns back.");
            OutSpeed = Config.Bind("Throw", "OutSpeed", 30f, "Outbound speed (m/s).");
            ReturnSpeed = Config.Bind("Throw", "ReturnSpeed", 32f, "Return speed (m/s).");
            SpinSpeed = Config.Bind("Throw", "SpinSpeed", 1080f, "Spin (degrees/s).");
            HitRadius = Config.Bind("Throw", "HitRadius", 0.4f, "Radius of the sweep that detects hits (m).");
            CurveWidth = Config.Bind("Throw", "CurveWidth", 0.3f,
                "Half-width of the elliptical path as a fraction of its length (0 = straight out and back).");
            CurveSide = Config.Bind("Throw", "CurveSide", 1f, "1 = goes out on the right and comes back on the left, -1 = the opposite.");
            Craftable = Config.Bind("Loot", "CraftableAtForge", false,
                "Let the axe be crafted at the forge (restart). Off: it only comes from Sunken Crypt chests. Repair at the forge works either way.");
            CryptChestChance = Config.Bind("Loot", "SunkenCryptChestChance", 0.02f,
                "Chance (0-1) that a Sunken Crypt chest holds the axe, rolled once when the chest is first filled.");
            VisualTilt = Config.Bind("Visual", "Tilt", Vector3.zero,
                "Extra rotation (degrees) applied to the flat-lying axe model, if it doesn't look right.");
            _configStamp = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);

            PerfLog = Config.Bind("Debug", "PerfLog", true, "Log frame rate and this mod's hook timings every 10 s (live).");
            Look.PluginDir = System.IO.Path.GetDirectoryName(Info.Location);
            Rabbits.BindConfig(Config);
            Birds.BindConfig(Config);
            Foxes.BindConfig(Config);
            Mice.BindConfig(Config);
            AddLocalization();
            PrefabManager.OnVanillaPrefabsAvailable += CreateItem;
            new Harmony(Guid).PatchAll();
            CommandManager.Instance.AddConsoleCommand(new ShowCommand());
            CommandManager.Instance.AddConsoleCommand(new ClearCommand());
            CommandManager.Instance.AddConsoleCommand(new LabCommand());
            CommandManager.Instance.AddConsoleCommand(new OverlayCommand());
            Log.LogInfo("Throwing Axe " + Version + " loaded");
        }

        // Edits to the .cfg apply while the game runs (checked once a second).
        private void Update()
        {
            Perf.Frame();
            long t = Perf.Begin();
            try { UpdateImpl(); }
            finally { Perf.End("Plugin.Update (config check)", t); }
        }

        private void UpdateImpl()
        {
            if (Time.unscaledTime < _nextConfigCheck)
                return;
            _nextConfigCheck = Time.unscaledTime + 1f;
            var stamp = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
            if (stamp == _configStamp)
                return;
            _configStamp = stamp;
            Config.Reload();
            Log.LogInfo("Config reloaded");
        }

        private float _nextLabPoll;

        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextLabPoll || Look.PluginDir == null)
                return;
            _nextLabPoll = Time.unscaledTime + 1f;
            Lab.Poll();
        }

        private void AddLocalization()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
            Rabbits.AddTranslations(loc);
            Birds.AddTranslations(loc);
            Foxes.AddTranslations(loc);
            Mice.AddTranslations(loc);
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "item_axethrowing", "Returning Axe" },
                { "item_axethrowing_desc", "A one-handed axe bound by a rune. Throw it (secondary attack): it spins up to 20 m, cuts through everything in its path and flies back to your hand." },
            });
            loc.AddTranslation("French", new Dictionary<string, string>
            {
                { "item_axethrowing", "Hache de retour" },
                { "item_axethrowing_desc", "Une hache à une main liée par une rune. Lance-la (attaque secondaire) : elle tournoie jusqu'à 20 m, tranche tout sur son passage et revient dans ta main." },
            });
        }

        private void CreateItem()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= CreateItem;

            // Inactive template: Instantiate() gives an active copy, the template itself never updates.
            var container = new GameObject("ThrowingAxe_templates");
            container.SetActive(false);
            DontDestroyOnLoad(container);
            _projectileTemplate = new GameObject("AxeThrowing_projectile");
            _projectileTemplate.transform.SetParent(container.transform, false);
            _projectileTemplate.AddComponent<ThrowingAxeProjectile>();

            var item = new CustomItem(ItemPrefab, "AxeIron", new ItemConfig
            {
                Name = ItemToken,
                Description = "$item_axethrowing_desc",
                // A disabled recipe still counts for repair: ObjectDB.GetRecipe ignores m_enabled.
                Enabled = Craftable.Value,
                CraftingStation = "forge",
                RepairStation = "forge",
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig("Iron", 20, 10, true),
                    new RequirementConfig("ElderBark", 6, 3, true),
                    new RequirementConfig("LeatherScraps", 4, 2, true),
                },
            });

            // The throw reuses the spear's secondary attack (animation, stamina, timing),
            // but keeps the item and fires our returning projectile.
            var spear = PrefabManager.Cache.GetPrefab<ItemDrop>("SpearBronze");
            var shared = item.ItemDrop.m_itemData.m_shared;
            var throwAttack = spear.m_itemData.m_shared.m_secondaryAttack.Clone();
            throwAttack.m_attackType = Attack.AttackType.Projectile;
            throwAttack.m_consumeItem = false;
            throwAttack.m_attackProjectile = _projectileTemplate;
            throwAttack.m_projectileAccuracy = 0f;
            throwAttack.m_projectileAccuracyMin = 0f;
            throwAttack.m_damageMultiplier = 1f;
            shared.m_secondaryAttack = throwAttack;

            ItemManager.Instance.AddItem(item);
            Log.LogInfo("Registered " + ItemPrefab + " (clone of AxeIron, throw from SpearBronze, anim '"
                        + throwAttack.m_attackAnimation + "')");

            try
            {
                if (PrefabManager.Cache.GetPrefab<Container>(SunkenCryptLoot.ChestPrefab) == null)
                    Log.LogWarning("Chest prefab " + SunkenCryptLoot.ChestPrefab + " not found: the axe won't appear as loot");
                Rabbits.Register();
            }
            catch (System.Exception e)
            {
                Log.LogError("Rabbits failed to register: " + e);
            }

            try
            {
                Foxes.Register();
            }
            catch (System.Exception e)
            {
                Log.LogError("Foxes failed to register: " + e);
            }

            try
            {
                Mice.Register();
            }
            catch (System.Exception e)
            {
                Log.LogError("Mice failed to register: " + e);
            }

            try
            {
                Birds.Register();
            }
            catch (System.Exception e)
            {
                Log.LogError("Birds failed to register: " + e);
            }
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class BlockAttackWhileThrown
    {
        // The axe is not in the hand while it flies: no melee, no second throw.
        private static bool Prefix(Humanoid __instance, ref bool __result)
        {
            long t = Perf.Begin();
            try { return PrefixImpl(__instance, ref __result); }
            finally { Perf.End("BlockAttackWhileThrown", t); }
        }

        private static bool PrefixImpl(Humanoid __instance, ref bool __result)
        {
            if (!ThrowingAxeProjectile.IsInFlight(__instance))
                return true;
            var weapon = __instance.GetCurrentWeapon();
            if (weapon == null || weapon.m_shared.m_name != Plugin.ItemToken)
                return true;
            __result = false;
            return false;
        }
    }

    /// <summary>
    /// Very rare loot: Sunken Crypt chests (Swamp, iron tier, like the axe's damage) roll once, when they are
    /// first filled, for a chance to hold the axe. Dungeon chests fill on the owner only, once per chest.
    /// </summary>
    [HarmonyPatch(typeof(Container), "AddDefaultItems")]
    internal static class SunkenCryptLoot
    {
        public const string ChestPrefab = "TreasureChest_sunkencrypt";

        private static void Postfix(Container __instance)
        {
            long t = Perf.Begin();
            try { PostfixImpl(__instance); }
            finally { Perf.End("SunkenCryptLoot", t); }
        }

        private static void PostfixImpl(Container __instance)
        {
            if (__instance.gameObject.name.Replace("(Clone)", "").Trim() != ChestPrefab)
                return;
            if (Random.value >= Plugin.CryptChestChance.Value)
                return;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(Plugin.ItemPrefab) : null;
            if (prefab == null)
                return;
            var item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = 1;
            item.m_durability = item.GetMaxDurability();
            __instance.GetInventory().AddItem(item);
            Plugin.Log.LogInfo("Throwing axe placed in a Sunken Crypt chest at " + __instance.transform.position);
        }
    }
}
