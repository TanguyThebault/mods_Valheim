using System.Collections.Generic;
using System.IO;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// Our own models for the legendary weapons (fal Flux concept -> Trellis 2 -> tools/build_weapons.py ->
    /// models/&lt;name&gt;.tam + .png): every mesh of the cloned vanilla item (the one held in the hand under "attach"
    /// and the one lying on the ground) is replaced by ours, fitted to it:
    /// - our length (+y, head up, grip down) runs along the original's longest axis, the head on the side away from
    ///   the grip (the held item's origin is the hand; same rule as the thrown spear), same length;
    /// - our blade width (x) along the original's second axis, the blade on the same side of the handle;
    /// - the handle passes through the hand. Uniform scale keeps our proportions; a proper rotation (no mirror).
    /// The material is a copy of the original with our texture. Thrown copies (axe, spear) copy the held item, so they
    /// get the new model too. [Models] FlipHead lists weapons whose head comes out the wrong way.
    /// </summary>
    internal static class WeaponModels
    {
        private class Tam
        {
            public Vector3[] Pos, Nrm;
            public Vector2[] Uv;
            public int[] Idx;
            public Texture2D Tex, Emit;
            public Bounds Bounds;
        }

        private static Texture2D s_flatNormal;

        /// <summary>
        /// Per model: length relative to the vanilla weapon, and how far (share of the length) the model slides
        /// towards its grip end, so the hand sits higher on the haft. Same values as tools/build_weapons.py's FIT,
        /// whose previews replay this fitting over the vanilla meshes.
        /// </summary>
        private static readonly Dictionary<string, (float length, float gripShift, bool flip)> s_fit =
            new Dictionary<string, (float, float, bool)>
        {
            { "axe", (1f, 0.08f, false) },     // the hand was low on the haft
            { "sword", (1f, 0.03f, false) },   // the lighter Wind Blade (v0.13.2): the hand mid-grip
            { "mace", (1f, 0.1f, false) },
            { "knife", (1.45f, 0.08f, false) },// was too small
            { "spear", (1f, 0f, true) },       // the vanilla spear is held near its head: the head is on the hand's side
            { "horn", (0.5f, 0.1f, false) },
            { "surtr", (1f, 0.13f, false) },   // Surtrbrand, the Ashlands greatsword (the hand just under the guard)
            { "ymir", (1f, 0f, false) },       // Ymir's Bite, the Deep North atgeir   // the Fog Horn, half a club long; the mouthpiece sticks out ~11 cm behind the fist
        };

        /// <summary>Where the head of our model points, in the held item's space (for the thrown spear).</summary>
        internal static readonly Dictionary<string, Vector3> Heads = new Dictionary<string, Vector3>();

        private static Tam Load(string name)
        {
            string dir = Path.Combine(Path.GetDirectoryName(typeof(Plugin).Assembly.Location), "models");
            string tam = Path.Combine(dir, name + ".tam"), png = Path.Combine(dir, name + ".png");
            if (!File.Exists(tam) || !File.Exists(png))
            {
                Plugin.Log.LogWarning("Weapon model missing: " + tam);
                return null;
            }
            var d = new Tam();
            using (var r = new BinaryReader(File.OpenRead(tam)))
            {
                if (new string(r.ReadChars(4)) != "TAM1")
                    throw new InvalidDataException(tam);
                int nv = r.ReadInt32(), ni = r.ReadInt32();
                d.Pos = new Vector3[nv];
                d.Nrm = new Vector3[nv];
                d.Uv = new Vector2[nv];
                d.Idx = new int[ni];
                for (int i = 0; i < nv; i++) d.Pos[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < nv; i++) d.Nrm[i] = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < nv; i++) d.Uv[i] = new Vector2(r.ReadSingle(), r.ReadSingle());
                for (int i = 0; i < ni; i++) d.Idx[i] = r.ReadInt32();
            }
            d.Tex = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = name };
            d.Tex.LoadImage(File.ReadAllBytes(png));
            string emit = Path.Combine(dir, name + "_emit.png");          // glowing parts (Surtrbrand's magma)
            if (File.Exists(emit))
            {
                d.Emit = new Texture2D(2, 2, TextureFormat.RGBA32, true) { name = name + "_emit" };
                d.Emit.LoadImage(File.ReadAllBytes(emit));
            }
            d.Bounds = new Bounds(d.Pos[0], Vector3.zero);
            foreach (var p in d.Pos) d.Bounds.Encapsulate(p);
            return d;
        }

        private static Texture2D FlatNormal
        {
            get
            {
                if (s_flatNormal != null)
                    return s_flatNormal;
                s_flatNormal = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "lw_flat_normal" };
                var px = new Color[16];
                for (int i = 0; i < 16; i++) px[i] = new Color(0.5f, 0.5f, 1f, 0.5f);
                s_flatNormal.SetPixels(px);
                s_flatNormal.Apply(false, true);
                return s_flatNormal;
            }
        }

        private static float Get(Vector3 v, int i) => i == 0 ? v.x : i == 1 ? v.y : v.z;

        private static Vector3 Axis(int i) => i == 0 ? Vector3.right : i == 1 ? Vector3.up : Vector3.forward;

        /// <summary>Puts our model on the item, re-renders its icon. False (and the vanilla look) on any problem.</summary>
        public static bool Apply(CustomItem item, string model)
        {
            if (!Plugin.CustomModels.Value)
                return false;
            var d = Load(model);
            if (d == null)
                return false;
            var root = item.ItemPrefab.transform;
            var attach = root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "attach");
            var filters = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null && f.GetComponent<MeshRenderer>() != null).ToArray();
            if (filters.Length == 0)
            {
                Plugin.Log.LogWarning(item.ItemPrefab.name + ": no mesh to replace");
                return false;
            }
            var fit = s_fit.TryGetValue(model, out var ff) ? ff : (1f, 0f, false);
            bool flip = fit.Item3 ^ Plugin.FlipHead.Value.Split(',').Any(s => s.Trim().Equals(model, System.StringComparison.OrdinalIgnoreCase));

            // our grip (the bottom quarter of the length) and which side the blade is on
            float y0 = d.Bounds.min.y, len = d.Bounds.size.y;
            var grip = d.Pos.Where(p => p.y < y0 + 0.25f * len).ToArray();
            var head = d.Pos.Where(p => p.y > y0 + 0.6f * len).ToArray();
            float gx = grip.Average(p => p.x), gz = grip.Average(p => p.z);
            float blade = head.Average(p => p.x) - gx;

            // the hand, in each original mesh's space (an item lying on the ground shares the held item's mesh)
            var handInMesh = new Dictionary<Mesh, Vector3>();
            foreach (var f in filters)
                if (attach != null && f.transform.IsChildOf(attach))
                    handInMesh[f.sharedMesh] = f.transform.InverseTransformPoint(attach.position);

            var done = new Dictionary<Mesh, Mesh>();
            Vector3? iconHead = null, iconSide = null;
            foreach (var f in filters)
            {
                var orig = f.sharedMesh;
                if (!done.TryGetValue(orig, out var ours))
                {
                    var b = orig.bounds;
                    Vector3 hand = handInMesh.TryGetValue(orig, out var h) ? h : Vector3.zero;
                    var e = b.extents;
                    int k = e.x >= e.y && e.x >= e.z ? 0 : (e.y >= e.z ? 1 : 2);
                    int j = Enumerable.Range(0, 3).Where(i => i != k).OrderByDescending(i => Get(e, i)).First();
                    int l = 3 - k - j;
                    float s = Get(b.center, k) - Get(hand, k) >= 0f ? 1f : -1f;      // head away from the hand
                    if (flip) s = -s;
                    float sj = (Get(b.center, j) - Get(hand, j)) * blade >= 0f ? 1f : -1f;   // blade on the same side
                    // a proper rotation: the third axis follows from the other two
                    float sl = Mathf.Sign(Vector3.Dot(Vector3.Cross(Axis(j) * sj, Axis(k) * s), Axis(l)));
                    float scale = 2f * Get(e, k) / Mathf.Max(len, 1e-4f) * fit.Item1 * Plugin.ModelLengthScale.Value;
                    float gripEnd = Get(b.center, k) - s * Get(e, k) - s * fit.Item2 * 2f * Get(e, k);
                    var map = new Matrix4x4();
                    map.SetColumn(0, Axis(j) * sj * scale);
                    map.SetColumn(1, Axis(k) * s * scale);
                    map.SetColumn(2, Axis(l) * sl * scale);
                    var origin = Axis(k) * gripEnd + Axis(j) * Get(hand, j) + Axis(l) * Get(hand, l)
                                 - (Vector3)(map * new Vector4(gx, y0, gz, 0f));
                    map.SetColumn(3, new Vector4(origin.x, origin.y, origin.z, 1f));
                    ours = new Mesh { name = model + "_lw" };
                    ours.vertices = d.Pos.Select(p => map.MultiplyPoint3x4(p)).ToArray();
                    ours.normals = d.Nrm.Select(n => map.MultiplyVector(n).normalized).ToArray();
                    ours.uv = d.Uv;
                    ours.triangles = d.Idx;
                    ours.RecalculateBounds();
                    ours.RecalculateTangents();
                    done[orig] = ours;
                    if (attach != null && f.transform.IsChildOf(attach))
                    {
                        Heads[item.ItemPrefab.name] = attach.InverseTransformDirection(f.transform.TransformDirection(Axis(k) * s)).normalized;
                        iconHead = root.InverseTransformDirection(f.transform.TransformDirection(Axis(k) * s));
                        iconSide = root.InverseTransformDirection(f.transform.TransformDirection(Axis(j) * sj));
                    }
                    Plugin.Log.LogInfo(item.ItemPrefab.name + ": " + orig.name + " (extents " + e.ToString("F3") + ", hand " +
                                       hand.ToString("F3") + ") -> " + model + " along " + "xyz"[k] + (s > 0 ? "+" : "-") +
                                       ", blade " + "xyz"[j] + (sj > 0 ? "+" : "-") + ", scale " + scale.ToString("F3"));
                }
                f.sharedMesh = ours;
                var r = f.GetComponent<MeshRenderer>();
                var src = r.sharedMaterial;
                if (src != null)
                {
                    var m = new Material(src) { name = src.name + "_" + model };
                    if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", d.Tex);
                    if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
                    if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", FlatNormal);      // the original's normals had other UVs
                    if (m.HasProperty("_MetallicGlossMap")) m.SetTexture("_MetallicGlossMap", null);
                    if (m.HasProperty("_EmissionMap")) m.SetTexture("_EmissionMap", d.Emit);
                    if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", d.Emit != null ? new Color(1.6f, 1.1f, 0.8f) : Color.black);
                    if (d.Emit != null) m.EnableKeyword("_EMISSION");
                    bool horn = model == "horn";                       // bone and horn, not polished metal
                    if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", horn ? 0f : 0.25f);
                    if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", horn ? 0.2f : 0.35f);
                    r.sharedMaterials = new[] { m };
                }
            }
            // the inventory icon, from the new model, on the diagonal with its element's halo
            Icons.Make(item, iconHead, iconSide);
            return true;
        }
    }
}
