using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// A player in ghost mode (or debug-flying) is invisible to our animals. Vanilla AnimalAI stops finding such a
    /// player, but a rabbit or mouse that targeted them before keeps fleeing forever (the target is only dropped
    /// once alerted, and an unsensed target never alerts); birds flee from any player in range.
    /// </summary>
    internal static class Ghost
    {
        private static readonly HashSet<string> s_ours = new HashSet<string>
        {
            Rabbits.CreaturePrefab, Mice.CreaturePrefab, Foxes.CreaturePrefab, Birds.SparrowPrefab, Birds.OwlPrefab, "Crow",
            Frogs.CreaturePrefab,
        };

        internal static bool Ignored(Player p) => p != null && (p.InGhostMode() || p.InDebugFlyMode());

        internal static bool Ours(GameObject go) => s_ours.Contains(Utils.GetPrefabName(go));

        /// <summary>Player.IsPlayerInRange without the players in ghost mode.</summary>
        internal static bool PlayerInRange(Vector3 point, float range)
        {
            foreach (var p in Player.GetAllPlayers())
                if (p != null && !Ignored(p) && Vector3.Distance(p.transform.position, point) < range)
                    return true;
            return false;
        }
    }

    [HarmonyPatch(typeof(AnimalAI), nameof(AnimalAI.UpdateAI))]
    internal static class GhostAnimalAI
    {
        private static readonly AccessTools.FieldRef<AnimalAI, Character> s_target = AccessTools.FieldRefAccess<AnimalAI, Character>("m_target");

        private static void Prefix(AnimalAI __instance)
        {
            var target = s_target(__instance);
            if (target is Player p && Ghost.Ignored(p) && Ghost.Ours(__instance.gameObject))
            {
                s_target(__instance) = null;
                Traverse.Create(__instance).Method("SetAlerted", false).GetValue();
            }
        }
    }

    [HarmonyPatch(typeof(RandomFlyingBird), "DangerNearby")]
    internal static class GhostBirds
    {
        private static bool Prefix(RandomFlyingBird __instance, Vector3 p, ref bool __result)
        {
            if (!Ghost.Ours(__instance.gameObject))
                return true;
            __result = Ghost.PlayerInRange(p, __instance.m_avoidDangerDistance);
            return false;
        }
    }
}
