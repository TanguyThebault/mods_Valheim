using System.Collections.Generic;
using System.Linq;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// What the animals give and what is made from it: at least three recipes per species, one for the start of the
    /// game, one for the middle, one for the late game (food, gear or decoration). Materials are re-tinted clones of
    /// vanilla items; the statues carry our own generated models. The tiers follow the vanilla stations and
    /// materials: Meadows/Black Forest (workbench, cauldron 1), Swamp/Mountains (forge, cauldron 2-3, stonecutter,
    /// silver, wolf pelts), Plains/Mistlands (linen, black metal, needles, eitr, black marble, cauldron 4+).
    ///
    /// | Animal  | Beginner                     | Mid game                        | Late game                         |
    /// |---------|------------------------------|---------------------------------|-----------------------------------|
    /// | Rabbit  | rabbit-fur boots (Rabbits)   | rabbit stew                     | lucky rabbit's foot (utility)     |
    /// | Fox     | fox cape (Foxes)             | fox-fur hat (frost resistant)   | silver fox cape (stealth)         |
    /// | Mouse   | honeyed field mice           | mouse and turnip pâté           | harvest pie                       |
    /// | Sparrow | sparrow-fletched arrows      | feather banner                  | sparrow feather cape              |
    /// | Owl     | owl-feather hood (quiet)     | owl statuette                   | owl-fletched needle arrows        |
    /// | Whale   | whale broth                  | whale-oil lamp                  | baleen shield                     |
    /// | Orca    | smoked orca with berries     | orca-tooth spear                | black marble orca statue          |
    /// </summary>
    internal static class Crafts
    {
        public const string RabbitFoot = "RabbitFoot";
        public const string SparrowFeather = "SparrowFeather";
        public const string OwlFeather = "OwlFeather";
        public const string WhaleMeat = "WhaleMeat";
        public const string CookedWhaleMeat = "CookedWhaleMeat";
        public const string WhaleBlubber = "WhaleBlubber";
        public const string Baleen = "Baleen";
        public const string OrcaMeat = "OrcaMeat";
        public const string CookedOrcaMeat = "CookedOrcaMeat";
        public const string OrcaTooth = "OrcaTooth";

        private static readonly Dictionary<string, GameObject> s_made = new Dictionary<string, GameObject>();

        internal static GameObject Prefab(string name) => s_made.TryGetValue(name, out var go) ? go : null;

        public static void AddTranslations(CustomLocalization loc)
        {
            var en = new Dictionary<string, string>();
            var fr = new Dictionary<string, string>();
            void T(string key, string e, string f, string ed, string fd)
            {
                en["item_wl_" + key] = e; fr["item_wl_" + key] = f;
                en["item_wl_" + key + "_desc"] = ed; fr["item_wl_" + key + "_desc"] = fd;
            }
            // materials
            T("rabbitfoot", "Rabbit's foot", "Patte de lapin", "Rare, and said to bring luck. Not to the rabbit.", "Rare, et censée porter chance. Pas au lapin.");
            T("sparrowfeather", "Sparrow feather", "Plume de moineau", "Small, light and brown. Good fletching.", "Petite, légère et brune. Un bon empennage.");
            T("owlfeather", "Owl feather", "Plume de chouette", "Soft-edged: the owl flies without a sound.", "Aux bords duveteux : la chouette vole sans un bruit.");
            T("whalemeat", "Whale meat", "Viande de baleine", "Dark, heavy meat. A whole winter's worth.", "Une viande sombre et lourde. De quoi passer l'hiver.");
            T("whalemeat_cooked", "Whale steak", "Steak de baleine", "Grilled whale. Rich and filling.", "De la baleine grillée. Riche et nourrissante.");
            T("whaleblubber", "Blubber", "Graisse de baleine", "Thick fat from a sea giant. Burns long and bright.", "L'épaisse graisse d'un géant des mers. Brûle longtemps, et clair.");
            T("baleen", "Baleen", "Fanon de baleine", "Tough, springy plates from a humpback's mouth.", "Des lames souples et solides, tirées de la bouche d'une baleine à bosse.");
            T("orcameat", "Orca meat", "Viande d'orque", "Lean, dark meat from a hunter of the sea.", "Une viande maigre et sombre, celle d'un chasseur des mers.");
            T("orcameat_cooked", "Grilled orca", "Orque grillée", "Firm and tasty.", "Ferme et savoureuse.");
            T("orcatooth", "Orca tooth", "Dent d'orque", "A conical, ivory tooth as long as a finger.", "Une dent d'ivoire conique, longue comme un doigt.");
            // food
            T("rabbitstew", "Rabbit stew", "Civet de lapin", "Rabbit simmered with carrot and turnip.", "Du lapin mijoté avec carotte et navet.");
            T("mousehoney", "Honeyed field mice", "Mulots au miel", "Crunchy little mice glazed with honey. Better than it sounds.", "De petits mulots croustillants, nappés de miel. Meilleur qu'il n'y paraît.");
            T("mousepate", "Mouse and turnip pâté", "Pâté de mulot aux navets", "A rustic pâté that keeps you on your feet.", "Un pâté rustique qui tient au corps et aux jambes.");
            T("mousepie", "Harvest pie", "Tourte des moissons", "Field mice, barley and onions under a golden crust.", "Mulots, orge et oignons sous une croûte dorée.");
            T("whalebroth", "Whale broth", "Bouillon de baleine", "Blubber and meat boiled with mushrooms. Warms you to the bones.", "Graisse et viande bouillies avec des champignons. Réchauffe jusqu'aux os.");
            T("frogsoup", "Frog soup", "Soupe de grenouille", "Frog legs simmered with mushrooms. Light and warming.", "Des cuisses de grenouille mijotées aux champignons. Légère et réconfortante.");
            T("bootsswamp", "Swamp boots", "Bottes de marais", "Lined with frog skin, which never dries out: you swim far longer.", "Doublées de peau de grenouille, qui ne sèche jamais : on nage bien plus longtemps.");
            T("elixirleap", "Leaping elixir", "Élixir de bond", "Tastes of swamp water. For two minutes you jump like a frog and land like one.", "Un goût d'eau de marais. Pendant deux minutes, on saute comme une grenouille et on retombe comme elle.");
            en["se_wl_swampboots"] = "Frog skin"; fr["se_wl_swampboots"] = "Peau de grenouille";
            en["se_wl_leap"] = "Frog leap"; fr["se_wl_leap"] = "Bond de grenouille";
            en["se_wl_leap_tooltip"] = "Jump x2, fall damage halved"; fr["se_wl_leap_tooltip"] = "Saut x2, dégâts de chute divisés par deux";
            T("beltfirefly", "Firefly lantern", "Lanterne à lucioles", "A little cage of fireflies on the belt: their glow lights your way.", "Une petite cage à lucioles à la ceinture : leur lueur éclaire le chemin.");
            T("meadfirefly", "Glowing mead", "Hydromel luminescent", "Sweet and faintly green. For ten minutes you glow, and you tire less.", "Doux et vaguement vert. Pendant dix minutes, on luit, et l'on se fatigue moins.");
            en["piece_wl_fireflyjar"] = "Firefly jar"; fr["piece_wl_fireflyjar"] = "Bocal à lucioles";
            en["piece_wl_fireflyjar_desc"] = "A jar of fireflies blinking softly. Needs no fuel."; fr["piece_wl_fireflyjar_desc"] = "Un bocal de lucioles qui clignotent doucement. Sans combustible.";
            en["se_wl_glow"] = "Firefly glow"; fr["se_wl_glow"] = "Lueur de lucioles";
            en["se_wl_glowmead"] = "Glowing"; fr["se_wl_glowmead"] = "Luminescent";
            en["se_wl_glowmead_tooltip"] = "A soft light around you, stamina regeneration +25 %"; fr["se_wl_glowmead_tooltip"] = "Une douce lumière autour de soi, régénération d'endurance +25 %";
            T("orcasmoked", "Smoked orca with berries", "Orque fumée aux baies", "Smoked orca with a sharp blueberry sauce.", "De l'orque fumée, relevée d'une sauce aux myrtilles.");
            // gear
            T("rabbitcharm", "Lucky rabbit's foot", "Patte de lapin porte-bonheur", "Worn on the belt. You land softly and jump higher.", "Se porte à la ceinture. On retombe en douceur et on saute plus haut.");
            T("foxhat", "Fox-fur hat", "Toque de renard", "A warm fur hat with a tail down the back. Keeps the cold out.", "Une toque chaude, la queue dans le dos. Protège du froid.");
            T("capefoxsilver", "Silver fox cape", "Cape du renard argenté", "Silent as a fox in the snow: quieter steps, harder to spot, no cold.", "Silencieux comme un renard dans la neige : pas plus discrets, moins repérable, pas de froid.");
            T("arrowsparrow", "Sparrow-fletched arrow", "Flèche empennée de moineau", "Light wooden arrows. The sparrow fletching flies true.", "Des flèches de bois légères. L'empennage de moineau vole droit.");
            T("capesparrow", "Sparrow feather cape", "Cape de plumes de moineau", "Hundreds of little feathers: you glide down and move a little faster.", "Des centaines de petites plumes : on descend en planant et l'on se déplace un peu plus vite.");
            T("helmetowl", "Owl-feather hood", "Capuche en plumes de chouette", "A hood lined with owl down. Your steps make less noise.", "Une capuche doublée de duvet de chouette. Vos pas font moins de bruit.");
            T("arrowowl", "Owl-fletched needle arrow", "Flèche-aiguille empennée de chouette", "Needle-tipped arrows with silent owl fletching. Hit harder.", "Des flèches à pointe d'aiguille, à l'empennage silencieux de chouette. Frappent plus fort.");
            T("shieldbaleen", "Baleen shield", "Bouclier en fanons", "Black metal rim, layered baleen: light, springy, hard to break through.", "Cerclage de fer noir, fanons superposés : léger, souple, difficile à percer.");
            T("spearorca", "Orca-tooth spear", "Lance en dent d'orque", "Orca teeth set in fine wood. Bites like the cold sea.", "Des dents d'orque serties dans du bois fin. Mord comme la mer glacée.");
            // pieces
            en["piece_wl_featherbanner"] = "Feather banner"; fr["piece_wl_featherbanner"] = "Bannière à plumes";
            en["piece_wl_featherbanner_desc"] = "Strung with sparrow feathers that flutter in the wind."; fr["piece_wl_featherbanner_desc"] = "Garnie de plumes de moineau qui frémissent au vent.";
            en["piece_wl_whalelamp"] = "Whale-oil lamp"; fr["piece_wl_whalelamp"] = "Lampe à huile de baleine";
            en["piece_wl_whalelamp_desc"] = "A hanging lamp. Fill it with blubber: it burns for days."; fr["piece_wl_whalelamp_desc"] = "Une lampe suspendue. Remplissez-la de graisse de baleine : elle brûle des jours.";
            en["piece_wl_owlstatue"] = "Owl statuette"; fr["piece_wl_owlstatue"] = "Statuette de chouette";
            en["piece_wl_owlstatue_desc"] = "A stone owl keeps watch."; fr["piece_wl_owlstatue_desc"] = "Une chouette de pierre monte la garde.";
            en["piece_wl_orcastatue"] = "Leaping orca statue"; fr["piece_wl_orcastatue"] = "Statue d'orque bondissante";
            en["piece_wl_orcastatue_desc"] = "An orca leaping from black marble waves."; fr["piece_wl_orcastatue_desc"] = "Une orque qui jaillit de vagues de marbre noir.";
            en["se_wl_rabbitluck"] = "Rabbit's luck"; fr["se_wl_rabbitluck"] = "Chance du lapin";
            en["se_wl_quiet"] = "Silent steps"; fr["se_wl_quiet"] = "Pas silencieux";
            en["se_wl_silverfox"] = "Silver fox"; fr["se_wl_silverfox"] = "Renard argenté";
            loc.AddTranslation("English", en);
            loc.AddTranslation("French", fr);
        }

        // ------------------------------------------------------------ materials

        /// <summary>The animals' own materials and their simplest cooking. Before the creatures (drop tables).</summary>
        public static void RegisterMaterials()
        {
            Material(RabbitFoot, "HareMeat", "rabbitfoot", new Color(0.92f, 0.8f, 0.66f), 0.6f, 0.2f, 20);
            Material(SparrowFeather, "Feathers", "sparrowfeather", new Color(0.7f, 0.52f, 0.34f), 0.8f, 0.05f, 100);
            Material(OwlFeather, "Feathers", "owlfeather", new Color(0.86f, 0.74f, 0.55f), 1.2f, 0.05f, 100);
            Material(WhaleMeat, "SerpentMeat", "whalemeat", new Color(0.62f, 0.32f, 0.32f), 1.1f, 1f, 30);
            Material(WhaleBlubber, "Entrails", "whaleblubber", new Color(1f, 0.92f, 0.76f), 1.1f, 0.5f, 50);
            Material(Baleen, "Chitin", "baleen", new Color(0.36f, 0.33f, 0.3f), 1f, 1f, 50);
            Material(OrcaMeat, "SerpentMeat", "orcameat", new Color(0.5f, 0.24f, 0.26f), 0.9f, 1f, 30);
            Material(OrcaTooth, "WolfFang", "orcatooth", new Color(1f, 0.95f, 0.84f), 1.4f, 0.3f, 50);

            Food(CookedWhaleMeat, "SerpentMeatCooked", "whalemeat_cooked", new Color(0.8f, 0.68f, 0.62f), 60f, 20f, 3f, 2000f, null, 0, null);
            Food(CookedOrcaMeat, "SerpentMeatCooked", "orcameat_cooked", new Color(0.86f, 0.76f, 0.72f), 55f, 25f, 3f, 1800f, null, 0, null);
            Cook(WhaleMeat, CookedWhaleMeat, 40f);
            Cook(OrcaMeat, CookedOrcaMeat, 30f);
        }

        /// <summary>The recipes and pieces (after the creatures: some use their pelts and meat).</summary>
        public static void RegisterRecipes()
        {
            // --- food (cauldron)
            Food("RabbitStew", "DeerStew", "rabbitstew", new Color(0.95f, 0.85f, 0.75f), 48f, 32f, 3f, 1800f,
                CraftingStations.Cauldron, 2, Req((Rabbits.MeatPrefab, 2), ("Carrot", 1), ("Turnip", 1)));
            Food("MouseHoney", "HoneyGlazedChicken", "mousehoney", Color.white, 30f, 22f, 2f, 1500f,
                CraftingStations.Cauldron, 1, Req((Mice.MeatPrefab, 3), ("Honey", 1)), scale: 0.6f);
            Food("MousePate", "TurnipStew", "mousepate", new Color(0.85f, 0.72f, 0.6f), 22f, 52f, 3f, 1800f,
                CraftingStations.Cauldron, 2, Req((Mice.MeatPrefab, 4), ("Turnip", 2), ("Thistle", 1)));
            Food("MousePie", "LoxPie", "mousepie", new Color(1f, 0.9f, 0.7f), 72f, 42f, 4f, 2400f,
                CraftingStations.Cauldron, 4, Req((Mice.MeatPrefab, 4), ("BarleyFlour", 2), ("Onion", 2)));
            Food("WhaleBroth", "SerpentStew", "whalebroth", new Color(1f, 0.95f, 0.85f), 50f, 35f, 3f, 1800f,
                CraftingStations.Cauldron, 1, Req((WhaleMeat, 1), (WhaleBlubber, 1), ("Mushroom", 2)));
            Food("OrcaSmoked", "FishCooked", "orcasmoked", new Color(0.62f, 0.45f, 0.5f), 58f, 30f, 3f, 1800f,
                CraftingStations.Cauldron, 1, Req((OrcaMeat, 1), ("Blueberries", 4)));

            // --- gear
            RabbitCharm();
            Helmet("HelmetFox", "foxhat", new Color(0.95f, 0.55f, 0.28f), 8f, frost: true, se: null, station: CraftingStations.Workbench, level: 3,
                req: Req((Foxes.PeltPrefab, 3), ("WolfPelt", 1), ("LeatherScraps", 2)));
            Helmet("HelmetOwl", "helmetowl", new Color(0.72f, 0.6f, 0.45f), 3f, frost: false,
                se: Stats("SE_WL_OwlQuiet", "$se_wl_quiet", s => { s.m_noiseModifier = -0.2f; s.m_stealthModifier = -0.1f; }),
                station: CraftingStations.Workbench, level: 2, req: Req((OwlFeather, 4), ("LeatherScraps", 3), ("DeerHide", 2)));
            SilverFoxCape();
            SparrowCape();
            Arrow("ArrowSparrow", "ArrowWood", "arrowsparrow", new Color(0.8f, 0.65f, 0.5f), 1.15f, Req(("Wood", 8), (SparrowFeather, 2)));
            Arrow("ArrowOwl", "ArrowNeedle", "arrowowl", new Color(0.9f, 0.82f, 0.7f), 1.1f, Req(("Wood", 8), ("Needle", 4), (OwlFeather, 2)));
            BaleenShield();
            OrcaSpear();
            FrogRecipes();
            FireflyRecipes();

            // --- decoration
            FeatherBanner();
            WhaleLamp();
            Statue("piece_wl_owlstatue", "owl_perched", "StatueHare", 0f, 1f, Req(("Stone", 10), (OwlFeather, 2)));
            Statue("piece_wl_orcastatue", "orca", "StatueHare", -32f, 1.9f, Req(("BlackMarble", 12), (OrcaTooth, 2)));
            SetDurability();
        }

        /// <summary>
        /// Our own durability for each piece of gear (the clones all showed 1000): light fur and feathers wear fast,
        /// late-game gear lasts longer; weapons and shields gain some per upgrade. The charm never wears.
        /// </summary>
        private static void SetDurability()
        {
            var table = new (string item, float max, float perLevel)[]
            {
                (Rabbits.BootsPrefab, 200f, 0f), (Foxes.CapePrefab, 400f, 0f), ("HelmetFox", 500f, 0f), ("HelmetOwl", 300f, 0f),
                ("CapeFoxSilver", 800f, 0f), ("CapeSparrow", 700f, 0f), ("ShieldBaleen", 300f, 50f), ("SpearOrcaTooth", 250f, 50f), ("BootsSwamp", 600f, 100f),
            };
            foreach (var (name, max, per) in table)
            {
                var drop = PrefabManager.Instance.GetPrefab(name)?.GetComponent<ItemDrop>();
                if (drop == null)
                {
                    Plugin.Log.LogWarning("Durability: no item " + name);
                    continue;
                }
                var s = drop.m_itemData.m_shared;
                Plugin.Log.LogInfo("Durability " + name + ": " + s.m_maxDurability + " -> " + max + (per > 0f ? " (+" + per + "/level)" : ""));
                s.m_useDurability = true;
                s.m_maxDurability = max;
                s.m_durabilityPerLevel = per;
                drop.m_itemData.m_durability = max;
            }
            var charm = PrefabManager.Instance.GetPrefab("RabbitCharm")?.GetComponent<ItemDrop>();
            if (charm != null)
                charm.m_itemData.m_shared.m_useDurability = false;
        }

        // ------------------------------------------------------------- helpers

        private static RequirementConfig[] Req(params (string item, int amount)[] r) =>
            r.Select(x => new RequirementConfig(x.item, x.amount, 0, true)).ToArray();

        private static CustomItem Clone(string name, string from, string token, Color tint, ItemConfig cfg = null)
        {
            cfg = cfg ?? new ItemConfig();
            cfg.Name = "$item_wl_" + token;
            cfg.Description = "$item_wl_" + token + "_desc";
            var item = new CustomItem(name, from, cfg);
            if (tint != Color.white)
                Rabbits.Tint(item.ItemPrefab, tint);
            s_made[name] = item.ItemPrefab;
            return item;
        }

        private static void Add(CustomItem item)
        {
            Rabbits.SetIcon(item);
            ItemManager.Instance.AddItem(item);
        }

        private static void Material(string name, string from, string token, Color tint, float scale, float weight, int stack)
        {
            var item = Clone(name, from, token, tint);
            item.ItemPrefab.transform.localScale *= scale;
            var s = item.ItemDrop.m_itemData.m_shared;
            s.m_weight = weight;
            s.m_maxStackSize = stack;
            s.m_food = s.m_foodStamina = s.m_foodEitr = 0f;     // raw: not edible (serpent meat is not either)
            Add(item);
        }

        private static void Food(string name, string from, string token, Color tint, float hp, float stamina, float regen, float time,
            string station, int level, RequirementConfig[] req, float scale = 1f)
        {
            var cfg = station != null ? new ItemConfig { CraftingStation = station, MinStationLevel = level, Requirements = req } : null;
            var item = Clone(name, from, token, tint, cfg);
            item.ItemPrefab.transform.localScale *= scale;
            var s = item.ItemDrop.m_itemData.m_shared;
            s.m_food = hp;
            s.m_foodStamina = stamina;
            s.m_foodEitr = 0f;
            s.m_foodRegen = regen;
            s.m_foodBurnTime = time;
            s.m_maxStackSize = 10;
            Add(item);
        }

        private static void Cook(string raw, string cooked, float seconds)
        {
            ItemManager.Instance.AddItemConversion(new CustomItemConversion(new CookingConversionConfig
            {
                Station = "piece_cookingstation", FromItem = raw, ToItem = cooked, CookTime = seconds,
            }));
        }

        private static SE_Stats Stats(string name, string token, System.Action<SE_Stats> set)
        {
            var se = ScriptableObject.CreateInstance<SE_Stats>();
            se.name = name;
            se.m_name = token;
            set(se);
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, false));
            return se;
        }

        private static void Equip(CustomItem item, SE_Stats se)
        {
            var s = item.ItemDrop.m_itemData.m_shared;
            if (se != null && se.m_icon == null && s.m_icons != null && s.m_icons.Length > 0)
                se.m_icon = s.m_icons[0];
            s.m_equipStatusEffect = se;
        }

        private static void TintWorn(ItemDrop.ItemData.SharedData s, Color tint)
        {
            if (s.m_armorMaterial == null)
                return;
            var worn = new Material(s.m_armorMaterial) { name = s.m_armorMaterial.name + "_wl" };
            if (worn.HasProperty("_Color"))
                worn.SetColor("_Color", worn.GetColor("_Color") * tint);
            s.m_armorMaterial = worn;
        }

        private static void Frost(ItemDrop.ItemData.SharedData s)
        {
            s.m_damageModifiers = new List<HitData.DamageModPair>
            {
                new HitData.DamageModPair { m_type = HitData.DamageType.Frost, m_modifier = HitData.DamageModifier.Resistant },
            };
        }

        // ----------------------------------------------------------------- gear

        /// <summary>Late game rabbit: a utility-slot charm (the slot of Megingjord), without the strength.</summary>
        private static void RabbitCharm()
        {
            var item = Clone("RabbitCharm", "BeltStrength", "rabbitcharm", new Color(0.92f, 0.8f, 0.66f), new ItemConfig
            {
                CraftingStation = CraftingStations.ArtisanTable,
                Requirements = Req((RabbitFoot, 3), ("Silver", 2), ("LinenThread", 4)),
            });
            var s = item.ItemDrop.m_itemData.m_shared;
            s.m_weight = 0.5f;
            s.m_movementModifier = 0f;
            s.m_maxQuality = 1;
            Add(item);
            Equip(item, Stats("SE_WL_RabbitLuck", "$se_wl_rabbitluck", se =>
            {
                se.m_fallDamageModifier = -0.5f;
                se.m_jumpModifier = new Vector3(0f, 0.12f, 0f);
                se.m_jumpStaminaUseModifier = -0.25f;
            }));
        }

        private static void Helmet(string name, string token, Color tint, float armor, bool frost, SE_Stats se, string station, int level,
            RequirementConfig[] req)
        {
            var item = Clone(name, "HelmetLeather", token, tint, new ItemConfig { CraftingStation = station, MinStationLevel = level, Requirements = req });
            var s = item.ItemDrop.m_itemData.m_shared;
            TintWorn(s, tint);
            s.m_armor = armor;
            s.m_armorPerLevel = 0f;
            s.m_maxQuality = 1;
            if (frost) Frost(s);
            Add(item);
            Equip(item, se);
        }

        private static void SilverFoxCape()
        {
            var tint = new Color(0.86f, 0.86f, 0.92f);
            var item = Clone("CapeFoxSilver", "CapeWolf", "capefoxsilver", tint, new ItemConfig
            {
                CraftingStation = CraftingStations.Workbench,
                MinStationLevel = 4,
                Requirements = Req((Foxes.PeltPrefab, 6), ("LinenThread", 6), ("Silver", 3)),
            });
            var s = item.ItemDrop.m_itemData.m_shared;
            TintWorn(s, tint);
            s.m_armor = 6f;
            s.m_armorPerLevel = 0f;
            s.m_maxQuality = 1;
            Frost(s);
            Add(item);
            Equip(item, Stats("SE_WL_SilverFox", "$se_wl_silverfox", se =>
            {
                se.m_noiseModifier = -0.3f;
                se.m_stealthModifier = -0.25f;
                se.m_sneakStaminaUseModifier = -0.2f;
            }));
        }

        /// <summary>A brown copy of the feather cape: the same slow fall, plus a little speed.</summary>
        private static void SparrowCape()
        {
            var tint = new Color(0.75f, 0.56f, 0.38f);
            var item = Clone("CapeSparrow", "CapeFeather", "capesparrow", tint, new ItemConfig
            {
                CraftingStation = CraftingStations.Workbench,
                MinStationLevel = 4,
                Requirements = Req((SparrowFeather, 12), ("LinenThread", 6), ("Eitr", 10)),
            });
            var s = item.ItemDrop.m_itemData.m_shared;
            TintWorn(s, tint);
            s.m_maxQuality = 1;
            SE_Stats se;
            if (s.m_equipStatusEffect is SE_Stats orig)
            {
                se = Object.Instantiate(orig);
                se.name = "SE_WL_SparrowCape";
                ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, false));
            }
            else
                se = Stats("SE_WL_SparrowCape", s.m_name, x => { x.m_maxMaxFallSpeed = 5f; x.m_fallDamageModifier = -1f; });
            se.m_speedModifier = 0.05f;
            Add(item);
            Equip(item, se);
        }

        private static void Arrow(string name, string from, string token, Color tint, float damage, RequirementConfig[] req)
        {
            var item = Clone(name, from, token, tint, new ItemConfig { CraftingStation = CraftingStations.Workbench, Amount = 20, Requirements = req });
            item.ItemDrop.m_itemData.m_shared.m_damages.Modify(damage);
            Add(item);
        }

        private static void BaleenShield()
        {
            var tint = new Color(0.55f, 0.5f, 0.45f);
            var item = Clone("ShieldBaleen", "ShieldBlackmetal", "shieldbaleen", tint, new ItemConfig
            {
                CraftingStation = CraftingStations.Forge,
                MinStationLevel = 3,
                Requirements = new[]
                {
                    new RequirementConfig(Baleen, 8, 4, true), new RequirementConfig("BlackMetal", 4, 2, true),
                    new RequirementConfig("LinenThread", 6, 3, true),
                },
            });
            var s = item.ItemDrop.m_itemData.m_shared;
            s.m_blockPower *= 1.1f;
            s.m_deflectionForce *= 1.2f;
            s.m_weight *= 0.75f;
            Add(item);
        }

        /// <summary>
        /// Frogs (Swamp): beginner frog soup (cauldron 1; the grilled legs are the spit conversion), mid-game swamp
        /// boots (leg armour, swimming costs half the stamina), late-game leaping elixir (cauldron 4, Plains barley).
        /// </summary>
        private static void FrogRecipes()
        {
            Food("FrogSoup", "CarrotSoup", "frogsoup", new Color(0.85f, 0.95f, 0.75f), 36f, 42f, 3f, 1800f,
                CraftingStations.Cauldron, 1, Req((Frogs.LegsPrefab, 2), ("Mushroom", 2)));

            var boots = Clone("BootsSwamp", "ArmorTrollLeatherLegs", "bootsswamp", new Color(0.6f, 0.75f, 0.45f), new ItemConfig
            {
                CraftingStation = CraftingStations.Workbench,
                MinStationLevel = 3,
                Requirements = new[]
                {
                    new RequirementConfig(Frogs.SkinPrefab, 6, 3, true), new RequirementConfig("TrollHide", 2, 1, true),
                    new RequirementConfig("LeatherScraps", 4, 2, true),
                },
            });
            var bs = boots.ItemDrop.m_itemData.m_shared;
            TintWorn(bs, new Color(0.6f, 0.75f, 0.45f));
            bs.m_armor = 12f;
            bs.m_armorPerLevel = 2f;
            bs.m_maxQuality = 3;
            Add(boots);
            Equip(boots, Stats("SE_WL_SwampBoots", "$se_wl_swampboots", se => se.m_swimStaminaUseModifier = -0.5f));

            var elixir = Clone("ElixirLeap", "MeadStaminaMinor", "elixirleap", new Color(0.55f, 0.85f, 0.4f), new ItemConfig
            {
                CraftingStation = CraftingStations.Cauldron,
                MinStationLevel = 4,
                Amount = 2,
                Requirements = Req((Frogs.LegsPrefab, 3), (Frogs.SkinPrefab, 2), ("Barley", 4), ("Honey", 2)),
            });
            var es = elixir.ItemDrop.m_itemData.m_shared;
            var leap = Stats("SE_WL_Leap", "$se_wl_leap", se =>
            {
                se.m_tooltip = "$se_wl_leap_tooltip";
                se.m_ttl = 120f;
                se.m_jumpModifier = new Vector3(0f, 1f, 0f);
                se.m_fallDamageModifier = -0.5f;
            });
            es.m_consumeStatusEffect = leap;
            Add(elixir);
            if (es.m_icons != null && es.m_icons.Length > 0) leap.m_icon = es.m_icons[0];
        }

        /// <summary>A light that follows its wearer: a point light and a few blinking motes, attached by a status effect.</summary>
        private static GameObject Glow(string name, float range, float intensity)
        {
            var go = PrefabManager.Instance.CreateEmptyPrefab(name, false);
            foreach (var c in go.GetComponents<Collider>()) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<MeshRenderer>()) Object.DestroyImmediate(c);
            foreach (var c in go.GetComponents<MeshFilter>()) Object.DestroyImmediate(c);
            var lg = new GameObject("light");
            lg.transform.SetParent(go.transform, false);
            lg.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            var l = lg.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(0.75f, 1f, 0.45f);
            l.range = range;
            l.intensity = intensity;
            l.shadows = LightShadows.None;
            var motes = new GameObject("motes");
            motes.transform.SetParent(go.transform, false);
            motes.transform.localPosition = new Vector3(0f, 1.1f, 0f);
            var ps = motes.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 4f);
            main.startSpeed = 0.1f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 1f, 0.3f), new Color(1f, 0.95f, 0.45f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission;
            em.rateOverTime = 3f;
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.6f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.1f, 0.4f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            motes.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fireflies.Soft;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, false));
            return go;
        }

        /// <summary>
        /// Fireflies (Swamp, caught at night): beginner firefly jar (a light without fuel, workbench), mid-game firefly
        /// lantern (belt: a soft light around you), late-game glowing mead (cauldron 5: ten minutes of light and +25 %
        /// stamina regeneration).
        /// </summary>
        private static void FireflyRecipes()
        {
            var jar = Piece("piece_wl_fireflyjar", "piece_dvergr_lantern", PieceCategories.Furniture, CraftingStations.Workbench,
                Req((Fireflies.ItemPrefab, 6), ("Resin", 2), ("FineWood", 1)), new Color(0.8f, 1f, 0.6f));
            jar.PiecePrefab.transform.localScale *= 0.7f;
            foreach (var l in jar.PiecePrefab.GetComponentsInChildren<Light>(true))
            {
                l.color = new Color(0.75f, 1f, 0.4f);
                l.range *= 0.8f;
            }
            var fire = jar.PiecePrefab.GetComponent<Fireplace>();
            if (fire != null) fire.m_infiniteFuel = true;
            AddPiece(jar);

            var lantern = Clone("BeltFirefly", "BeltStrength", "beltfirefly", new Color(0.8f, 1f, 0.55f), new ItemConfig
            {
                CraftingStation = CraftingStations.Forge,
                MinStationLevel = 1,
                Requirements = Req((Fireflies.ItemPrefab, 10), ("Iron", 2), ("LeatherScraps", 2)),
            });
            var ls = lantern.ItemDrop.m_itemData.m_shared;
            ls.m_movementModifier = 0f;
            ls.m_maxQuality = 1;
            ls.m_useDurability = false;
            Add(lantern);
            var glow = Glow("wl_firefly_glow", 9f, 1.1f);
            Equip(lantern, Stats("SE_WL_Glow", "$se_wl_glow", se =>
                se.m_startEffects = new EffectList { m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = glow, m_enabled = true, m_attach = true } } }));

            var mead = Clone("MeadFirefly", "MeadStaminaMinor", "meadfirefly", new Color(0.7f, 1f, 0.5f), new ItemConfig
            {
                CraftingStation = CraftingStations.Cauldron,
                MinStationLevel = 5,
                Amount = 2,
                Requirements = Req((Fireflies.ItemPrefab, 10), ("Honey", 3), ("Sap", 2)),
            });
            var big = Glow("wl_firefly_glow_big", 14f, 1.5f);
            var se2 = Stats("SE_WL_GlowMead", "$se_wl_glowmead", se =>
            {
                se.m_tooltip = "$se_wl_glowmead_tooltip";
                se.m_ttl = 600f;
                se.m_staminaRegenMultiplier = 1.25f;
                se.m_startEffects = new EffectList { m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = big, m_enabled = true, m_attach = true } } };
            });
            mead.ItemDrop.m_itemData.m_shared.m_consumeStatusEffect = se2;
            Add(mead);
            if (mead.ItemDrop.m_itemData.m_shared.m_icons?.Length > 0) se2.m_icon = mead.ItemDrop.m_itemData.m_shared.m_icons[0];
        }

        private static void OrcaSpear()
        {
            var item = Clone("SpearOrcaTooth", "SpearWolfFang", "spearorca", new Color(1f, 0.95f, 0.86f), new ItemConfig
            {
                CraftingStation = CraftingStations.Forge,
                MinStationLevel = 2,
                Requirements = new[]
                {
                    new RequirementConfig(OrcaTooth, 4, 2, true), new RequirementConfig("FineWood", 6, 3, true),
                    new RequirementConfig("Silver", 2, 1, true),
                },
            });
            var s = item.ItemDrop.m_itemData.m_shared;
            s.m_damages.Modify(1.1f);
            s.m_damages.m_frost += 10f;
            Add(item);
        }

        // ----------------------------------------------------------- decoration

        private static CustomPiece Piece(string name, string from, string category, string station, RequirementConfig[] req, Color tint)
        {
            var piece = new CustomPiece(name, from, new PieceConfig
            {
                Name = "$" + name,
                Description = "$" + name + "_desc",
                PieceTable = PieceTables.Hammer,
                Category = category,
                CraftingStation = station,
                Requirements = req,
            });
            if (tint != Color.white)
                Rabbits.Tint(piece.PiecePrefab, tint);
            return piece;
        }

        private static void AddPiece(CustomPiece piece)
        {
            try
            {
                var icon = RenderManager.Instance.Render(piece.PiecePrefab, RenderManager.IsometricRotation);
                if (icon != null)
                    piece.Piece.m_icon = icon;
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Icon render failed for " + piece.PiecePrefab.name + ": " + e.Message);
            }
            PieceManager.Instance.AddPiece(piece);
            Plugin.Log.LogInfo("Registered piece " + piece.PiecePrefab.name);
        }

        private static void FeatherBanner()
        {
            AddPiece(Piece("piece_wl_featherbanner", "piece_banner02", PieceCategories.Furniture, CraftingStations.Workbench,
                Req(("FineWood", 2), (SparrowFeather, 6), ("LeatherScraps", 2)), new Color(0.82f, 0.62f, 0.42f)));
        }

        /// <summary>The hanging brazier, fed with blubber instead of coal, and burning far longer.</summary>
        private static void WhaleLamp()
        {
            var piece = Piece("piece_wl_whalelamp", "piece_brazierceiling01", PieceCategories.Furniture, CraftingStations.Forge,
                Req(("Bronze", 2), ("Chain", 1), (WhaleBlubber, 4)), Color.white);
            var fire = piece.PiecePrefab.GetComponent<Fireplace>();
            var fuel = Prefab(WhaleBlubber);
            if (fire != null && fuel != null)
            {
                fire.m_fuelItem = fuel.GetComponent<ItemDrop>();
                fire.m_startFuel = 0f;
                fire.m_maxFuel = 6f;
                fire.m_secPerFuel = 6000f;      // one lump of blubber: about 1.7 hours of light
            }
            AddPiece(piece);
        }

        /// <summary>
        /// A vanilla stone statue with our own model in place of its mesh (same stone material, collider and
        /// placement), fitted to the original's footprint: largest side = original largest side x size, standing on
        /// the original's base. pitch > 0 leans the model nose-down, < 0 nose-up (a leap).
        /// </summary>
        private static void Statue(string name, string model, string from, float pitch, float size, RequirementConfig[] req)
        {
            var d = ModelData.Load(model);
            if (d == null)
                return;
            var piece = Piece(name, from, PieceCategories.Furniture, CraftingStations.Stonecutter, req, Color.white);
            var root = piece.PiecePrefab.transform;
            var filters = piece.PiecePrefab.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null).ToArray();
            if (filters.Length == 0)
            {
                Plugin.Log.LogWarning(name + ": no mesh in " + from);
                return;
            }
            // the original's bounds in the piece's space
            var orig = new Bounds();
            bool first = true;
            foreach (var f in filters)
            {
                var m = root.worldToLocalMatrix * f.transform.localToWorldMatrix;
                var b = f.sharedMesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    var c = m.MultiplyPoint3x4(b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1)));
                    if (first) { orig = new Bounds(c, Vector3.zero); first = false; }
                    else orig.Encapsulate(c);
                }
            }
            // our model, posed and fitted in the piece's space
            var rot = Quaternion.Euler(pitch, 0f, 0f);
            var pos = d.Pos.Select(p => rot * (p - d.Bounds.center)).ToArray();
            var nb = new Bounds(pos[0], Vector3.zero);
            foreach (var p in pos) nb.Encapsulate(p);
            float scale = Mathf.Max(orig.size.x, orig.size.y, orig.size.z) * size / Mathf.Max(nb.size.x, nb.size.y, nb.size.z);
            var offset = new Vector3(orig.center.x, orig.min.y, orig.center.z) - new Vector3(nb.center.x, nb.min.y, nb.center.z) * scale;
            for (int i = 0; i < pos.Length; i++) pos[i] = pos[i] * scale + offset;
            var nrm = d.Nrm.Select(n => rot * n).ToArray();
            foreach (var f in filters)
            {
                var m = f.transform.worldToLocalMatrix * root.localToWorldMatrix;
                var mesh = new Mesh { name = name + "_mesh" };
                if (pos.Length > 65000) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.vertices = pos.Select(p => m.MultiplyPoint3x4(p)).ToArray();
                mesh.normals = nrm.Select(n => m.MultiplyVector(n).normalized).ToArray();
                mesh.uv = d.Uv;
                mesh.triangles = d.Idx;
                mesh.RecalculateBounds();
                ProcRig.SafeTangents(mesh);
                f.sharedMesh = mesh;
            }
            // colliders keep the original's shape, close enough for a statue; a mesh collider gets ours
            foreach (var mc in piece.PiecePrefab.GetComponentsInChildren<MeshCollider>(true))
                if (mc.GetComponent<MeshFilter>() is MeshFilter mf && mf.sharedMesh != null)
                    mc.sharedMesh = mf.sharedMesh;
            Plugin.Log.LogInfo(name + ": " + model + " in " + from + " (" + filters.Length + " meshes, original " + orig.size.ToString("F2") + ", scale " + scale.ToString("F3") + ")");
            AddPiece(piece);
        }
    }
}
