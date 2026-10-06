using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Test chests (black metal chests, 8 x 6) that fill themselves, once, with every item of this mod (and only this
    /// mod: Legendary Weapons has its own `lw_chest`): a stack of each material and food (20, or the stack size if
    /// smaller), one of each piece of gear. `wl_chest` drops as many chests as the items need, side by side, each
    /// holding its page (stored in its ZDO). Not in the hammer menu. The building pieces (rugs, banner, lamp, jar,
    /// statues) are not items: build them with the hammer.
    /// </summary>
    internal static class ModChest
    {
        public const string Prefab = "WL_ModChest";
        public const int Width = 8, Height = 6, PerChest = Width * Height;
        internal static readonly int PageKey = "wl_modchest_page".GetStableHashCode();

        public static void AddTranslations(CustomLocalization loc)
        {
            loc.AddTranslation("English", new Dictionary<string, string> { { "piece_wl_modchest", "Wildlife chest" } });
            loc.AddTranslation("French", new Dictionary<string, string> { { "piece_wl_modchest", "Coffre de Wildlife" } });
        }

        public static void Register()
        {
            var go = PrefabManager.Instance.CreateClonedPrefab(Prefab, "piece_chest_blackmetal");
            if (go == null)
            {
                Plugin.Log.LogWarning("piece_chest_blackmetal not found: no mod chest");
                return;
            }
            var container = go.GetComponent<Container>();
            container.m_name = "$piece_wl_modchest";
            container.m_width = Width;
            container.m_height = Height;
            var piece = go.GetComponent<Piece>();
            if (piece != null)
                piece.m_name = "$piece_wl_modchest";
            go.AddComponent<ModChestFiller>();
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            CommandManager.Instance.AddConsoleCommand(new ModChestCommand());
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
    public class ModChestFiller : MonoBehaviour
    {
        private static readonly int s_filled = "wl_modchest_filled".GetStableHashCode();

        private void Start()
        {
            var nview = GetComponent<ZNetView>();
            var container = GetComponent<Container>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner() || container == null || nview.GetZDO().GetBool(s_filled))
                return;
            var inv = container.GetInventory();
            int n = 0, page = nview.GetZDO().GetInt(ModChest.PageKey);
            foreach (var prefab in ModChest.Items().Skip(page * ModChest.PerChest).Take(ModChest.PerChest))
            {
                var data = prefab.GetComponent<ItemDrop>().m_itemData.Clone();
                data.m_dropPrefab = prefab;
                data.m_stack = data.m_shared.m_maxStackSize > 1 ? Mathf.Min(20, data.m_shared.m_maxStackSize) : 1;
                data.m_durability = data.GetMaxDurability();
                if (inv.AddItem(data))
                    n++;
                else
                    Plugin.Log.LogWarning("Mod chest full: no room for " + prefab.name);
            }
            nview.GetZDO().Set(s_filled, true);
            Plugin.Log.LogInfo("Mod chest " + (page + 1) + " filled with " + n + " items");
        }
    }

    internal class ModChestCommand : ConsoleCommand
    {
        public override string Name => "wl_chest";

        public override string Help => "Drop chests holding every item of Wildlife in front of you (as many as needed)";

        public override void Run(string[] args)
        {
            var player = Player.m_localPlayer;
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(ModChest.Prefab) : null;
            if (player == null || prefab == null)
            {
                Console.instance.Print("wl_chest: no player or no chest prefab");
                return;
            }
            int count = ModChest.Items().Count;
            int chests = Mathf.Max(1, (count + ModChest.PerChest - 1) / ModChest.PerChest);
            var fwd = player.transform.forward;
            var right = player.transform.right;
            for (int i = 0; i < chests; i++)
            {
                var pos = player.transform.position + fwd * 2.5f + right * ((i - (chests - 1) * 0.5f) * 2.2f);
                if (ZoneSystem.instance != null)
                    pos.y = ZoneSystem.instance.GetGroundHeight(pos);
                // the page is set before the filler's Start runs (same frame, we own the new ZDO)
                var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(-fwd));
                go.GetComponent<ZNetView>()?.GetZDO()?.Set(ModChest.PageKey, i);
            }
            Console.instance.Print("wl_chest: " + chests + " chest(s) with " + count + " items");
        }
    }
}
