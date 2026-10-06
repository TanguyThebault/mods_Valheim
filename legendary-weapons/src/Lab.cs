using System.Collections;
using System.Globalization;
using System.IO;
using Jotunn.Entities;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// File-driven test hook ([Debug] Lab): lines of plugins/LegendaryWeapons/lab/request.txt run in order, then the
    /// file is renamed .done.
    ///   cmd &lt;console command&gt;    wait &lt;seconds&gt;    equip &lt;prefab&gt;
    ///   view &lt;name&gt; &lt;azimuth&gt; &lt;elevation&gt; &lt;distance&gt; [height] [fov]   render the local player from a camera
    ///        orbiting a point `height` m above the feet (azimuth 0 = in front, degrees) -> lab/&lt;name&gt;.png
    /// </summary>
    internal static class Lab
    {
        private static float s_next;
        private static bool s_running;

        private static string Dir => Path.Combine(Wav.PluginDir, "lab");

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
            while (Player.m_localPlayer == null)                     // wait for the character to be in the world
                yield return new WaitForSeconds(0.5f);
            yield return new WaitForSeconds(2f);
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                Plugin.Log.LogInfo("Lab: " + line);
                var sp = line.IndexOf(' ');
                var verb = sp < 0 ? line : line.Substring(0, sp);
                var rest = sp < 0 ? "" : line.Substring(sp + 1).Trim();
                var a = rest.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (verb == "cmd" && Console.instance != null)
                {
                    try { Console.instance.TryRunCommand(rest); }
                    catch (System.Exception e) { Plugin.Log.LogWarning("Lab: " + rest + " failed: " + e.Message); }
                }
                else if (verb == "wait") yield return new WaitForSeconds(F(a, 0, 1f));
                else if (verb == "equip" && a.Length > 0 && Player.m_localPlayer != null)
                {
                    var pl = Player.m_localPlayer;
                    var it = pl.GetInventory().AddItem(a[0], 1, 1, 0, 0L, "", false);
                    if (it != null) pl.EquipItem(it);
                }
                else if (verb == "view")
                {
                    yield return new WaitForEndOfFrame();
                    View(a.Length > 0 ? a[0] : "view", F(a, 1, 0f), F(a, 2, 10f), F(a, 3, 2.5f), F(a, 4, 1.0f), F(a, 5, 40f));
                }
            }
            s_running = false;
        }

        private static float F(string[] a, int i, float d) =>
            a.Length > i && float.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : d;

        private static void View(string name, float az, float el, float dist, float height, float fov)
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            var target = p.transform.position + Vector3.up * height;
            var dir = p.transform.rotation * (Quaternion.Euler(-el, az, 0f) * Vector3.forward);
            var go = new GameObject("lw_lab_cam");
            try
            {
                var cam = go.AddComponent<Camera>();
                if (Camera.main != null) cam.CopyFrom(Camera.main);
                cam.enabled = false;
                cam.fieldOfView = fov;
                cam.nearClipPlane = 0.03f;
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

    internal class HornCallCommand : ConsoleCommand
    {
        public override string Name => "lw_horncall";
        public override string Help => "Legendary Weapons: sound the Fog Horn's call now (spirits), ignoring the cooldown - tests";

        public override void Run(string[] args)
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            FogHorn.ResetCooldown();
            FogHorn.Blow(p, true);
        }
    }

    internal class HornPoseCommand : ConsoleCommand
    {
        public override string Name => "lw_hornpose";
        public override string Help => "Legendary Weapons: hold the Fog Horn at the mouth (again: release) - pose tests";

        public override void Run(string[] args)
        {
            var p = Player.m_localPlayer;
            if (p != null) FogHorn.TogglePose(p);
        }
    }
}
