using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Spreads our land animals and birds along the player's path instead of piling them up where a biome begins.
    /// A spawner stops once enough of a species exists in the 5x5 zones around it, and spawned creatures are saved
    /// with the world: the ones met at the edge of a biome stayed there and kept the count full, so fewer new ones
    /// appeared further in. Creatures left behind are now removed:
    /// - loaded ones (FarDespawn), by their owner, after DespawnDelay seconds with no player within DespawnDistance;
    /// - unloaded ones (the world only instantiates objects within about 2 zones of a player, so most of them are
    ///   already out of reach when the player walks on): the server sweeps their saved ZDOs every 20 s and
    ///   deletes those far from every player.
    /// Tamed creatures are never removed. Whales and orcas keep their own leash (Sea.cs).
    /// Stars: the game rolls 10 % per level (times the biome sector's multiplier); ours use StarChance instead.
    /// </summary>
    internal static class Spawns
    {
        internal static ConfigEntry<float> DespawnDistance;
        internal static ConfigEntry<float> DespawnDelay;
        internal static ConfigEntry<float> StarChance;

        private static readonly List<string> s_names = new List<string>();
        private static readonly HashSet<GameObject> s_prefabs = new HashSet<GameObject>();
        private static readonly List<ZDO> s_found = new List<ZDO>();
        private static readonly List<Vector3> s_players = new List<Vector3>();
        private static int s_sweepPrefab = -1;
        private static int s_index;
        private static float s_nextSweep;

        public static void BindConfig(ConfigFile config)
        {
            DespawnDistance = config.Bind("Spawns", "DespawnDistance", 150f,
                "Our animals and birds farther than this from every player are removed, m (live).");
            DespawnDelay = config.Bind("Spawns", "DespawnDelay", 30f,
                "Seconds a still-loaded animal must stay that far before it is removed (live).");
            StarChance = config.Bind("Spawns", "StarChance", 3f,
                "Chance (%) per level that one of our animals spawns with a star (vanilla: 10, times the area's multiplier) (live).");
        }

        /// <summary>Call on each spawnable prefab of ours.</summary>
        public static void AddDespawn(GameObject prefab)
        {
            if (prefab == null || !s_prefabs.Add(prefab))
                return;
            prefab.AddComponent<FarDespawn>();
            s_names.Add(prefab.name);
        }

        internal static bool IsOurs(GameObject prefab)
        {
            return prefab != null && s_prefabs.Contains(prefab);
        }

        /// <summary>Every frame: a slice of the sweep over saved ZDOs (server only).</summary>
        public static void Tick()
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || ZNetScene.instance == null)
            {
                s_sweepPrefab = -1;
                return;
            }
            if (s_sweepPrefab < 0)
            {
                if (Time.time < s_nextSweep || s_names.Count == 0)
                    return;
                s_sweepPrefab = 0;
                s_index = 0;
                s_found.Clear();
            }
            if (s_sweepPrefab < s_names.Count)
            {
                if (ZDOMan.instance.GetAllZDOsWithPrefabIterative(s_names[s_sweepPrefab], s_found, ref s_index))
                {
                    s_sweepPrefab++;
                    s_index = 0;
                }
                return;
            }

            s_sweepPrefab = -1;
            s_nextSweep = Time.time + 20f;
            s_players.Clear();
            s_players.Add(ZNet.instance.GetReferencePosition());
            foreach (var peer in ZNet.instance.GetPeers())
                s_players.Add(peer.m_refPos);
            float range = DespawnDistance.Value, range2 = range * range;
            long me = ZDOMan.GetSessionID();
            int removed = 0;
            foreach (var zdo in s_found)
            {
                if (!zdo.IsValid() || zdo.GetBool(ZDOVars.s_tamed) || ZNetScene.instance.FindInstance(zdo) != null)
                    continue; // loaded ones: FarDespawn decides
                if (zdo.HasOwner() && !zdo.IsOwner())
                    continue; // still handled by another player
                Vector3 p = zdo.GetPosition();
                bool near = false;
                foreach (var player in s_players)
                {
                    float dx = p.x - player.x, dz = p.z - player.z;
                    if (dx * dx + dz * dz < range2) { near = true; break; }
                }
                if (near)
                    continue;
                zdo.SetOwner(me);
                ZDOMan.instance.DestroyZDO(zdo);
                removed++;
            }
            if (removed > 0)
                Plugin.Log.LogInfo("Removed " + removed + " of our animals left behind (of " + s_found.Count + " saved)");
            s_found.Clear();
        }
    }

    public class FarDespawn : MonoBehaviour
    {
        private const float CheckInterval = 5f;

        private ZNetView _nview;
        private Character _character;
        private float _nextCheck;
        private float _farSince = -1f;

        private void Awake()
        {
            _nview = GetComponent<ZNetView>();
            _character = GetComponent<Character>();
            _nextCheck = Time.time + Random.Range(0f, CheckInterval);
        }

        private void Update()
        {
            if (Time.time < _nextCheck)
                return;
            _nextCheck = Time.time + CheckInterval;
            if (_nview == null || !_nview.IsValid() || !_nview.IsOwner() || ZNetScene.instance == null)
                return;
            if (_character != null && _character.IsTamed())
                return;
            if (Player.IsPlayerInRange(transform.position, Spawns.DespawnDistance.Value))
            {
                _farSince = -1f;
                return;
            }
            if (_farSince < 0f)
            {
                _farSince = Time.time;
                return;
            }
            if (Time.time - _farSince < Spawns.DespawnDelay.Value)
                return;
            Plugin.Log.LogDebug("Despawned " + name + " left behind at " + transform.position);
            ZNetScene.instance.Destroy(gameObject);
        }
    }

    /// <summary>Our animals roll stars with StarChance instead of the vanilla 10 %.</summary>
    [HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.GetLevelUpChance), new[] { typeof(Vector3), typeof(SpawnSystem.SpawnData) })]
    internal static class StarChancePatch
    {
        private static void Prefix(SpawnSystem.SpawnData creature)
        {
            if (creature != null && Spawns.IsOurs(creature.m_prefab))
                creature.m_overrideLevelupChance = Mathf.Max(0.01f, Spawns.StarChance.Value); // 0 would mean "vanilla 10 %"
        }
    }
}
