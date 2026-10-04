using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// Legendary weapons: rare, unique weapons with their own mechanics, found as loot rather than crafted.
    /// - Returning Axe: thrown with the secondary attack, flies an elliptical path up to 20 m, cuts through
    ///   creatures, comes back to the hand. Very rare in Sunken Crypt chests; repaired at the forge.
    /// - Thunder Spear: call the lightning (hold the secondary attack), throw the spear into a target before the bolt falls (4 s): it
    ///   strikes the spear and the area around it; the spear then lies there, to be picked up. Keep it in
    ///   hand and the bolt strikes you. Very rare in
    ///   Mountain frost cave chests; repaired at the forge.
    /// </summary>
    [BepInPlugin(Guid, "Legendary Weapons", Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "lekinox.legendaryweapons";
        public const string Version = "0.2.2";
        public const string ItemPrefab = "AxeThrowing";     // unchanged: axes already in inventories keep working
        public const string ItemToken = "$item_axethrowing";
        public const string SpearPrefab = "SpearThunder";
        public const string SpearToken = "$item_spearthunder";

        internal static BepInEx.Logging.ManualLogSource Log;
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

        internal static ConfigEntry<float> CallHoldTime;
        internal static ConfigEntry<float> CallDelay;
        internal static ConfigEntry<float> StrikeDamage;
        internal static ConfigEntry<float> StrikeDamagePerLevel;
        internal static ConfigEntry<float> StrikeRadius;
        internal static ConfigEntry<float> StrikePush;
        internal static ConfigEntry<float> BoltHeight;
        internal static ConfigEntry<float> SpearGravity;
        internal static ConfigEntry<bool> SpearFlipVisual;
        internal static ConfigEntry<Vector3> SpearVisualTilt;
        internal static ConfigEntry<bool> SpearCraftable;
        internal static ConfigEntry<string> SpearChest;
        internal static ConfigEntry<float> SpearChestChance;

        private System.DateTime _configStamp;
        private float _nextConfigCheck;
        private GameObject _projectileTemplate;

        private void Awake()
        {
            Log = Logger;
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

            CallHoldTime = Config.Bind("ThunderSpear", "CallHoldTime", 1f,
                "Seconds to hold the secondary attack to call the lightning; a shorter press throws the spear.");
            CallDelay = Config.Bind("ThunderSpear", "CallDelay", 4f, "Seconds between the call and the bolt.");
            StrikeDamage = Config.Bind("ThunderSpear", "StrikeDamage", 70f, "Lightning damage of the bolt.");
            StrikeDamagePerLevel = Config.Bind("ThunderSpear", "StrikeDamagePerLevel", 15f,
                "Extra lightning damage per upgrade level of the spear.");
            StrikeRadius = Config.Bind("ThunderSpear", "StrikeRadius", 6f, "Radius (m) of the area the bolt hurts.");
            StrikePush = Config.Bind("ThunderSpear", "StrikePush", 30f, "Knockback of the bolt.");
            BoltHeight = Config.Bind("ThunderSpear", "BoltHeight", 70f, "Height (m) the bolt falls from.");
            SpearGravity = Config.Bind("ThunderSpear", "Gravity", 6f, "Gravity on the thrown spear (m/s2).");
            SpearCraftable = Config.Bind("Loot", "SpearCraftableAtForge", false,
                "Let the Thunder Spear be crafted at the forge (restart). Off: loot only. Repair at the forge works either way.");
            SpearChest = Config.Bind("Loot", "SpearChest", "TreasureChest_mountaincave",
                "Chest prefab that can hold the Thunder Spear.");
            SpearChestChance = Config.Bind("Loot", "SpearChestChance", 0.02f,
                "Chance (0-1) that such a chest holds the spear, rolled once when it is first filled.");
            SpearFlipVisual = Config.Bind("Visual", "SpearFlip", false,
                "Flip the thrown spear model end for end, if it flies butt first.");
            SpearVisualTilt = Config.Bind("Visual", "SpearTilt", Vector3.zero,
                "Extra rotation (degrees) applied to the thrown spear model.");
            _configStamp = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);

            AddLocalization();
            PrefabManager.OnVanillaPrefabsAvailable += CreateItem;
            new Harmony(Guid).PatchAll();
            Log.LogInfo("Legendary Weapons " + Version + " loaded");
        }

        // Edits to the .cfg apply while the game runs (checked once a second).
        private void Update()
        {
            if (Player.m_localPlayer != null)
                LightningCall.RegisterRpc();
            LightningCall.Tick();

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

        private void AddLocalization()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "item_axethrowing", "Returning Axe" },
                { "item_axethrowing_desc", "A one-handed axe bound by a rune. Throw it (secondary attack): it spins up to 20 m, cuts through everything in its path and flies back to your hand." },
                { "item_spearthunder", "Thunder Spear" },
                { "item_spearthunder_desc", "A spear that draws the storm. Hold the secondary attack to call the lightning, then throw the spear (secondary attack) into your foe before the bolt falls: it strikes the spear and everything around it. Keep it in your hand, and the lightning finds you." },
                { "msg_thunderspear_called", "The storm answers... throw the spear!" },
                { "msg_thunderspear_self", "The lightning found the spear in your hand" },
            });
            loc.AddTranslation("French", new Dictionary<string, string>
            {
                { "item_axethrowing", "Hache de retour" },
                { "item_axethrowing_desc", "Une hache à une main liée par une rune. Lance-la (attaque secondaire) : elle tournoie jusqu'à 20 m, tranche tout sur son passage et revient dans ta main." },
                { "item_spearthunder", "Lance du tonnerre" },
                { "item_spearthunder_desc", "Une lance qui attire l'orage. Maintiens l'attaque secondaire pour appeler la foudre, puis lance la lance (attaque secondaire) sur ta cible avant que l'éclair ne tombe : il frappe la lance et tout ce qui l'entoure. Garde-la en main, et la foudre te trouve." },
                { "msg_thunderspear_called", "L'orage répond... lance la lance !" },
                { "msg_thunderspear_self", "La foudre a trouvé la lance dans ta main" },
            });
        }

        private void CreateItem()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= CreateItem;

            // Inactive template: Instantiate() gives an active copy, the template itself never updates.
            var container = new GameObject("LegendaryWeapons_templates");
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
            if (PrefabManager.Cache.GetPrefab<Container>(ChestLoot.CryptChest) == null)
                Log.LogWarning("Chest prefab " + ChestLoot.CryptChest + " not found: the axe won't appear as loot");
            Log.LogInfo("Registered " + ItemPrefab + " (clone of AxeIron, throw from SpearBronze, anim '" + throwAttack.m_attackAnimation + "')");

            CreateSpear(container);
        }

        private void CreateSpear(GameObject container)
        {
            string basePrefab = null;
            foreach (var n in new[] { "SpearWolfFang", "SpearElderbark", "SpearBronze" })
                if (PrefabManager.Cache.GetPrefab<ItemDrop>(n) != null) { basePrefab = n; break; }
            if (basePrefab == null)
            {
                Log.LogError("No vanilla spear found: no Thunder Spear");
                return;
            }

            var template = new GameObject("SpearThunder_projectile");
            template.transform.SetParent(container.transform, false);
            template.AddComponent<ThunderSpearProjectile>();

            var item = new CustomItem(SpearPrefab, basePrefab, new ItemConfig
            {
                Name = SpearToken,
                Description = "$item_spearthunder_desc",
                Enabled = SpearCraftable.Value,
                CraftingStation = "forge",
                RepairStation = "forge",
                MinStationLevel = 2,
                Requirements = new[]
                {
                    new RequirementConfig("Silver", 12, 6, true),
                    new RequirementConfig("WolfFang", 6, 3, true),
                    new RequirementConfig("Crystal", 4, 2, true),
                },
            });

            // Same throw as the base spear (animation, stamina, speed, item taken from the inventory), but our
            // projectile flies instead of the vanilla one and drops the spear where it ends up.
            var shared = item.ItemDrop.m_itemData.m_shared;
            var throwAttack = shared.m_secondaryAttack.Clone();
            var vanillaProjectile = throwAttack.m_attackProjectile != null
                ? throwAttack.m_attackProjectile.GetComponent<Projectile>() : null;
            throwAttack.m_attackType = Attack.AttackType.Projectile;
            throwAttack.m_consumeItem = true;
            throwAttack.m_attackProjectile = template;
            throwAttack.m_projectileAccuracy = 0f;
            throwAttack.m_projectileAccuracyMin = 0f;
            shared.m_secondaryAttack = throwAttack;

            Sfx.Create(throwAttack.m_triggerEffect, throwAttack.m_startEffect,
                vanillaProjectile != null ? vanillaProjectile.m_hitEffects : null, shared.m_hitEffect);

            ItemManager.Instance.AddItem(item);
            if (PrefabManager.Cache.GetPrefab<Container>(SpearChest.Value) == null)
                Log.LogWarning("Chest prefab " + SpearChest.Value + " not found: the Thunder Spear won't appear as loot");
            Log.LogInfo("Registered " + SpearPrefab + " (clone of " + basePrefab + ", throw anim '" +
                        throwAttack.m_attackAnimation + "', " + throwAttack.m_projectileVel + " m/s)");
        }
    }

    /// <summary>
    /// Thunder Spear controls, on the secondary attack: holding it for CallHoldTime calls the lightning, a
    /// shorter press throws the spear when released. Once the lightning is called, the secondary attack throws
    /// at once (after the button has been let go). Other weapons are untouched.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class SpearControls
    {
        private static float s_holdStart = -1f;
        private static bool s_called;

        private static void Prefix(Player __instance, ref bool secondaryAttack, ref bool secondaryAttackHold)
        {
            if (__instance != Player.m_localPlayer)
                return;
            var weapon = __instance.GetCurrentWeapon();
            bool spear = weapon != null && weapon.m_shared.m_name == Plugin.SpearToken;
            bool held = secondaryAttack || secondaryAttackHold;

            if (s_holdStart >= 0f)
            {
                if (held && spear)
                {
                    if (!s_called && Time.time - s_holdStart >= Plugin.CallHoldTime.Value)
                    {
                        s_called = true;
                        LightningCall.TryStart(__instance);
                    }
                    secondaryAttack = secondaryAttackHold = false;
                    return;
                }
                // Released: a short press throws now; after a call, the next press throws.
                bool throwNow = spear && !s_called;
                s_holdStart = -1f;
                s_called = false;
                secondaryAttack = throwNow;
                secondaryAttackHold = false;
                return;
            }
            if (!spear || LightningCall.IsPending(__instance) || !secondaryAttack)
                return;
            s_holdStart = Time.time;
            s_called = false;
            secondaryAttack = secondaryAttackHold = false;
        }
    }

    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class BlockAttackWhileThrown
    {
        // The axe is not in the hand while it flies: no melee, no second throw.
        private static bool Prefix(Humanoid __instance, ref bool __result)
        {
            var weapon = __instance.GetCurrentWeapon();
            if (weapon == null)
                return true;
            if (weapon.m_shared.m_name != Plugin.ItemToken || !ThrowingAxeProjectile.IsInFlight(__instance))
                return true;
            __result = false;
            return false;
        }
    }

    /// <summary>
    /// Very rare loot: a chest rolls once, when it is first filled, for a chance to hold a legendary weapon.
    /// - Sunken Crypt chests (Swamp, iron tier, like the axe's damage): the Returning Axe.
    /// - Mountain frost cave chests (silver tier, like the fang spear): the Thunder Spear.
    /// Dungeon chests fill on the owner only, once per chest.
    /// </summary>
    [HarmonyPatch(typeof(Container), "AddDefaultItems")]
    internal static class ChestLoot
    {
        public const string CryptChest = "TreasureChest_sunkencrypt";

        private static void Postfix(Container __instance)
        {
            string chest = __instance.gameObject.name.Replace("(Clone)", "").Trim();
            if (chest == CryptChest)
                Roll(__instance, Plugin.ItemPrefab, Plugin.CryptChestChance.Value);
            if (chest == Plugin.SpearChest.Value)
                Roll(__instance, Plugin.SpearPrefab, Plugin.SpearChestChance.Value);
        }

        private static void Roll(Container container, string itemPrefab, float chance)
        {
            if (Random.value >= chance)
                return;
            var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(itemPrefab) : null;
            if (prefab == null)
                return;
            var item = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
            item.m_dropPrefab = prefab;
            item.m_stack = 1;
            item.m_durability = item.GetMaxDurability();
            container.GetInventory().AddItem(item);
            Plugin.Log.LogInfo(itemPrefab + " placed in " + container.gameObject.name + " at " + container.transform.position);
        }
    }
}
