using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;

namespace Wildlife
{
    /// <summary>
    /// Vanilla creatures and ours ignore each other, so Fulings, wolves and the like don't chase every rabbit,
    /// mouse and fox (and those don't flee from deer and boars). Exceptions:
    /// - players and tamed creatures keep the vanilla answer (you can hunt them, a tame wolf can too);
    /// - a fox that was hit fights back (FoxEnemies), and our own food chain (fox, owl) is untouched;
    /// - vanilla creatures listed in [Interactions] VanillaHunters still hunt ours.
    /// Runs after FoxEnemies, which only decides fox matters.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.IsEnemy), new[] { typeof(Character), typeof(Character) })]
    [HarmonyPriority(Priority.Low)]
    internal static class Interactions
    {
        internal static ConfigEntry<string> VanillaHunters;
        private static string s_parsed;
        private static readonly HashSet<string> s_hunters = new HashSet<string>();
        private static readonly Dictionary<Character, bool> s_isHunter = new Dictionary<Character, bool>();

        public static void BindConfig(ConfigFile config)
        {
            VanillaHunters = config.Bind("Interactions", "VanillaHunters", "",
                "Vanilla creatures (prefab names, comma separated, e.g. Wolf,Goblin) that still hunt our animals. " +
                "Empty: vanilla creatures ignore them (live).");
        }

        internal static bool IsOurs(Character c)
        {
            return RabbitTag.All.Contains(c) || MouseTag.All.Contains(c) || FoxTag.All.ContainsKey(c) || FrogAnim.All.Contains(c);
        }

        private static void Postfix(Character a, Character b, ref bool __result)
        {
            long t = Perf.Begin();
            try { PostfixImpl(a, b, ref __result); }
            finally { Perf.End("Interactions", t); }
        }

        private static void PostfixImpl(Character a, Character b, ref bool __result)
        {
            if (!__result || a == null || b == null)
                return;
            bool oursA = IsOurs(a), oursB = IsOurs(b);
            if (oursA == oursB)
                return; // both ours (our food chain) or neither (vanilla)
            Character ours = oursA ? a : b, other = oursA ? b : a;
            if (other.IsPlayer() || other.IsTamed() || ours.IsTamed())
                return;
            if (FoxTag.All.TryGetValue(ours, out var fox) && fox.IsProvokedBy(other))
                return;
            if (IsHunter(other))
                return;
            __result = false;
        }

        private static bool IsHunter(Character c)
        {
            string list = VanillaHunters.Value;
            if (string.IsNullOrEmpty(list))
                return false;
            if (list != s_parsed)
            {
                s_parsed = list;
                s_hunters.Clear();
                s_isHunter.Clear();
                foreach (var n in list.Split(','))
                    if (n.Trim().Length > 0)
                        s_hunters.Add(n.Trim());
            }
            if (!s_isHunter.TryGetValue(c, out bool hunter))
            {
                if (s_isHunter.Count > 512)
                    s_isHunter.Clear(); // forget destroyed creatures now and then
                hunter = s_hunters.Contains(Utils.GetPrefabName(c.gameObject));
                s_isHunter[c] = hunter;
            }
            return hunter;
        }
    }
}
