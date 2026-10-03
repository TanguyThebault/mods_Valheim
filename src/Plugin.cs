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
        internal static ConfigEntry<float> MaxRange;
        internal static ConfigEntry<float> OutSpeed;
        internal static ConfigEntry<float> ReturnSpeed;
        internal static ConfigEntry<float> SpinSpeed;
        internal static ConfigEntry<float> HitRadius;

        private GameObject _projectileTemplate;

        private void Awake()
        {
            Log = Logger;
            MaxRange = Config.Bind("Throw", "MaxRange", 20f, "Distance (m) before the axe turns back.");
            OutSpeed = Config.Bind("Throw", "OutSpeed", 30f, "Outbound speed (m/s).");
            ReturnSpeed = Config.Bind("Throw", "ReturnSpeed", 32f, "Return speed (m/s).");
            SpinSpeed = Config.Bind("Throw", "SpinSpeed", 1080f, "Spin (degrees/s).");
            HitRadius = Config.Bind("Throw", "HitRadius", 0.4f, "Radius of the sweep that detects hits (m).");

            AddLocalization();
            PrefabManager.OnVanillaPrefabsAvailable += CreateItem;
            new Harmony(Guid).PatchAll();
            Log.LogInfo("Throwing Axe " + Version + " loaded");
        }

        private void AddLocalization()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
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
                CraftingStation = "forge",
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
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class BlockAttackWhileThrown
    {
        // The axe is not in the hand while it flies: no melee, no second throw.
        private static bool Prefix(Humanoid __instance, ref bool __result)
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
}
