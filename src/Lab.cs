using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Jotunn.Managers;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Model lab: renders a prefab and every animation clip of its Animator into PNG sheets, with the skeleton
    /// drawn on top, from a dedicated camera far from the world. Works from the main menu, no input needed:
    /// drop a request file and read the PNGs.
    ///
    ///   BepInEx/plugins/Wildlife/lab/request.txt   one job per line:  &lt;prefab&gt; [frames=8] [tile=256]
    ///                                                 or "refit" to re-apply the generated models with the
    ///                                                 current [MouseFit] config before rendering
    ///   -> lab/&lt;prefab&gt;_bind.png     bind pose: side | front | top
    ///      lab/&lt;prefab&gt;_&lt;clip&gt;.png   one row side view, one row front view, frames across the clip
    ///      lab/&lt;prefab&gt;_index.txt    clips, lengths, files;  lab/done.txt when the request is finished
    /// Also the console command `ta_lab &lt;prefab&gt; [frames] [tile]`.
    /// </summary>
    internal static class Lab
    {
        private const int Layer = 31;
        private static string Dir => Path.Combine(Look.PluginDir, "lab");

        private static bool s_busy;

        public static void Poll()
        {
            string req = Path.Combine(Dir, "request.txt");
            if (s_busy || !File.Exists(req) || Plugin.Instance == null)
                return;
            string[] lines;
            try
            {
                lines = File.ReadAllLines(req);
                File.Delete(req);
            }
            catch (IOException)
            {
                return; // still being written
            }
            s_busy = true;
            Plugin.Instance.StartCoroutine(Process(lines));
        }

        /// <summary>
        /// Coroutine: Unity skins meshes once per frame, so each animation sample needs its own frame before it
        /// is rendered (rendering several samples in one frame shows the same pose).
        /// </summary>
        private static IEnumerator Process(string[] lines)
        {
            var log = new StringBuilder();
            foreach (var raw in lines)
            {
                var a = raw.Split(new[] { ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);
                if (a.Length == 0)
                    continue;
                if (a[0] == "refit")
                {
                    try { log.AppendLine(Mice.Refit()); }
                    catch (System.Exception e) { log.AppendLine(raw + ": " + e); }
                    continue;
                }
                yield return Render(a[0], a.Length > 1 ? int.Parse(a[1]) : 8, a.Length > 2 ? int.Parse(a[2]) : 256, log);
            }
            File.WriteAllText(Path.Combine(Dir, "done.txt"), log.ToString());
            s_busy = false;
        }

        public static IEnumerator Render(string prefabName, int frames, int tile, StringBuilder log)
        {
            Directory.CreateDirectory(Dir);
            var prefab = PrefabManager.Instance.GetPrefab(prefabName) ?? PrefabManager.Cache.GetPrefab<GameObject>(prefabName);
            if (prefab == null)
            {
                log.AppendLine(prefabName + ": not found");
                yield break;
            }

            var go = Spawn(prefab);
            var animator = go.GetComponentInChildren<Animator>(true);
            var clips = animator != null && animator.runtimeAnimatorController != null
                ? animator.runtimeAnimatorController.animationClips.GroupBy(c => c.name).Select(g => g.First()).ToArray()
                : new AnimationClip[0];
            if (animator != null)
                animator.enabled = false;

            var bones = BoneLines(go);
            var bounds = Bounds(go);
            float size = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z) * 0.75f;
            Vector3 c = bounds.center;

            var camGo = new GameObject("lab_camera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = size;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.36f, 0.39f, 0.43f);
            cam.cullingMask = 1 << Layer;
            cam.nearClipPlane = 0.01f;
            cam.farClipPlane = size * 20f;
            var lightGo = new GameObject("lab_light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.cullingMask = 1 << Layer;
            lightGo.transform.rotation = Quaternion.Euler(40f, -35f, 0f);
            var rt = new RenderTexture(tile, tile, 24);
            cam.targetTexture = rt;

            float dist = size * 6f;
            Vector3 right = go.transform.right, fwd = go.transform.forward, up = go.transform.up;
            void View(Vector3 dir, Vector3 camUp)
            {
                camGo.transform.position = c + dir * dist;
                camGo.transform.rotation = Quaternion.LookRotation(-dir, camUp);
            }
            void Shot(Texture2D sheet, int col, int row)
            {
                foreach (var b in bones) b.Update();
                cam.Render();
                RenderTexture.active = rt;
                sheet.ReadPixels(new Rect(0, 0, tile, tile), col * tile, sheet.height - (row + 1) * tile);
                RenderTexture.active = null;
            }
            yield return null;   // let the skinned meshes update once

            var index = new StringBuilder(prefabName + "  bounds " + bounds.size.ToString("F3") + "  (left = side view from the right, head to the right if it faces +z)\n");
            var bind = new Texture2D(tile * 3, tile, TextureFormat.RGB24, false);
            View(right, up); Shot(bind, 0, 0);
            View(fwd, up); Shot(bind, 1, 0);
            View(up, fwd); Shot(bind, 2, 0);
            Save(bind, prefabName + "_bind.png");
            index.AppendLine("bind: " + prefabName + "_bind.png (side | front | top)");

            foreach (var clip in clips)
            {
                var sheet = new Texture2D(tile * frames, tile * 2, TextureFormat.RGB24, false);
                for (int f = 0; f < frames; f++)
                {
                    float t = frames > 1 ? clip.length * f / (frames - 1) : 0f;
                    clip.SampleAnimation(animator.gameObject, t);
                    yield return null;   // skinning happens once per frame
                    yield return null;
                    View(right, up); Shot(sheet, f, 0);
                    View(fwd, up); Shot(sheet, f, 1);
                }
                string file = prefabName + "_" + Safe(clip.name) + ".png";
                Save(sheet, file);
                index.AppendLine(clip.name + "  " + clip.length.ToString("F2") + " s" + (clip.isLooping ? " loop" : "") + ": " + file);
            }
            foreach (var proc in go.GetComponentsInChildren<IProcAnimated>(true))
            {
                foreach (float speed in proc.LabSpeeds)
                {
                    var sheet = new Texture2D(tile * frames, tile * 2, TextureFormat.RGB24, false);
                    for (int f = 0; f < frames; f++)
                    {
                        proc.LabPose(speed, speed < 0.1f ? f * 0.6f : f / (float)frames * 1.2f);
                        yield return null;
                        yield return null;
                        View(right, up); Shot(sheet, f, 0);
                        View(fwd, up); Shot(sheet, f, 1);
                    }
                    string file = prefabName + "_proc_" + speed.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + ".png";
                    Save(sheet, file);
                    index.AppendLine("procedural at " + speed + " m/s: " + file);
                }
            }
            File.WriteAllText(Path.Combine(Dir, prefabName + "_index.txt"), index.ToString());

            cam.targetTexture = null;
            Object.Destroy(rt);
            Object.Destroy(camGo);
            Object.Destroy(lightGo);
            Object.Destroy(go);
            log.AppendLine(prefabName + ": " + clips.Length + " clips rendered");
        }

        /// <summary>An inert copy: game scripts removed (they need the network), renderers and Animator kept.</summary>
        private static GameObject Spawn(GameObject prefab)
        {
            var holder = new GameObject("lab_holder");
            holder.SetActive(false);
            var go = Object.Instantiate(prefab, holder.transform);
            for (int pass = 0; pass < 3; pass++)   // RequireComponent dependencies: retry
                foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (!(mb is IProcAnimated))
                        try { Object.DestroyImmediate(mb); } catch { }
            foreach (var col in go.GetComponentsInChildren<Collider>(true)) col.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.useGravity = false; }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true)) ps.gameObject.SetActive(false);
            foreach (var lod in go.GetComponentsInChildren<LODGroup>(true)) lod.enabled = false;
            foreach (var t in go.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = Layer;
            go.transform.SetParent(null, false);
            go.transform.SetPositionAndRotation(new Vector3(0f, 9000f, 0f), Quaternion.identity);
            Object.Destroy(holder);
            go.SetActive(true);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            return go;
        }

        private static Bounds Bounds(GameObject go)
        {
            // only what is visible: a creature can hide its base model under its own rig
            var rs = go.GetComponentsInChildren<Renderer>().Where(r => r.enabled && !(r is ParticleSystemRenderer) && !(r is LineRenderer)).ToArray();
            if (rs.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        private class BoneLine
        {
            public Transform A, B;
            public LineRenderer Line;

            public void Update()
            {
                Line.SetPosition(0, A.position);
                Line.SetPosition(1, B.position);
            }
        }

        private static List<BoneLine> BoneLines(GameObject go)
        {
            var list = new List<BoneLine>();
            var shader = Shader.Find("Sprites/Default");
            if (shader == null)
                return list;
            var mat = new Material(shader) { color = new Color(1f, 0.9f, 0.1f) };
            float width = Mathf.Max(Bounds(go).size.magnitude * 0.006f, 0.002f);
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled))
                foreach (var bone in smr.bones)
                {
                    if (bone == null || bone.childCount == 0)
                        continue;
                    var lgo = new GameObject("bone_" + bone.name) { layer = Layer };
                    lgo.transform.SetParent(go.transform, false);
                    var lr = lgo.AddComponent<LineRenderer>();
                    lr.sharedMaterial = mat;
                    lr.positionCount = 2;
                    lr.useWorldSpace = true;
                    lr.startWidth = lr.endWidth = width;
                    lr.numCapVertices = 2;
                    list.Add(new BoneLine { A = bone, B = bone.GetChild(0), Line = lr });
                }
            return list;
        }

        private static void Save(Texture2D tex, string file)
        {
            tex.Apply(false);
            File.WriteAllBytes(Path.Combine(Dir, file), tex.EncodeToPNG());
            Object.Destroy(tex);
        }

        private static string Safe(string s)
        {
            return new string(s.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' ? ch : '_').ToArray());
        }
    }

    internal class LabCommand : Jotunn.Entities.ConsoleCommand
    {
        public override string Name => "ta_lab";
        public override string Help => "ta_lab <prefab> [frames=8] [tile=256] - render the model and its animations to plugins/Wildlife/lab";

        public override void Run(string[] args)
        {
            if (args.Length < 1)
            {
                Console.instance.Print(Help);
                return;
            }
            var log = new StringBuilder();
            Plugin.Instance.StartCoroutine(Lab.Render(args[0], args.Length > 1 ? int.Parse(args[1]) : 8, args.Length > 2 ? int.Parse(args[2]) : 256, log));
            Console.instance.Print("Rendering " + args[0] + " to plugins/Wildlife/lab ...");
        }
    }
}
