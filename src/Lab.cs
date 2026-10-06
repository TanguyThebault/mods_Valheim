using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Caca
{
    /// <summary>
    /// File-driven test hook (for an agent testing the mod): write lines to plugins/Caca/lab/request.txt and they
    /// run in order, then the file is renamed .done.
    ///   cmd &lt;console command&gt;                    run a console command (e.g. `cmd pipi force`)
    ///   wait &lt;seconds&gt;
    ///   view &lt;name&gt; &lt;azimuth&gt; &lt;elevation&gt; &lt;distance&gt; [height]   render the local player from a camera
    ///        orbiting a point `height` m above the feet (azimuth 0 = in front, degrees) -> lab/&lt;name&gt;.png
    ///   key &lt;Key|PeeKey&gt;                        same as pressing the mod's key
    ///   cam &lt;az&gt; &lt;el&gt; &lt;dist&gt; &lt;height&gt; [orbit deg/s]   the game camera orbits the player (showcase shots)
    ///   camoff                                   back to the normal camera
    ///   aim &lt;yaw&gt; &lt;pitch&gt; / aimoff                   fixed pee aim (degrees, in the body's frame)
    ///   turn &lt;degrees&gt;                          turn the player
    ///   tp &lt;forward m&gt; [right m]               hop, snapped to the ground; where: log the position
    ///   equip &lt;prefab&gt; [quality]               add an item and equip it
    ///   hud &lt;0|1&gt;                                hide / show the HUD
    /// </summary>
    internal static class Lab
    {
        private static float s_next;
        private static bool s_running;

        private static string Dir => Path.Combine(Wav.PluginDir, "lab");

        private static readonly HarmonyLib.AccessTools.FieldRef<Character, Quaternion> s_lookYaw =
            HarmonyLib.AccessTools.FieldRefAccess<Character, Quaternion>("m_lookYaw");
        public static bool CamOn, AimOn;
        public static Vector3 Aim = Vector3.forward;
        private static float s_az, s_el, s_dist, s_height, s_orbit, s_camT0;

        /// <summary>Called after the game camera's own update: places it on the scripted orbit.</summary>
        public static void PlaceCamera(Transform cam)
        {
            var p = Player.m_localPlayer;
            if (!CamOn || p == null) return;
            var target = p.transform.position + Vector3.up * s_height;
            float az = s_az + s_orbit * (Time.time - s_camT0);
            var yaw = Quaternion.Euler(0f, p.transform.eulerAngles.y, 0f);
            var dir = yaw * (Quaternion.Euler(-s_el, az, 0f) * Vector3.forward);
            cam.position = target + dir * s_dist;
            cam.LookAt(target);
        }

        public static void Poll()
        {
            if (s_running || Time.unscaledTime < s_next) return;
            s_next = Time.unscaledTime + 0.5f;
            var req = Path.Combine(Dir, "request.txt");
            if (!File.Exists(req)) return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(req);
                var done = req + ".done";
                if (File.Exists(done)) File.Delete(done);
                File.Move(req, done);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Lab: " + e.Message);
                return;
            }
            Plugin.Instance.StartCoroutine(Run(lines));
        }

        private static IEnumerator Run(string[] lines)
        {
            s_running = true;
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                Plugin.Log.LogInfo("Lab: " + line);
                var sp = line.IndexOf(' ');
                var verb = sp < 0 ? line : line.Substring(0, sp);
                var rest = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                var a = rest.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                switch (verb)
                {
                    case "cmd":
                        if (Console.instance != null) Console.instance.TryRunCommand(rest);
                        break;
                    case "wait":
                        yield return new WaitForSeconds(F(a, 0, 1f));
                        break;
                    case "key":
                        var p = Player.m_localPlayer;
                        if (p != null && rest == "PeeKey") Pee.TryOnCommand(p);
                        else if (p != null) Need.TryOnCommand(p);
                        break;
                    case "cam":
                        s_az = F(a, 0, 0f); s_el = F(a, 1, 10f); s_dist = F(a, 2, 3f); s_height = F(a, 3, 1f);
                        s_orbit = F(a, 4, 0f); s_camT0 = Time.time; CamOn = true;
                        break;
                    case "camoff":
                        CamOn = false;
                        break;
                    case "aim":
                        Aim = Quaternion.Euler(-F(a, 1, 10f), F(a, 0, 0f), 0f) * Vector3.forward;
                        AimOn = true;
                        break;
                    case "aimoff":
                        AimOn = false;
                        break;
                    case "turn":
                        var pt = Player.m_localPlayer;
                        if (pt != null)
                        {
                            var yawNow = s_lookYaw(pt);
                            s_lookYaw(pt) = Quaternion.Euler(0f, F(a, 0, 0f), 0f) * yawNow;   // the mouse look builds on it
                            pt.transform.rotation = s_lookYaw(pt);
                        }
                        break;
                    case "tp":                                   // tp <forward m> [right m]: hop, snapped to the ground
                        var pp = Player.m_localPlayer;
                        if (pp != null)
                        {
                            var to = pp.transform.position + pp.transform.forward * F(a, 0, 0f) + pp.transform.right * F(a, 1, 0f);
                            if (Physics.Raycast(to + Vector3.up * 30f, Vector3.down, out var gh, 80f, LayerMask.GetMask("terrain", "static_solid", "Default", "piece")))
                                to.y = gh.point.y + 0.05f;
                            pp.TeleportTo(to, pp.transform.rotation, false);
                        }
                        break;
                    case "where":
                        var pw = Player.m_localPlayer;
                        if (pw != null) Plugin.Log.LogInfo("Lab: at " + pw.transform.position + " yaw " + pw.transform.eulerAngles.y.ToString("0"));
                        break;
                    case "equip":
                        var pe = Player.m_localPlayer;
                        if (pe != null && a.Length > 0)
                        {
                            var it = pe.GetInventory().AddItem(a[0], 1, (int)F(a, 1, 1f), 0, 0L, "", false);
                            if (it != null) pe.EquipItem(it);
                        }
                        break;
                    case "hud":
                        if (Hud.instance != null) Hud.instance.m_userHidden = F(a, 0, 1f) < 0.5f;
                        break;
                    case "view":
                        yield return new WaitForEndOfFrame();
                        View(a.Length > 0 ? a[0] : "view", F(a, 1, 0f), F(a, 2, 10f), F(a, 3, 2.5f), F(a, 4, 1.0f));
                        break;
                }
            }
            s_running = false;
        }

        private static float F(string[] a, int i, float d) =>
            a.Length > i && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;

        private static void View(string name, float az, float el, float dist, float height)
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            var target = p.transform.position + Vector3.up * height;
            var dir = p.transform.rotation * (Quaternion.Euler(-el, az, 0f) * Vector3.forward);
            var go = new GameObject("caca_lab_cam");
            try
            {
                var cam = go.AddComponent<Camera>();
                var main = Camera.main;
                if (main != null) cam.CopyFrom(main);
                cam.enabled = false;
                cam.fieldOfView = 40f;
                cam.nearClipPlane = 0.05f;
                go.transform.position = target + dir * dist;
                go.transform.LookAt(target);
                const int w = 900, h = 900;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                var prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                RenderTexture.ReleaseTemporary(rt);
                Directory.CreateDirectory(Dir);
                File.WriteAllBytes(Path.Combine(Dir, name + ".png"), tex.EncodeToPNG());
                Object.Destroy(tex);
                Plugin.Log.LogInfo("Lab: wrote " + name + ".png");
            }
            finally
            {
                Object.Destroy(go);
            }
        }
    }
}
