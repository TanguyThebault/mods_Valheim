using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// A test chest (a black metal chest, 8 x 2) that fills itself, once, with every item of this mod: one of each
    /// legendary weapon at full durability. Not in the hammer menu: `lw_chest` drops it in front of you (or
    /// `spawn LW_TestChest`). Wildlife has its own (`wl_chest`); the two mods share nothing.
    /// </summary>
    internal static class TestChest
    {
        public const string Prefab = "LW_TestChest";

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", new Dictionary<string, string> { { "piece_lw_testchest", "Chest of legends" } });
            loc.AddTranslation("French", new Dictionary<string, string> { { "piece_lw_testchest", "Coffre des légendes" } });
        }

        /// <summary>Needs the vanilla prefabs (call from OnVanillaPrefabsAvailable).</summary>
        public static void Register()
        {
            var go = PrefabManager.Instance.CreateClonedPrefab(Prefab, "piece_chest_blackmetal");
            if (go == null)
            {
                Plugin.Log.LogWarning("piece_chest_blackmetal not found: no test chest");
                return;
            }
            var container = go.GetComponent<Container>();
            container.m_name = "$piece_lw_testchest";
            container.m_width = 8;
            container.m_height = 2;
            var piece = go.GetComponent<Piece>();
            if (piece != null)
                piece.m_name = "$piece_lw_testchest";
            go.AddComponent<TestChestFiller>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            CommandManager.Instance.AddConsoleCommand(new TestChestCommand());
        }

        /// <summary>Every item this mod registered (Jotunn's registry), in a stable order.</summary>
        internal static List<GameObject> Items()
        {
            var list = new List<GameObject>();
            foreach (var item in ModRegistry.GetItems(Plugin.Guid).OrderBy(i => i.ItemPrefab.name))
            {
                var prefab = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab(item.ItemPrefab.name) : null;
                if (prefab != null)
                    list.Add(prefab);
            }
            return list;
        }
    }

    /// <summary>Fills the chest the first time it exists (the flag lives in its ZDO, so it never refills).</summary>
    public class TestChestFiller : MonoBehaviour
    {
        private static readonly int s_filled = "lw_testchest_filled".GetStableHashCode();

        private void Start()
        {
            var nview = GetComponent<ZNetView>();
            var container = GetComponent<Container>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner() || container == null || nview.GetZDO().GetBool(s_filled))
                return;
            var inv = container.GetInventory();
            int n = 0;
            foreach (var prefab in TestChest.Items())
            {
                var data = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
                data.m_dropPrefab = prefab;
                data.m_stack = 1;
                data.m_durability = data.GetMaxDurability();
                if (inv.AddItem(data))
                    n++;
                else
                    Plugin.Log.LogWarning("Test chest full: no room for " + prefab.name);
            }
            nview.GetZDO().Set(s_filled, true);
            Plugin.Log.LogInfo("Test chest filled with " + n + " items");
        }
    }

    internal class TestChestCommand : ConsoleCommand
    {
        public override string Name => "lw_chest";

        public override string Help => "Drop a chest holding every Legendary Weapons item in front of you";

        public override void Run(string[] args)
        {
            var player = Player.m_localPlayer;
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(TestChest.Prefab) : null;
            if (player == null || prefab == null)
            {
                Console.instance.Print("lw_chest: no player or no chest prefab");
                return;
            }
            var pos = player.transform.position + player.transform.forward * 2.5f;
            if (ZoneSystem.instance != null)
                pos.y = ZoneSystem.instance.GetGroundHeight(pos);
            Object.Instantiate(prefab, pos, Quaternion.LookRotation(-player.transform.forward));
            Console.instance.Print("lw_chest: chest with " + TestChest.Items().Count + " items");
        }
    }
}
