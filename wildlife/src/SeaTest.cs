using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using HarmonyLib;
using Jotunn.Entities;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Sea test bench (for a test world):
    ///   ta_sea goto [min depth=30]       teleport to the nearest deep water, flying and invincible
    ///   ta_sea spawn [whales=1] [orcas=2] spawn them around the player in deep water
    ///   ta_sea log [seconds=10|off]      every N seconds, log each sea swimmer: distance, owner, speed
    /// The log lines ("SeaLog ...") show whether whales and orcas keep moving over time.
    /// </summary>
    internal class SeaCommand : ConsoleCommand
    {
        private static Coroutine s_log;

        public override string Name => "ta_sea";
        public override string Help => "ta_sea goto [depth] | land | spawn [whales] [orcas] | log [seconds|off] | film [seconds] | act blow|glide|lobtail|breach|flee [whale|orca] - sea test bench";

        public override void Run(string[] args)
        {
            var player = Player.m_localPlayer;
            if (args.Length < 1 || player == null || ZoneSystem.instance == null)
            {
                Console.instance.Print(Help);
                return;
            }
            float water = ZoneSystem.instance.m_waterLevel;
            switch (args[0])
            {
                case "goto":
                {
                    float depth = args.Length > 1 ? Parse(args[1], 30f) : 30f;
                    var start = player.transform.position;
                    for (float r = 0f; r < 4000f; r += 50f)
                    {
                        int steps = Mathf.Max(1, (int)(r / 25f));
                        for (int i = 0; i < steps; i++)
                        {
                            float a = i * Mathf.PI * 2f / steps;
                            var p = start + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                            if (WorldGenerator.instance.GetHeight(p.x, p.z) < water - depth)
                            {
                                p.y = water + 6f;
                                player.TeleportTo(p, player.transform.rotation, true);
                                Traverse.Create(player).Field("m_debugFly").SetValue(true);
                                player.SetGodMode(true);
                                Print("ta_sea: teleported to deep water at " + p.ToString("F0") + " (" + r + " m away), flying");
                                return;
                            }
                        }
                    }
                    Print("ta_sea: no deep water within 4 km");
                    return;
                }
                case "land":
                {
                    // back to dry, fairly flat ground (and off with flying / god mode)
                    var start = player.transform.position;
                    for (float r = 0f; r < 4000f; r += 25f)
                    {
                        int steps = Mathf.Max(1, (int)(r / 12f));
                        for (int i = 0; i < steps; i++)
                        {
                            float a = i * Mathf.PI * 2f / steps;
                            var p = start + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                            // the world generator knows the terrain everywhere (loaded zones or not)
                            float g = WorldGenerator.instance.GetHeight(p.x, p.z);
                            float g2 = WorldGenerator.instance.GetHeight(p.x + 3f, p.z + 3f);
                            if (g > water + 3f && Mathf.Abs(g - g2) < 1.5f)
                            {
                                p.y = g + 0.5f;
                                Traverse.Create(player).Field("m_debugFly").SetValue(false);
                                player.SetGodMode(false);
                                player.TeleportTo(p, player.transform.rotation, true);
                                Plugin.Instance.StartCoroutine(SaveSoon());   // keep the spot if the game is closed
                                Print("ta_sea: back on land at " + p.ToString("F0") + " (" + r + " m away)");
                                return;
                            }
                        }
                    }
                    Print("ta_sea: no land within 4 km");
                    return;
                }
                case "spawn":
                {
                    int whales = args.Length > 1 ? (int)Parse(args[1], 1) : 1;
                    int orcas = args.Length > 2 ? (int)Parse(args[2], 2) : 2;
                    int n = 0;
                    foreach (var name in Enumerable.Repeat(Sea.WhalePrefab, whales).Concat(Enumerable.Repeat(Sea.OrcaPrefab, orcas)))
                    {
                        var prefab = ZNetScene.instance.GetPrefab(name);
                        if (prefab == null)
                            continue;
                        for (int t = 0; t < 30; t++)
                        {
                            Vector2 c = Random.insideUnitCircle.normalized * Random.Range(25f, 55f);
                            var p = player.transform.position + new Vector3(c.x, 0f, c.y);
                            if (ZoneSystem.instance.GetGroundHeight(p) < water - 15f)
                            {
                                p.y = water - 5f;
                                Object.Instantiate(prefab, p, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                                n++;
                                break;
                            }
                        }
                    }
                    Print("ta_sea: spawned " + n);
                    return;
                }
                case "act":
                {
                    string what = args.Length > 1 ? args[1].ToLowerInvariant() : "blow";
                    string want = args.Length > 2 ? args[2].ToLowerInvariant() : (what == "glide" || what == "breach" ? "orca" : what == "lobtail" ? "whale" : "");
                    var target = SeaSwimmerRegistry.All.Where(x => x != null && x.name.ToLowerInvariant().Contains(want))
                        .OrderBy(x => Vector3.Distance(x.transform.position, player.transform.position)).FirstOrDefault();
                    Print("ta_sea: " + (target == null ? "nothing to act on" : target.name + ": " + target.Force(what)));
                    return;
                }
                case "film":
                {
                    float secs = args.Length > 1 ? Parse(args[1], 36f) : 36f;
                    string want = args.Length > 2 ? args[2].ToLowerInvariant() : "";
                    var target = SeaSwimmerRegistry.All.Where(x => x != null && x.name.ToLowerInvariant().Contains(want))
                        .OrderBy(x => Vector3.Distance(x.transform.position, player.transform.position)).FirstOrDefault();
                    if (target == null)
                    {
                        Print("ta_sea: nothing to film");
                        return;
                    }
                    FilmCamera.Target = target.transform;
                    target.S.Depth = 2.5f;              // swim just under the surface while filmed: readable through the water
                    FilmCamera.Length = target.name.Contains("Whale") ? 14f : 7f;
                    FilmCamera.Start = Time.time;
                    FilmCamera.Until = Time.time + secs;
                    Print("ta_sea: filming " + target.name + " for " + secs + " s");
                    return;
                }
                case "log":
                {
                    if (s_log != null)
                        Plugin.Instance.StopCoroutine(s_log);
                    s_log = null;
                    if (args.Length > 1 && args[1] == "off")
                    {
                        Print("ta_sea: log off");
                        return;
                    }
                    float every = args.Length > 1 ? Parse(args[1], 10f) : 10f;
                    s_log = Plugin.Instance.StartCoroutine(Log(every));
                    Print("ta_sea: logging every " + every + " s");
                    return;
                }
            }
            Console.instance.Print(Help);
        }

        private static IEnumerator SaveSoon()
        {
            yield return new WaitForSeconds(6f);
            if (Game.instance != null) Game.instance.SavePlayerProfile(true);
            Plugin.Log.LogInfo("ta_sea: player profile saved");
        }

        private static IEnumerator Log(float every)
        {
            var last = new Dictionary<SeaSwimmer, Vector3>();
            while (true)
            {
                var player = Player.m_localPlayer;
                if (player != null)
                {
                    var sb = new StringBuilder("SeaLog t=" + Time.time.ToString("F0") + " n=" + SeaSwimmerRegistry.All.Count);
                    foreach (var s in SeaSwimmerRegistry.All)
                    {
                        if (s == null)
                            continue;
                        var nv = s.GetComponent<ZNetView>();
                        var p = s.transform.position;
                        float speed = last.TryGetValue(s, out var q) ? Vector3.Distance(p, q) / every : -1f;
                        last[s] = p;
                        bool valid = nv != null && nv.IsValid();
                        sb.Append(" | " + s.name.Replace("(Clone)", "") + "#" + (s.GetInstanceID() % 1000) +
                                  " d=" + Vector3.Distance(p, player.transform.position).ToString("F0") +
                                  " y=" + (p.y - ZoneSystem.instance.m_waterLevel).ToString("F1") +
                                  " v=" + speed.ToString("F2") + " " + s.State +
                                  " own=" + (valid ? (nv.IsOwner() ? "me" : nv.GetZDO().GetOwner() == 0L ? "none" : "other") : "invalid"));
                    }
                    Plugin.Log.LogInfo(sb.ToString());
                }
                yield return new WaitForSeconds(every);
            }
        }

        private static void Print(string s)
        {
            Plugin.Log.LogInfo(s);
            Console.instance?.Print(s);
        }

        private static float Parse(string s, float fallback) =>
            float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
    }
}

