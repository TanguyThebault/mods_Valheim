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
    /// - Wind Blade: a Plains sword whose secondary attack looses a blade of air that cuts everything 6 m ahead
    ///   and turns the wind the same way. Very rare in Plains stone tower chests; repaired at the forge.
    /// - Earthbreaker: a Meadows mace whose held secondary attack splits the ground ahead, throwing creatures up.
    ///   Very rare in Meadows chests; repaired at the workbench.
    /// - Fox Fang: a Black Forest knife whose held secondary attack camouflages you for an ambush. Very rare in
    ///   Black Forest chests, burial chambers and troll caves; repaired at the forge.
    /// - Skyfisher's Rod: an Ocean fishing rod (fished up) whose held secondary attack casts into the sky and drops
    ///   a giant fish that explodes where you aim.
    /// - Fog Horn: a Mistlands horn (found in dvergr chests). Blow it (primary attack); hold the secondary attack to
    ///   call three spirit animals that fight for you for 30 s, every 20 minutes.
    /// - Surtrbrand: an Ashlands greatsword (charred fortress chests). Hold the secondary attack to drive it into the
    ///   ground: a dome like Haldor's rises from it for 10 minutes, keeps creatures, projectiles and embers out, and
    ///   makes a camp (rest, no spawns, respawn at the sword). Take it back with Use.
    /// - Ymir's Bite: a Deep North atgeir (Deep North village chests). Hold the secondary attack: no cold for 10
    ///   minutes, an aura of frost shared with the players close by.
    /// Held-secondary powers share HoldPower. The sky powers need the open sky (OpenSky).
    /// </summary>
    [BepInPlugin(Guid, "Legendary Weapons", Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "lekinox.legendaryweapons";
        public const string Version = "0.13.5";
        public const string ItemPrefab = "AxeThrowing";     // unchanged: axes already in inventories keep working
        public const string ItemToken = "$item_axethrowing";
        public const string SpearPrefab = "SpearThunder";
        public const string SpearToken = "$item_spearthunder";
        public const string SwordPrefab = "SwordWind";
        public const string SwordToken = "$item_swordwind";
        public const string MacePrefab = "MaceEarth";
        public const string MaceToken = "$item_maceearth";
        public const string KnifePrefab = "KnifeFox";
        public const string KnifeToken = "$item_knifefox";
        public const string RodPrefab = "FishingRodSky";
        public const string RodToken = "$item_rodsky";
        public const string HornPrefab = "HornFog";
        public const string HornToken = "$item_hornfog";
        public const string SurtrPrefab = "SwordSurtr";
        public const string SurtrToken = "$item_swordsurtr";
        public const string YmirPrefab = "AtgeirYmir";
        public const string YmirToken = "$item_atgeirymir";
        internal static ConfigEntry<float> SurtrHoldTime, SurtrDuration, SurtrCooldown, SurtrRadius, SurtrSpawnBlock, SurtrPush;
        internal static ConfigEntry<float> SurtrPlantDistance, SurtrLean, SurtrSink, SurtrChestChance;
        internal static ConfigEntry<bool> SurtrRespawn;
        internal static ConfigEntry<Color> SurtrDomeTint;
        internal static ConfigEntry<string> SurtrChests;
        internal static ConfigEntry<float> YmirHoldTime, GiantDuration, GiantCooldown, GiantScale, GiantSmashDamage, YmirAuraVisual, YmirChestChance;
        internal static ConfigEntry<bool> GiantBreakBuildings;
        internal static ConfigEntry<float> GiantAnimSpeed;
        internal static ConfigEntry<string> YmirChests;
        internal static ConfigEntry<float> AuraStrength, DamageBonus, SurtrVeil;
        internal static ConfigEntry<bool> HideCooldownIcons, Upgrades;
        internal const float HornShortTime = 1.4f, HornCallTime = 4.2f;   // seconds the horn stays at the mouth (the notes in sfx/)
        internal static Plugin Instance;
        internal static ConfigEntry<float> HornHoldTime, HornDuration, HornCooldown, HornAlpha, HornLength, HornChestChance, HornAggro;
        internal static ConfigEntry<float> HornHoldMax, HornDamage, HornMouthpiece, HornMouthDrop, HornRoll;
        internal static ConfigEntry<int> HornCount, HornLevel;
        internal static ConfigEntry<string> HornAnimals, HornChests;
        internal static ConfigEntry<bool> LabEnabled;

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
        internal static ConfigEntry<float> ThrowHoldTime;

        internal static ConfigEntry<float> CallHoldTime;
        internal static ConfigEntry<float> CallDelay;
        internal static ConfigEntry<float> CallCooldown;
        internal static ConfigEntry<float> CastHoldTime, CastRange, CastRadius, CastDamage, CastEdgeDamage, CastCooldown, FishScale;
        internal static ConfigEntry<float> CatchChance, CatchPity, FallHeight, StrainStamina, FallTime, StormFade, ClearFade;
        internal static ConfigEntry<string> StormEnv;
        internal static ConfigEntry<float> VortexHeight;
        internal static ConfigEntry<bool> HitEverything;
        internal static ConfigEntry<bool> CustomModels;
        internal static ConfigEntry<string> FlipHead;
        internal static ConfigEntry<float> ModelLengthScale;
        internal static ConfigEntry<float> StrikeDamage;
        internal static ConfigEntry<float> StrikeDamagePerLevel;
        internal static ConfigEntry<float> StrikeRadius;
        internal static ConfigEntry<float> StrikePush;
        internal static ConfigEntry<float> StrikeEdgeDamage;
        internal static ConfigEntry<float> BoltHeight;
        internal static ConfigEntry<float> SpearGravity;
        internal static ConfigEntry<bool> SpearFlipVisual;
        internal static ConfigEntry<Vector3> SpearVisualTilt;
        internal static ConfigEntry<bool> SpearCraftable;
        internal static ConfigEntry<string> SpearChest;
        internal static ConfigEntry<float> SpearChestChance;

        internal static ConfigEntry<float> SlashRange;
        internal static ConfigEntry<float> ConeAngle;
        internal static ConfigEntry<float> SlashEdgeDamage;
        internal static ConfigEntry<float> HoldTime;
        internal static ConfigEntry<float> SlashDamage;
        internal static ConfigEntry<float> WindDuration;
        internal static ConfigEntry<float> WindTurnTime;
        internal static ConfigEntry<bool> SwordCraftable;
        internal static ConfigEntry<string> SwordChest;
        internal static ConfigEntry<float> SwordChestChance;

        internal static ConfigEntry<float> GustCooldown, QuakeCooldown;
        internal static ConfigEntry<float> QuakeStun;
        internal static ConfigEntry<float> QuakeHoldTime, QuakeLength, QuakeWidth, QuakeDamage, QuakeEdgeDamage, QuakeLaunch;
        internal static ConfigEntry<bool> MaceCraftable;
        internal static ConfigEntry<string> MaceChests;
        internal static ConfigEntry<float> MaceChestChance;
        internal static ConfigEntry<float> CamoHoldTime, CamoDuration, CamoCooldown, RevealDistance, AmbushMultiplier;
        internal static ConfigEntry<bool> KnifeCraftable;
        internal static ConfigEntry<string> KnifeChests;
        internal static ConfigEntry<float> KnifeChestChance;

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
            ThrowHoldTime = Config.Bind("Throw", "HoldTime", 0.5f,
                "Seconds to hold the secondary attack to throw the axe; a shorter press is the axe's own secondary attack.");
            VisualTilt = Config.Bind("Visual", "Tilt", Vector3.zero,
                "Extra rotation (degrees) applied to the flat-lying axe model, if it doesn't look right.");

            CallHoldTime = Config.Bind("ThunderSpear", "CallHoldTime", 1f,
                "Seconds to hold the secondary attack to call the lightning; a shorter press throws the spear.");
            CallDelay = Config.Bind("ThunderSpear", "CallDelay", 4f, "Seconds between the call and the bolt.");
            CallCooldown = Config.Bind("ThunderSpear", "CallCooldown", 30f,
                "Seconds after a strike before the storm can be called again (a hold then just throws on release).");
            StrikeDamage = Config.Bind("ThunderSpear", "StrikeDamage", 70f, "Lightning damage of the bolt.");
            StrikeDamagePerLevel = Config.Bind("ThunderSpear", "StrikeDamagePerLevel", 15f,
                "Extra lightning damage per upgrade level of the spear.");
            StrikeRadius = Config.Bind("ThunderSpear", "StrikeRadius", 6f, "Radius (m) of the area the bolt hurts.");
            StrikePush = Config.Bind("ThunderSpear", "StrikePush", 30f, "Knockback of the bolt.");
            StrikeEdgeDamage = Config.Bind("ThunderSpear", "StrikeEdgeDamage", 0.25f,
                "Share of the damage left at the edge of the area: full within 1 m of the impact, falling off linearly to this.");
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
            SlashRange = Config.Bind("WindBlade", "SlashRange", 16f, "Reach (m) of the gust: length of the cone.");
            ConeAngle = Config.Bind("WindBlade", "ConeAngle", 36f, "Opening (degrees) of the cone the gust fills.");
            SlashEdgeDamage = Config.Bind("WindBlade", "EdgeDamage", 0.3f, "Share of the damage left at the far end of the zone (full within 1.5 m of the swordsman).");
            HoldTime = Config.Bind("WindBlade", "HoldTime", 0.6f, "Seconds to hold the secondary attack before the gust is loosed.");
            SlashDamage = Config.Bind("WindBlade", "SlashDamage", 1.5f, "Damage of the gust near the swordsman, times the sword's damage.");
            WindDuration = Config.Bind("WindBlade", "WindDuration", 120f, "Seconds the wind keeps blowing the way of the slash.");
            GustCooldown = Config.Bind("WindBlade", "Cooldown", 30f, "Seconds between two gusts.");
            QuakeCooldown = Config.Bind("Earthbreaker", "Cooldown", 30f, "Seconds between two quakes.");
            WindTurnTime = Config.Bind("WindBlade", "WindTurnTime", 2f, "Seconds the wind takes to turn after a gust (the game uses 5). Its strength is untouched.");
            SwordCraftable = Config.Bind("Loot", "SwordCraftableAtForge", false,
                "Let the Wind Blade be crafted at the forge (restart). Off: loot only. Repair at the forge works either way.");
            SwordChest = Config.Bind("Loot", "SwordChest", "TreasureChest_plains_stone", "Chest prefab that can hold the Wind Blade.");
            SwordChestChance = Config.Bind("Loot", "SwordChestChance", 0.02f,
                "Chance (0-1) that such a chest holds the sword, rolled once when it is first filled.");
            QuakeHoldTime = Config.Bind("Earthbreaker", "HoldTime", 0.6f, "Seconds to hold the secondary attack before the mace slams.");
            QuakeLength = Config.Bind("Earthbreaker", "QuakeLength", 8f, "Length (m) of the crack.");
            QuakeWidth = Config.Bind("Earthbreaker", "QuakeWidth", 2f, "Width (m) of the band the crack hits.");
            QuakeDamage = Config.Bind("Earthbreaker", "QuakeDamage", 1.5f, "Damage near the mace, times the mace's damage.");
            QuakeEdgeDamage = Config.Bind("Earthbreaker", "QuakeEdgeDamage", 0.35f,
                "Share of the damage left at the far end of the crack (full within 2 m).");
            QuakeStun = Config.Bind("Earthbreaker", "Stun", 1.4f, "Seconds the stars circle a dazed creature's head (one stagger).");
            QuakeLaunch = Config.Bind("Earthbreaker", "Launch", 7f, "Upward speed (m/s) given to creatures near the mace; less further out and for heavy ones; never bosses.");
            MaceCraftable = Config.Bind("Loot", "MaceCraftableAtWorkbench", false,
                "Let the Earthbreaker be crafted at the workbench (restart). Off: loot only. Repair works either way.");
            MaceChests = Config.Bind("Loot", "MaceChests", "TreasureChest_meadows,TreasureChest_meadows_buried",
                "Chest prefabs (comma-separated) that can hold the Earthbreaker.");
            MaceChestChance = Config.Bind("Loot", "MaceChestChance", 0.01f, "Chance (0-1) per such chest, rolled once when it is first filled.");
            CamoHoldTime = Config.Bind("FoxFang", "HoldTime", 1f, "Seconds to hold the secondary attack to vanish; a shorter press is the knife's own lunge.");
            CamoDuration = Config.Bind("FoxFang", "CamoDuration", 8f, "Seconds of camouflage.");
            CamoCooldown = Config.Bind("FoxFang", "CamoCooldown", 60f, "Seconds after the camouflage ends before it can be used again.");
            RevealDistance = Config.Bind("FoxFang", "RevealDistance", 1.5f, "Creatures closer than this (m) still notice a camouflaged player.");
            AmbushMultiplier = Config.Bind("FoxFang", "AmbushMultiplier", 3f, "Damage multiplier of the first blow struck from camouflage.");
            KnifeCraftable = Config.Bind("Loot", "KnifeCraftableAtForge", false,
                "Let the Fox Fang be crafted at the forge (restart). Off: loot only. Repair at the forge works either way.");
            KnifeChests = Config.Bind("Loot", "KnifeChests", "TreasureChest_blackforest,TreasureChest_forestcrypt,TreasureChest_trollcave",
                "Chest prefabs (comma-separated) that can hold the Fox Fang.");
            KnifeChestChance = Config.Bind("Loot", "KnifeChestChance", 0.015f, "Chance (0-1) per such chest, rolled once when it is first filled.");
            CastHoldTime = Config.Bind("SkyRod", "HoldTime", 1.6f, "Seconds to hold the secondary attack to cast into the sky.");
            FallTime = Config.Bind("SkyRod", "FallTime", 4.2f, "Seconds the fish takes to fall from the portal to the ground.");
            StormFade = Config.Bind("SkyRod", "StormFade", 8f, "Seconds the storm takes to roll in.");
            ClearFade = Config.Bind("SkyRod", "ClearFade", 14f, "Seconds the sky takes to clear after the impact.");
            CastRange = Config.Bind("SkyRod", "Range", 70f, "How far (m) the fish can be aimed.");
            CastRadius = Config.Bind("SkyRod", "Radius", 25f, "Radius (m) of the explosion.");
            CastDamage = Config.Bind("SkyRod", "Damage", 600f, "Blunt damage at the centre (plus 35 % as fire); falls off to EdgeDamage at the edge.");
            CastEdgeDamage = Config.Bind("SkyRod", "EdgeDamage", 0.35f, "Share of the damage left at the edge of the explosion (full within 1.5 m).");
            CastCooldown = Config.Bind("SkyRod", "Cooldown", 1200f, "Seconds between two casts into the sky (20 minutes).");
            FishScale = Config.Bind("SkyRod", "FishScale", 24f, "Size of the falling fish, times a normal fish.");
            FallHeight = Config.Bind("SkyRod", "AboveVortex", 2f, "How far (m) above the storm vortex the fish appears (it drops out of the vortex).");
            VortexHeight = Config.Bind("SkyRod", "VortexHeight", 85f, "Height (m) of the storm vortex the fish falls out of.");
            StormEnv = Config.Bind("SkyRod", "StormEnvironment", "ThunderStorm", "Weather forced while the sky is fished (empty: none).");
            StrainStamina = Config.Bind("SkyRod", "StrainStamina", 40f, "Stamina spent straining against the sky.");
            HitEverything = Config.Bind("SkyRod", "HitEverything", true,
                "The explosion spares nothing: tames, other players and yourself (PvP or not), trees, rocks and buildings. Off: enemies and breakables only.");
            CatchChance = Config.Bind("Loot", "RodCatchChance", 0.005f, "Chance (0-1) that an Ocean catch brings up the Skyfisher's Rod.");
            CatchPity = Config.Bind("Loot", "RodCatchPity", 0.002f, "Added to that chance for every Ocean catch without it (reset when found).");
            HornHoldTime = Config.Bind("FogHorn", "HoldTime", 1f, "Seconds to hold the secondary attack for the call (a shorter press, or the primary attack, is a plain blow).");
            HornDuration = Config.Bind("FogHorn", "Duration", 30f, "Seconds the spirit animals stay.");
            HornCooldown = Config.Bind("FogHorn", "Cooldown", 1200f, "Seconds between two calls (20 minutes). Plain blows are always possible.");
            HornCount = Config.Bind("FogHorn", "Count", 3, "Spirit animals per call.");
            HornAnimals = Config.Bind("FogHorn", "Animals", "Boar,Neck,Bjorn,Wolf,Ulv,Lox,Hare,Deathsquito",
                "Vanilla creatures (prefab names, comma-separated) the spirits are picked from, at random: beasts of the Meadows up to the Mistlands, none from further.");
            HornLevel = Config.Bind("FogHorn", "Level", 3, "Level of the spirits (1 = no star, 2 = one star, 3 = two stars); their stars show pink.");
            HornAggro = Config.Bind("FogHorn", "HuntRange", 60f, "The spirits hunt down any enemy within this range (m) of themselves.");
            HornDamage = Config.Bind("FogHorn", "DamageMultiplier", 1.5f, "The spirits' damage, times a normal creature's of their level.");
            HornRoll = Config.Bind("FogHorn", "Roll", 180f, "Turn (degrees) of the horn around its own axis at the mouth (its curve up, down or sideways).");
            HornHoldMax = Config.Bind("FogHorn", "HoldMax", 5f, "Longest plain blow (s): the primary attack sounds the horn as long as it is held, up to this.");
            HornMouthpiece = Config.Bind("FogHorn", "MouthpieceOffset", 0.12f, "Distance (m) from the hand to the horn's mouthpiece, the point put on the lips (larger: the horn goes higher).");
            HornMouthDrop = Config.Bind("FogHorn", "MouthDrop", 0.035f, "How far (m) below the head bone the lips are (negative: above; the head bone sits at the skull's base).");
            HornAlpha = Config.Bind("FogHorn", "Opacity", 0.5f, "How opaque the spirits are (0-1).");
            HornLength = Config.Bind("FogHorn", "BellDistance", 0.45f, "Distance (m) from the hand to the horn's bell, where the mist pours out.");
            HornChests = Config.Bind("Loot", "HornChests", "TreasureChest_dvergrtower,TreasureChest_dvergrtown,TreasureChest_dvergr_loose_stone",
                "Chest prefabs (comma-separated) that can hold the Fog Horn.");
            HornChestChance = Config.Bind("Loot", "HornChestChance", 0.015f, "Chance (0-1) per such chest, rolled once when it is first filled.");
            SurtrHoldTime = Config.Bind("Surtrbrand", "HoldTime", 1f, "Seconds to hold the secondary attack to plant the sword (a shorter press is its own secondary attack).");
            SurtrDuration = Config.Bind("Surtrbrand", "Duration", 600f, "Seconds the dome stands (10 minutes). The sword stays planted afterwards, until taken back.");
            SurtrCooldown = Config.Bind("Surtrbrand", "Cooldown", 1800f, "Seconds between two plantings (30 minutes, one camp per night), counted from the planting.");
            SurtrRadius = Config.Bind("Surtrbrand", "Radius", 12f, "Radius (m) of the dome.");
            SurtrSpawnBlock = Config.Bind("Surtrbrand", "SpawnBlockRadius", 40f, "No creature spawns within this distance (m) of the sword while the dome stands.");
            SurtrPush = Config.Bind("Surtrbrand", "Push", 9f, "Speed (m/s) a creature inside the dome is thrown back out at (less for heavy ones).");
            SurtrPlantDistance = Config.Bind("Surtrbrand", "PlantDistance", 1.4f, "How far ahead (m) the sword is driven in.");
            SurtrLean = Config.Bind("Surtrbrand", "Lean", 6f, "Lean (degrees) of the planted sword, its hilt away from the wielder.");
            SurtrSink = Config.Bind("Surtrbrand", "Sink", 0.35f, "How deep (m) the blade's tip goes into the ground.");
            SurtrRespawn = Config.Bind("Surtrbrand", "RespawnAtSword", true, "After a death while the dome stands, come back to life at the sword (the bed is left alone).");
            SurtrDomeTint = Config.Bind("Surtrbrand", "DomeTint", new Color(1f, 0.42f, 0.12f, 1f), "Hue of the dome (Haldor's force field, recoloured; restart).");
            SurtrChests = Config.Bind("Loot", "SurtrChests", "TreasureChest_charredfortress,TreasureChest_ashland_stone",
                "Chest prefabs (comma-separated) that can hold Surtrbrand.");
            SurtrChestChance = Config.Bind("Loot", "SurtrChestChance", 0.015f, "Chance (0-1) per such chest, rolled once when it is first filled.");
            YmirHoldTime = Config.Bind("YmirBite", "HoldTime", 1f, "Seconds to hold the secondary attack for Ymir's Blood (a shorter press is the atgeir's own sweep).");
            GiantDuration = Config.Bind("YmirBite", "GiantDuration", 45f, "Seconds as a frost giant.");
            GiantCooldown = Config.Bind("YmirBite", "GiantCooldown", 900f, "Seconds between two transformations (15 minutes), counted from the transformation.");
            GiantScale = Config.Bind("YmirBite", "GiantScale", 2f, "How many times taller the giant is.");
            GiantSmashDamage = Config.Bind("YmirBite", "SmashDamage", 80f, "Blunt damage dealt to whatever the giant walks into (three times as much against trees and rocks).");
            GiantAnimSpeed = Config.Bind("YmirBite", "AnimationSpeed", 0.75f, "Speed of the giant's animations (1 = normal).");
            GiantBreakBuildings = Config.Bind("YmirBite", "BreakBuildings", false, "The giant breaks buildings too.");
            YmirAuraVisual = Config.Bind("YmirBite", "AuraSize", 1.2f, "Radius (m) of the visible frost round the wielder.");
            YmirChests = Config.Bind("Loot", "YmirChests", "TreasureChest_deepnorth_village",
                "Chest prefabs (comma-separated) that can hold Ymir's Bite.");
            YmirChestChance = Config.Bind("Loot", "YmirChestChance", 0.015f, "Chance (0-1) per such chest, rolled once when it is first filled.");
            AuraStrength = Config.Bind("Aura", "Strength", 1f, "How much the legendary weapons' auras show (0 = none). A weapon glows only while its power is ready.");
            Upgrades = Config.Bind("Balance", "Upgrades", true, "Legendaries can be upgraded (never crafted) up to quality 4 at their station: the next boss's trophy and five bars of the next biome's metal per level (restart).");
            HideCooldownIcons = Config.Bind("Aura", "HideCooldownIcons", true, "Keep the cooldown icons off the HUD: the aura shows when a power is ready.");
            DamageBonus = Config.Bind("Balance", "DamageBonus", 1.35f, "Each legendary's damage, times the vanilla weapon of its biome and type (restart).");
            SurtrVeil = Config.Bind("Surtrbrand", "DomeVeil", 0.045f, "Opacity of the dome's ember veil (Haldor's force field only distorts the view behind it).");
            CustomModels = Config.Bind("Models", "CustomModels", true, "Use the weapons' own 3D models (models/*.tam), else the vanilla look (restart).");
            FlipHead = Config.Bind("Models", "FlipHead", "",
                "Comma-separated models (axe, spear, sword, mace, knife) whose head points the wrong way (restart).");
            ModelLengthScale = Config.Bind("Models", "LengthScale", 1f, "Length of our models relative to the vanilla weapon they replace (restart).");
            _configStamp = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);

            AddLocalization();
            PrefabManager.OnVanillaPrefabsAvailable += CreateItem;
            CommandManager.Instance.AddConsoleCommand(new ResetCooldownsCommand());
            CommandManager.Instance.AddConsoleCommand(new HornPoseCommand());
            CommandManager.Instance.AddConsoleCommand(new HornCallCommand());
            LabEnabled = Config.Bind("Debug", "Lab", false, "Test hook: run the lines of plugins/LegendaryWeapons/lab/request.txt (console commands, camera renders).");
            new Harmony(Guid).PatchAll();
            Log.LogInfo("Legendary Weapons " + Version + " loaded");
        }

        // Edits to the .cfg apply while the game runs (checked once a second).
        private void Update()
        {
            if (Player.m_localPlayer != null)
            {
                LightningCall.RegisterRpc();
                WindBlade.RegisterRpc();
                Earthbreaker.RegisterRpc();
                SkyFishing.RegisterRpc();
                FogHorn.RegisterRpc();
            }
            LightningCall.Tick();
            if (LabEnabled.Value) Lab.Poll();
            WindBlade.Tick();
            FoxFang.Tick();
            Surtrbrand.Tick();
            YmirBite.Tick();
            WeaponAuras.Tick();

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
            TestChest.AddTranslations(loc);
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "item_axethrowing", "Returning Axe" },
                { "item_axethrowing_desc", "Bound to your hand by a forgotten rune. Throw it (hold the secondary attack): it always finds its way back." },
                { "item_spearthunder", "Thunder Spear" },
                { "item_spearthunder_desc", "The storm sleeps in its point. Wake it (hold the secondary attack), then give the lightning somewhere to fall." },
                { "msg_thunderspear_called", "The storm answers... throw the spear!" },
                { "msg_thunderspear_self", "The lightning found the spear in your hand" },
                { "msg_lw_nosky", "The sky can't hear you in here" },
                { "se_swordwind_rest", "Wind at rest" },
                { "se_swordwind_rest_tooltip", "The gust is ready again when this ends." },
                { "se_maceearth_rest", "Settling ground" },
                { "se_maceearth_rest_tooltip", "The quake is ready again when this ends." },
                { "item_rodsky", "Skyfisher's Rod" },
                { "item_rodsky_desc", "Whalebone that remembers the clouds. Hold the secondary attack to cast where no fisher ever has. Under the open sky only." },
                { "msg_rodsky_found", "Something legendary was on the hook!" },
                { "msg_rodsky_broken", "The rod is spent: repair it at a workbench." },
                { "se_rodsky_rest", "Empty sky" },
                { "se_rodsky_rest_tooltip", "The sky's fish come back when this ends." },
                { "se_spearthunder_rest", "Storm at rest" },
                { "se_spearthunder_rest_tooltip", "The storm can be called again when this ends." },
                { "item_swordwind", "Wind Blade" },
                { "item_swordwind_desc", "The Plains wind never forgot its forge. Hold the secondary attack, and it cuts wherever you point." },
                { "item_maceearth", "Earthbreaker" },
                { "item_maceearth_desc", "Moss, stone and an old grudge against the ground. Hold the secondary attack, and the earth splits." },
                { "item_knifefox", "Fox Fang" },
                { "item_knifefox_desc", "Sly as the fox it is named for. Hold the secondary attack: the woods hide you, and the first blow from the shadows bites deep." },
                { "se_knifefox_camo", "Camouflage" },
                { "se_knifefox_camo_tooltip", "Unseen and unheard beyond 1.5 m. Your first blow strikes three times as hard." },
                { "se_knifefox_rest", "Fox at rest" },
                { "se_knifefox_rest_tooltip", "The camouflage is ready again when this ends." },
                { "item_hornfog", "Fog Horn" },
                { "item_hornfog_desc", "Blow, and the mist listens. Hold the secondary attack, and now and then, the mist answers." },
                { "msg_hornfog_call", "The spirits of the mist answer!" },
                { "msg_hornfog_notyet", "The mist doesn't answer yet" },
                { "se_hornfog_rest", "Silent mist" },
                { "se_hornfog_rest_tooltip", "The spirits answer the horn again when this ends." },
                { "item_swordsurtr", "Guardian Ember" },
                { "item_swordsurtr_desc", "Still warm from Surtr's forge. Plant it (hold the secondary attack): its embers watch over all who shelter there. Use takes it back." },
                { "se_swordsurtr_rest", "Sleeping ember" },
                { "se_swordsurtr_rest_tooltip", "The sword can raise its dome again when this ends." },
                { "item_atgeirymir", "Ymir's Bite" },
                { "item_atgeirymir_desc", "Carved from the bones of the first giant. Hold the secondary attack, and for a while, you become what he was." },
                { "se_atgeirymir_giant", "Giant's Form" },
                { "se_atgeirymir_giant_tooltip", "Ymir's size and toughness: blows barely hurt, you never tire, and nothing stands in your way." },
                { "se_atgeirymir_blood", "Ymir's Blood" },
                { "se_atgeirymir_blood_tooltip", "The cold can't touch you, and frost hurts you less." },
                { "se_atgeirymir_share", "Ymir's Breath" },
                { "se_atgeirymir_share_tooltip", "Beside the bearer of Ymir's Blood, the cold can't touch you." },
                { "se_atgeirymir_rest", "Frozen blood" },
                { "se_atgeirymir_rest_tooltip", "The giant wakes again when this ends." },
            });
            loc.AddTranslation("French", new Dictionary<string, string>
            {
                { "item_axethrowing", "Hache de retour" },
                { "item_axethrowing_desc", "Une rune oubliée la lie à ta main. Lance-la (maintiens l'attaque secondaire) : elle revient toujours." },
                { "item_spearthunder", "Lance du tonnerre" },
                { "item_spearthunder_desc", "L'orage dort dans sa pointe. Réveille-le (maintiens l'attaque secondaire), puis donne à la foudre un endroit où tomber." },
                { "msg_thunderspear_called", "L'orage répond... lance la lance !" },
                { "msg_thunderspear_self", "La foudre a trouvé la lance dans ta main" },
                { "msg_lw_nosky", "Le ciel ne t'entend pas ici" },
                { "se_swordwind_rest", "Vent apaisé" },
                { "se_swordwind_rest_tooltip", "La rafale est de nouveau prête quand ceci se termine." },
                { "se_maceearth_rest", "Sol qui se tasse" },
                { "se_maceearth_rest_tooltip", "Le séisme est de nouveau prêt quand ceci se termine." },
                { "item_rodsky", "Canne du pêcheur céleste" },
                { "item_rodsky_desc", "Un os de baleine qui se souvient des nuages. Maintiens l'attaque secondaire pour lancer là où nul pêcheur n'a jamais lancé. Ciel ouvert seulement." },
                { "msg_rodsky_found", "Quelque chose de légendaire a mordu à l'hameçon !" },
                { "msg_rodsky_broken", "La canne est à bout : répare-la à l'établi." },
                { "se_rodsky_rest", "Ciel vide" },
                { "se_rodsky_rest_tooltip", "Les poissons du ciel reviennent quand ceci se termine." },
                { "se_spearthunder_rest", "Orage apaisé" },
                { "se_spearthunder_rest_tooltip", "La foudre peut être rappelée quand ceci se termine." },
                { "item_swordwind", "Lame des vents" },
                { "item_swordwind_desc", "Le vent des Plaines n'a jamais oublié sa forge. Maintiens l'attaque secondaire, et il tranche là où tu pointes." },
                { "item_maceearth", "Brise-terre" },
                { "item_maceearth_desc", "De la mousse, de la pierre, et une vieille rancune contre le sol. Maintiens l'attaque secondaire : la terre se fend." },
                { "item_knifefox", "Croc du renard" },
                { "item_knifefox_desc", "Rusé comme le renard dont il porte le nom. Maintiens l'attaque secondaire : les bois te cachent, et le premier coup venu de l'ombre mord profond." },
                { "se_knifefox_camo", "Camouflage" },
                { "se_knifefox_camo_tooltip", "Ni vu ni entendu au-delà de 1,5 m. Ton premier coup frappe trois fois plus fort." },
                { "se_knifefox_rest", "Renard au repos" },
                { "se_knifefox_rest_tooltip", "Le camouflage est de nouveau prêt quand ceci se termine." },
                { "item_hornfog", "Corne de brume" },
                { "item_hornfog_desc", "Souffle, et la brume écoute. Maintiens l'attaque secondaire, et de loin en loin, la brume répond." },
                { "msg_hornfog_call", "Les esprits de la brume répondent !" },
                { "msg_hornfog_notyet", "La brume ne répond pas encore" },
                { "se_hornfog_rest", "Brume silencieuse" },
                { "se_hornfog_rest_tooltip", "Les esprits répondront de nouveau à la corne quand ceci se termine." },
                { "item_swordsurtr", "Braise-gardienne" },
                { "item_swordsurtr_desc", "Encore chaude de la forge de Surtr. Plante-la (maintiens l'attaque secondaire) : ses braises veillent sur ceux qui s'y abritent. Utiliser la reprend." },
                { "se_swordsurtr_rest", "Braise endormie" },
                { "se_swordsurtr_rest_tooltip", "L'épée peut de nouveau lever son dôme quand ceci se termine." },
                { "item_atgeirymir", "Morsure d'Ymir" },
                { "item_atgeirymir_desc", "Taillé dans les os du premier géant. Maintiens l'attaque secondaire, et pour un temps, tu deviens ce qu'il était." },
                { "se_atgeirymir_giant", "Forme de géant" },
                { "se_atgeirymir_giant_tooltip", "La taille et la robustesse d'Ymir : les coups t'effleurent, tu ne te fatigues pas, et rien ne te barre la route." },
                { "se_atgeirymir_blood", "Sang d'Ymir" },
                { "se_atgeirymir_blood_tooltip", "Le froid ne peut pas t'atteindre, et le givre te blesse moins." },
                { "se_atgeirymir_share", "Souffle d'Ymir" },
                { "se_atgeirymir_share_tooltip", "Près du porteur du sang d'Ymir, le froid ne peut pas t'atteindre." },
                { "se_atgeirymir_rest", "Sang figé" },
                { "se_atgeirymir_rest_tooltip", "Le géant se réveille de nouveau quand ceci se termine." },
            });
        }

        private void CreateItem()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= CreateItem;
            TestChest.Register();

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
            // the axe keeps AxeIron's own secondary attack; the throw is the held power (HoldPower)
            HoldPower.Register(new HoldPower.Spec
            {
                Token = ItemToken, HoldTime = () => ThrowHoldTime.Value, PowerAttack = throwAttack, Theme = ChargeFx.Theme.Rune,
            });
            Durability(item, 250f, 50f);
            Boost(item, "AxeIron");
            UpgradeOnly(item, Craftable.Value, "TrophyDragonQueen", "Silver");
            WeaponModels.Apply(item, "axe");

            ItemManager.Instance.AddItem(item);
            if (PrefabManager.Cache.GetPrefab<Container>(ChestLoot.CryptChest) == null)
                Log.LogWarning("Chest prefab " + ChestLoot.CryptChest + " not found: the axe won't appear as loot");
            Log.LogInfo("Registered " + ItemPrefab + " (clone of AxeIron, throw from SpearBronze, anim '" + throwAttack.m_attackAnimation + "')");

            CreateSpear(container);
            CreateSword();
            CreateMace();
            CreateKnife();
            CreateRod();
            CreateHorn();
            CreateSurtr(container);
            CreateYmir();
        }

        /// <summary>The crafting station of a vanilla item's recipe (repairs happen there), else a fallback.</summary>
        private static string StationOf(string itemPrefab, string fallback)
        {
            var db = ObjectDB.instance;
            if (db != null)
                foreach (var r in db.m_recipes)
                    if (r != null && r.m_item != null && r.m_item.name == itemPrefab && r.m_craftingStation != null)
                        return r.m_craftingStation.name;
            Log.LogWarning("No recipe found for " + itemPrefab + ": repairs at " + fallback);
            return fallback;
        }

        private static string FirstPrefab(params string[] names)
        {
            foreach (var n in names)
                if (PrefabManager.Cache.GetPrefab<ItemDrop>(n) != null)
                    return n;
            return null;
        }

        /// <summary>The Ashlands greatsword: a Slayer clone, ember-tinted, and the Guardian Ember (Surtrbrand.cs).</summary>
        private void CreateSurtr(GameObject container)
        {
            string basePrefab = FirstPrefab("THSwordSlayer", "THSwordKrom");
            if (basePrefab == null)
            {
                Log.LogError("No vanilla greatsword found: no Surtrbrand");
                return;
            }
            string station = StationOf(basePrefab, CraftingStations.BlackForge);
            var item = new CustomItem(SurtrPrefab, basePrefab, new ItemConfig
            {
                Name = SurtrToken,
                Description = "$item_swordsurtr_desc",
                Enabled = false,                                   // loot only; the recipe lets it be repaired
                CraftingStation = station,
                RepairStation = station,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig("Flametal", 20, 10, true),
                    new RequirementConfig("CharredBone", 6, 3, true),
                },
            });
            Tint(item, new Color(1f, 0.72f, 0.6f), "_surtr");
            var shared = item.ItemDrop.m_itemData.m_shared;
            Durability(item, 400f, 50f);
            Boost(item, basePrefab);
            UpgradeOnly(item, false, "TrophyFader", "Flametal");
            WeaponModels.Apply(item, "surtr");
            item.ItemPrefab.AddComponent<SurtrPlant>();
            Sfx.CreateSurtr(shared.m_secondaryAttack.m_triggerEffect, shared.m_hitEffect);
            ItemManager.Instance.AddItem(item);
            Surtrbrand.Register(shared, shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null, container);
            Log.LogInfo("Registered " + SurtrPrefab + " (clone of " + basePrefab + ", repaired at " + station + ", secondary anim " +
                        shared.m_secondaryAttack.m_attackAnimation + ")");
        }

        /// <summary>The Deep North atgeir: a gold atgeir clone, frost-tinted, and Ymir's Blood (YmirBite.cs).</summary>
        private void CreateYmir()
        {
            string basePrefab = FirstPrefab("AtgeirGold", "AtgeirBlackmetal");
            if (basePrefab == null)
            {
                Log.LogError("No vanilla atgeir found: no Ymir's Bite");
                return;
            }
            string station = StationOf(basePrefab, CraftingStations.Forge);
            var item = new CustomItem(YmirPrefab, basePrefab, new ItemConfig
            {
                Name = YmirToken,
                Description = "$item_atgeirymir_desc",
                Enabled = false,
                CraftingStation = station,
                RepairStation = station,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig("FreezeGland", 10, 5, true),
                    new RequirementConfig("Crystal", 6, 3, true),
                },
            });
            Tint(item, new Color(0.75f, 0.88f, 1f), "_ymir");
            var shared = item.ItemDrop.m_itemData.m_shared;
            Durability(item, 400f, 50f);
            Boost(item, basePrefab);
            UpgradeOnly(item, false, FirstOf("FrozenKingDrop", "TrophyFader"), FirstOf("Gold", "Flametal"));
            WeaponModels.Apply(item, "ymir");
            Sfx.CreateYmir(shared.m_secondaryAttack.m_triggerEffect, shared.m_hitEffect);
            ItemManager.Instance.AddItem(item);
            YmirBite.Register(shared, shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null);
            Log.LogInfo("Registered " + YmirPrefab + " (clone of " + basePrefab + ", repaired at " + station + ", secondary anim " +
                        shared.m_secondaryAttack.m_attackAnimation + ")");
        }

        /// <summary>The Mistlands Fog Horn: a club clone with no damage (it is blown, see FogHorn), our horn model.</summary>
        private void CreateHorn()
        {
            if (PrefabManager.Cache.GetPrefab<ItemDrop>("Club") == null)
            {
                Log.LogError("Club not found: no Fog Horn");
                return;
            }
            var item = new CustomItem(HornPrefab, "Club", new ItemConfig
            {
                Name = HornToken,
                Description = "$item_hornfog_desc",
                Enabled = false,                                   // loot only
            });
            var shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_damages = new HitData.DamageTypes();
            shared.m_damagesPerLevel = new HitData.DamageTypes();
            shared.m_attackForce = 0f;
            shared.m_useDurability = false;                        // a horn doesn't wear out
            shared.m_maxQuality = 1;
            shared.m_weight = 1.5f;
            shared.m_value = 0;
            WeaponModels.Apply(item, "horn");
            Sfx.CreateHorn(shared.m_hitEffect, shared.m_attack.m_triggerEffect);
            ItemManager.Instance.AddItem(item);
            FogHorn.Register(shared, shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null);
            Log.LogInfo("Registered " + HornPrefab + " (clone of Club, no damage; animals: " + HornAnimals.Value + ")");
        }

        /// <summary>The Skyfisher's Rod: a vanilla fishing rod (look and fishing kept), and the sky cast.</summary>
        private void CreateRod()
        {
            if (PrefabManager.Cache.GetPrefab<ItemDrop>("FishingRod") == null)
            {
                Log.LogError("FishingRod not found: no Skyfisher's Rod");
                return;
            }
            // a disabled recipe: never crafted, but it lets the rod be repaired at the workbench (the cast breaks it)
            var item = new CustomItem(RodPrefab, "FishingRod", new ItemConfig
            {
                Name = RodToken,
                Description = "$item_rodsky_desc",
                Enabled = false,
                CraftingStation = CraftingStations.Workbench,
                RepairStation = CraftingStations.Workbench,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig("FineWood", 6, 3, true),
                    new RequirementConfig("LinenThread", 4, 2, true),
                },
            });
            // keeps the vanilla rod's look (Lekinox's choice)
            var shared = item.ItemDrop.m_itemData.m_shared;
            Durability(item, 200f, 0f);                            // the sky cast takes it all: repair at the workbench
            shared.m_destroyBroken = false;                        // broken, never destroyed (the default would delete it)
            Icons.Make(item, null, null, true);                    // the vanilla look upside down otherwise, with its halo
            Sfx.CreateFishing(shared.m_attack.m_startEffect, shared.m_hitEffect);
            ItemManager.Instance.AddItem(item);
            SkyFishing.Register(shared);
            Log.LogInfo("Registered " + RodPrefab + " (clone of FishingRod)");
        }

        private static void Tint(CustomItem item, Color tint, string suffix)
        {
            foreach (var r in item.ItemPrefab.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].HasProperty("_Color")) continue;
                    var m = new Material(mats[i]) { name = mats[i].name + suffix };
                    m.SetColor("_Color", m.GetColor("_Color") * tint);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }

        /// <summary>The Meadows mace: a mossy-stone bronze mace with Meadows-tier blunt damage, and the quake.</summary>
        private void CreateMace()
        {
            if (PrefabManager.Cache.GetPrefab<ItemDrop>("MaceBronze") == null)
            {
                Log.LogError("MaceBronze not found: no Earthbreaker");
                return;
            }
            var item = new CustomItem(MacePrefab, "MaceBronze", new ItemConfig
            {
                Name = MaceToken,
                Description = "$item_maceearth_desc",
                Enabled = MaceCraftable.Value,
                CraftingStation = CraftingStations.Workbench,
                RepairStation = CraftingStations.Workbench,
                MinStationLevel = 2,
                Requirements = new[]
                {
                    new RequirementConfig("FineWood", 6, 3, true),
                    new RequirementConfig("Flint", 8, 4, true),
                    new RequirementConfig("LeatherScraps", 4, 2, true),
                    new RequirementConfig("TrophyEikthyr", 1, 0, true),
                },
            });
            Tint(item, new Color(0.62f, 0.7f, 0.58f), "_earth");
            var shared = item.ItemDrop.m_itemData.m_shared;
            Durability(item, 250f, 50f);
            Boost(item, "Club");                                  // the Meadows' own blunt weapon
            UpgradeOnly(item, MaceCraftable.Value, "TrophyTheElder", "Bronze");
            WeaponModels.Apply(item, "mace");
            Sfx.CreateQuake(shared.m_secondaryAttack.m_triggerEffect, shared.m_hitEffect);
            ItemManager.Instance.AddItem(item);
            Earthbreaker.Register(shared);
            Log.LogInfo("Registered " + MacePrefab + " (clone of MaceBronze, blunt 24, secondary anim " + shared.m_secondaryAttack.m_attackAnimation + ")");
        }

        /// <summary>The Black Forest knife: a russet copper knife, a little sharper, and the camouflage.</summary>
        private void CreateKnife()
        {
            if (PrefabManager.Cache.GetPrefab<ItemDrop>("KnifeCopper") == null)
            {
                Log.LogError("KnifeCopper not found: no Fox Fang");
                return;
            }
            const string pelt = "TrollHide";
            var item = new CustomItem(KnifePrefab, "KnifeCopper", new ItemConfig
            {
                Name = KnifeToken,
                Description = "$item_knifefox_desc",
                Enabled = KnifeCraftable.Value,
                CraftingStation = CraftingStations.Forge,
                RepairStation = CraftingStations.Forge,
                MinStationLevel = 1,
                Requirements = new[]
                {
                    new RequirementConfig("Bronze", 6, 3, true),
                    new RequirementConfig(pelt, 4, 2, true),
                },
            });
            Tint(item, new Color(0.85f, 0.55f, 0.35f), "_fox");
            var shared = item.ItemDrop.m_itemData.m_shared;
            Durability(item, 250f, 50f);
            Boost(item, "KnifeCopper");
            UpgradeOnly(item, KnifeCraftable.Value, "TrophyBonemass", "Iron");
            WeaponModels.Apply(item, "knife");
            Sfx.CreateRustle(shared.m_hitEffect, shared.m_secondaryAttack.m_triggerEffect);
            ItemManager.Instance.AddItem(item);
            FoxFang.Register(shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null);
            Log.LogInfo("Registered " + KnifePrefab + " (clone of KnifeCopper, recipe uses " + pelt + ")");
        }

        /// <summary>
        /// A legendary hits half as hard again as the vanilla weapon of its biome and type (damage and per-upgrade
        /// gain, [Balance] DamageBonus), whatever it was cloned from.
        /// </summary>
        private static void Boost(CustomItem item, string equivalent)
        {
            var eq = PrefabManager.Cache.GetPrefab<ItemDrop>(equivalent);
            if (eq == null)
            {
                Log.LogWarning(equivalent + " not found: " + item.ItemPrefab.name + " keeps its damage");
                return;
            }
            var shared = item.ItemDrop.m_itemData.m_shared;
            var src = eq.m_itemData.m_shared;
            float k = DamageBonus.Value;
            var d = src.m_damages;
            d.Modify(k);
            var dl = src.m_damagesPerLevel;
            dl.Modify(k);
            Log.LogInfo("Damage " + item.ItemPrefab.name + ": " + shared.m_damages.GetTotalDamage().ToString("F0") + " -> " +
                        d.GetTotalDamage().ToString("F0") + " (" + k + " x " + equivalent + " " + src.m_damages.GetTotalDamage().ToString("F0") + ")");
            shared.m_damages = d;
            shared.m_damagesPerLevel = dl;
        }

        /// <summary>
        /// Loot only, but not stuck at level 1: the recipe becomes upgrade-only (never in the craft tab, still the repair
        /// recipe), up to quality 4 at the weapon's station. Each level costs the trophy of the boss that comes after the
        /// weapon's biome and five bars of that next biome's metal, so a weapon found early follows the progression.
        /// A weapon set craftable in the config keeps its full recipe instead.
        /// </summary>
        private static void UpgradeOnly(CustomItem item, bool craftable, string trophy, string material)
        {
            var shared = item.ItemDrop.m_itemData.m_shared;
            shared.m_maxQuality = 4;
            var recipe = item.Recipe != null ? item.Recipe.Recipe : null;
            if (recipe == null || craftable || !Upgrades.Value)
                return;
            var reqs = new List<Piece.Requirement>();
            foreach (var (name, per) in new[] { (trophy, 1), (material, 5) })
            {
                var res = PrefabManager.Cache.GetPrefab<ItemDrop>(name);
                if (res == null)
                {
                    Log.LogWarning(name + " not found: not part of " + item.ItemPrefab.name + "'s upgrade");
                    continue;
                }
                reqs.Add(new Piece.Requirement { m_resItem = res, m_amount = 0, m_amountPerLevel = per, m_recover = false });
            }
            recipe.m_resources = reqs.ToArray();
            recipe.m_enabled = true;
            recipe.m_noCraftOnlyUpgrade = true;
            recipe.m_minStationLevel = 1;
            Log.LogInfo("Upgrades " + item.ItemPrefab.name + ": up to quality 4, " + per(reqs) + " per level");
        }

        private static string per(List<Piece.Requirement> reqs)
        {
            var parts = new List<string>();
            foreach (var r in reqs) parts.Add(r.m_amountPerLevel + " " + r.m_resItem.name);
            return string.Join(" + ", parts);
        }

        private static string FirstOf(params string[] names) => FirstPrefab(names) ?? names[names.Length - 1];

        /// <summary>Our own durability (the clones showed 1000), and a per-upgrade gain.</summary>
        private static void Durability(CustomItem item, float max, float perLevel)
        {
            var shared = item.ItemDrop.m_itemData.m_shared;
            Log.LogInfo("Durability " + item.ItemPrefab.name + ": " + shared.m_maxDurability + " -> " + max + " (+" + perLevel + "/level)");
            shared.m_useDurability = true;
            shared.m_maxDurability = max;
            shared.m_durabilityPerLevel = perLevel;
            item.ItemDrop.m_itemData.m_durability = max;
        }

        /// <summary>A Plains sword (black metal): vanilla attacks, plus the air slash on the secondary (WindBlade).</summary>
        private void CreateSword()
        {
            if (PrefabManager.Cache.GetPrefab<ItemDrop>("SwordBlackmetal") == null)
            {
                Log.LogError("SwordBlackmetal not found: no Wind Blade");
                return;
            }
            var item = new CustomItem(SwordPrefab, "SwordBlackmetal", new ItemConfig
            {
                Name = SwordToken,
                Description = "$item_swordwind_desc",
                Enabled = SwordCraftable.Value,
                CraftingStation = "forge",
                RepairStation = "forge",
                MinStationLevel = 3,
                Requirements = new[]
                {
                    new RequirementConfig("BlackMetal", 20, 10, true),
                    new RequirementConfig("LinenThread", 6, 3, true),
                    new RequirementConfig("Feathers", 10, 5, true),
                },
            });
            // a pale, steel-blue blade
            foreach (var r in item.ItemPrefab.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer) continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null || !mats[i].HasProperty("_Color")) continue;
                    var m = new Material(mats[i]) { name = mats[i].name + "_wind" };
                    m.SetColor("_Color", m.GetColor("_Color") * new Color(0.82f, 0.92f, 1f));
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
            var shared = item.ItemDrop.m_itemData.m_shared;
            Durability(item, 350f, 50f);
            Boost(item, "SwordBlackmetal");
            UpgradeOnly(item, SwordCraftable.Value, "TrophySeekerQueen", "Eitr");
            WeaponModels.Apply(item, "sword");
            Sfx.CreateSlash(shared.m_secondaryAttack.m_triggerEffect, shared.m_hitEffect);
            ItemManager.Instance.AddItem(item);
            WindBlade.Register(shared);
            if (PrefabManager.Cache.GetPrefab<Container>(SwordChest.Value) == null)
                Log.LogWarning("Chest prefab " + SwordChest.Value + " not found: the Wind Blade won't appear as loot");
            Log.LogInfo("Registered " + SwordPrefab + " (clone of SwordBlackmetal, secondary anim " + shared.m_secondaryAttack.m_attackAnimation + ")");
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
            Durability(item, 300f, 50f);
            Boost(item, PrefabManager.Cache.GetPrefab<ItemDrop>("SpearWolfFang") != null ? "SpearWolfFang" : basePrefab);
            UpgradeOnly(item, SpearCraftable.Value, "TrophyGoblinKing", "BlackMetal");

            WeaponModels.Apply(item, "spear");
            Sfx.Create(throwAttack.m_triggerEffect, throwAttack.m_startEffect,
                vanillaProjectile != null ? vanillaProjectile.m_hitEffects : null, shared.m_hitEffect);

            ItemManager.Instance.AddItem(item);
            LightningCall.Register(shared.m_icons != null && shared.m_icons.Length > 0 ? shared.m_icons[0] : null);
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
                    if (!s_called && LightningCall.Ready && Time.time - s_holdStart >= Plugin.CallHoldTime.Value && OpenSky.Check(__instance))
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
            if (chest == Plugin.SwordChest.Value)
                Roll(__instance, Plugin.SwordPrefab, Plugin.SwordChestChance.Value);
            if (In(Plugin.MaceChests.Value, chest))
                Roll(__instance, Plugin.MacePrefab, Plugin.MaceChestChance.Value);
            if (In(Plugin.KnifeChests.Value, chest))
                Roll(__instance, Plugin.KnifePrefab, Plugin.KnifeChestChance.Value);
            if (In(Plugin.HornChests.Value, chest))
                Roll(__instance, Plugin.HornPrefab, Plugin.HornChestChance.Value);
            if (In(Plugin.SurtrChests.Value, chest))
                Roll(__instance, Plugin.SurtrPrefab, Plugin.SurtrChestChance.Value);
            if (In(Plugin.YmirChests.Value, chest))
                Roll(__instance, Plugin.YmirPrefab, Plugin.YmirChestChance.Value);
        }

        private static bool In(string list, string chest)
        {
            foreach (var c in list.Split(','))
                if (c.Trim() == chest)
                    return true;
            return false;
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