namespace Wildlife
{
    /// <summary>
    /// Crash hunt: each object created from the save is written (flushed at once) to
    /// plugins/Wildlife/lab/created.txt, so after a native crash in Instantiate the last line names the culprit.
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(ZNetScene), "CreateObject")]
    internal static class CreateTrace
    {
        private static System.IO.StreamWriter s_out;
        private static int s_count;

        private static void Prefix(ZDO zdo)
        {
            if (zdo == null || Look.PluginDir == null || s_count > 20000)
                return;
            var prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
            try
            {
                if (s_out == null)
                    s_out = new System.IO.StreamWriter(System.IO.Path.Combine(Look.PluginDir, "lab", "created.txt"), false) { AutoFlush = true };
                s_count++;
                s_out.WriteLine((prefab != null ? prefab.name : zdo.GetPrefab().ToString()) + " " + zdo.GetPosition().ToString("F0"));
            }
            catch (System.Exception) { }
        }
    }
}

namespace Wildlife
{
    /// <summary>
    /// Filming rig for checks in game: `ta_sea film [seconds=36]` takes over the camera and orbits the nearest sea
    /// swimmer - a third of the time at 1.2 body lengths, then 2.5, then 5 - while the height sweeps from above the
    /// animal to below it (underwater). Record the window meanwhile (um win record).
    /// </summary>
    [HarmonyLib.HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    internal static class FilmCamera
    {
        internal static float Until, Start;
        internal static Transform Target;
        internal static float Length = 10f;

        private static void Postfix(GameCamera __instance)
        {
            if (Target == null || Time.time > Until)
            {
                Target = null;
                return;
            }
            float u = (Time.time - Start) / Mathf.Max(Until - Start, 1f);   // 0..1 over the take
            int part = Mathf.Min(2, (int)(u * 3f));
            float dist = Length * (part == 0 ? 1.0f : part == 1 ? 1.8f : 3f);
            float local = u * 3f - part;                                      // 0..1 inside the part
            float az = local * Mathf.PI * 2f * 1.25f;
            float el = Mathf.Lerp(45f, 10f, Mathf.PingPong(local * 2f, 1f)) * Mathf.Deg2Rad;   // stay above the water
            var c = Target.position;
            var fwd = Target.forward; fwd.y = 0f; fwd.Normalize();
            var right = Vector3.Cross(Vector3.up, fwd);
            var dir = (right * Mathf.Cos(az) + fwd * Mathf.Sin(az)) * Mathf.Cos(el) + Vector3.up * Mathf.Sin(el);
            var t = __instance.transform;
            t.position = c + dir * dist;
            t.rotation = Quaternion.LookRotation(c - t.position, Vector3.up);
        }
    }
}
