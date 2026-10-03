using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Wildlife
{
    /// <summary>A generated model (tools/build_models.py): y up, facing +z, the animal's left towards -x.</summary>
    internal class ModelData
    {
        public Vector3[] Pos;
        public Vector3[] Nrm;
        public Vector2[] Uv;
        public int[] Idx;
        public Texture2D Tex;
        public Bounds Bounds;

        public static ModelData Load(string name)
        {
            string dir = Path.Combine(Look.PluginDir, "models");
            string tam = Path.Combine(dir, name + ".tam"), png = Path.Combine(dir, name + ".png");
            if (!File.Exists(tam) || !File.Exists(png))
            {
                Plugin.Log.LogWarning("Model missing: " + tam);
                return null;
            }
            var d = new ModelData();
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
            d.Bounds = new Bounds(d.Pos[0], Vector3.zero);
            foreach (var p in d.Pos) d.Bounds.Encapsulate(p);
            return d;
        }

        /// <summary>Normalised position in the model's bounds (0..1 per axis).</summary>
        public Vector3 Norm(int i)
        {
            Vector3 p = Pos[i] - Bounds.min, s = Bounds.size;
            return new Vector3(p.x / s.x, p.y / s.y, p.z / s.z);
        }
    }

    /// <summary>
    /// Puts generated models on the game's skeletons: aligns them to the base mesh's bind pose, then skins every
    /// vertex to the bones of its body region (legs, spine, head, ears, tail, wings) by nearest bone segment.
    /// The base creature keeps its animations; only the geometry and texture are ours.
    /// </summary>
    internal static class Models
    {
        // ------------------------------------------------------------ helpers

        private static Texture2D s_flatNormal;

        private static Texture2D FlatNormal
        {
            get
            {
                if (s_flatNormal == null)
                {
                    s_flatNormal = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "flat_normal" };
                    var px = new Color[16];
                    for (int i = 0; i < px.Length; i++) px[i] = new Color(0.5f, 0.5f, 1f, 0.5f);
                    s_flatNormal.SetPixels(px);
                    s_flatNormal.Apply(false, true);
                }
                return s_flatNormal;
            }
        }

        public static void UseTexture(Renderer r, Texture2D tex, string owner)
        {
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null)
                    continue;
                var m = new Material(mats[i]) { name = mats[i].name + "_" + owner };
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
                // The base normal map has other UVs. Null would bind a white texture (broken normals, glare and
                // bloom), so use a flat normal: works for both RGB and DXT5nm (AG) unpacking.
                if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", FlatNormal);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
                mats[i] = m;
            }
            r.sharedMaterials = mats;
        }

        private static Mesh NewMesh(ModelData d, Vector3[] pos, Vector3[] nrm, string name)
        {
            var mesh = new Mesh { name = name };
            if (pos.Length > 65000)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = pos;
            mesh.normals = nrm;
            mesh.uv = d.Uv;
            mesh.triangles = d.Idx;
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>Rotation taking frame (f1, u1, l1) onto (f2, u2, l2); both orthonormal.</summary>
        private static Matrix4x4 Rotation(Vector3 f1, Vector3 u1, Vector3 l1, Vector3 f2, Vector3 u2, Vector3 l2)
        {
            var a = new Matrix4x4(f1, u1, l1, new Vector4(0, 0, 0, 1));
            var b = new Matrix4x4(f2, u2, l2, new Vector4(0, 0, 0, 1));
            return b * a.transpose;
        }

        private static void Transform(ModelData d, Matrix4x4 rot, float scale, Vector3 srcOrigin, Vector3 dstOrigin,
            out Vector3[] pos, out Vector3[] nrm)
        {
            pos = new Vector3[d.Pos.Length];
            nrm = new Vector3[d.Pos.Length];
            for (int i = 0; i < pos.Length; i++)
            {
                pos[i] = dstOrigin + scale * rot.MultiplyVector(d.Pos[i] - srcOrigin);
                nrm[i] = rot.MultiplyVector(d.Nrm[i]).normalized;
            }
        }

        private static Vector3 Principal(List<Vector3> pts, Vector3 c)
        {
            float xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var p in pts)
            {
                Vector3 q = p - c;
                xx += q.x * q.x; xy += q.x * q.y; xz += q.x * q.z; yy += q.y * q.y; yz += q.y * q.z; zz += q.z * q.z;
            }
            Vector3 v = new Vector3(1f, 0.7f, 0.3f);
            for (int it = 0; it < 50; it++)
                v = new Vector3(xx * v.x + xy * v.y + xz * v.z, xy * v.x + yy * v.y + yz * v.z, xz * v.x + yz * v.y + zz * v.z).normalized;
            return v;
        }

        private static Vector3 Mean(IEnumerable<Vector3> pts)
        {
            Vector3 s = Vector3.zero;
            int n = 0;
            foreach (var p in pts) { s += p; n++; }
            return n > 0 ? s / n : Vector3.zero;
        }

        // ------------------------------------------------------------ bones

        private class Skeleton
        {
            public readonly Dictionary<string, int> Index = new Dictionary<string, int>();
            public Vector3[] Start, End;

            public Skeleton(SkinnedMeshRenderer smr)
            {
                var bones = smr.bones;
                var bind = smr.sharedMesh.bindposes;
                Start = new Vector3[bones.Length];
                End = new Vector3[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i] == null)
                        continue;
                    Index[bones[i].name] = i;
                    var inv = bind[i].inverse;
                    Start[i] = inv.MultiplyPoint3x4(Vector3.zero);
                    End[i] = bones[i].childCount > 0 ? inv.MultiplyPoint3x4(bones[i].GetChild(0).localPosition) : Start[i];
                }
            }

            public Vector3 Pos(string name)
            {
                return Index.TryGetValue(name, out var i) ? Start[i] : Vector3.zero;
            }

            public bool Has(string name)
            {
                return Index.ContainsKey(name);
            }

            public int[] Find(Func<string, bool> pred)
            {
                return Index.Where(kv => pred(kv.Key)).Select(kv => kv.Value).ToArray();
            }

            public float Distance(int bone, Vector3 p)
            {
                Vector3 a = Start[bone], b = End[bone], ab = b - a;
                float t = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude) : 0f;
                return Vector3.Distance(p, a + t * ab);
            }
        }

        /// <summary>Two nearest allowed bones by segment distance, inverse-square weights.</summary>
        private static BoneWeight[] Skin(Skeleton sk, Vector3[] pos, Func<int, int[]> allowed, int[] fallback)
        {
            var w = new BoneWeight[pos.Length];
            for (int i = 0; i < pos.Length; i++)
            {
                var set = allowed(i);
                if (set == null || set.Length == 0)
                    set = fallback;
                int b0 = set[0], b1 = set[0];
                float d0 = float.MaxValue, d1 = float.MaxValue;
                foreach (int b in set)
                {
                    float d = sk.Distance(b, pos[i]);
                    if (d < d0) { d1 = d0; b1 = b0; d0 = d; b0 = b; }
                    else if (d < d1) { d1 = d; b1 = b; }
                }
                float w0 = 1f / (d0 * d0 + 1e-8f), w1 = (b1 == b0 || d1 > 2.5f * d0) ? 0f : 1f / (d1 * d1 + 1e-8f);
                float sum = w0 + w1;
                w[i] = new BoneWeight { boneIndex0 = b0, weight0 = w0 / sum, boneIndex1 = b1, weight1 = w1 / sum };
            }
            return w;
        }

        private static void Apply(SkinnedMeshRenderer smr, ModelData d, Vector3[] pos, Vector3[] nrm, BoneWeight[] weights, string name)
        {
            var mesh = NewMesh(d, pos, nrm, name);
            mesh.boneWeights = weights;
            mesh.bindposes = smr.sharedMesh.bindposes;
            mesh.RecalculateBounds();
            smr.sharedMesh = mesh;
            var b = mesh.bounds;
            b.Expand(b.size.magnitude * 0.5f);   // room for animation
            smr.localBounds = b;
        }

        // -------------------------------------------------------------- owl

        /// <summary>Static perched owl in place of the sitting crow (y up, beak towards -z).</summary>
        public static bool OwlSitting(MeshFilter mf, ModelData d)
        {
            if (mf.sharedMesh == null)
                return false;
            Bounds t = mf.sharedMesh.bounds;
            // the model faces +z; the crow faces -z: turn it around
            var rot = Rotation(Vector3.forward, Vector3.up, Vector3.left, Vector3.back, Vector3.up, Vector3.right);
            float scale = t.size.y / d.Bounds.size.y;
            Vector3 src = new Vector3(d.Bounds.center.x, d.Bounds.min.y, d.Bounds.center.z);
            Vector3 dst = new Vector3(t.center.x, t.min.y, t.center.z);
            Transform(d, rot, scale, src, dst, out var pos, out var nrm);
            mf.sharedMesh = NewMesh(d, pos, nrm, "owl_perched");
            return true;
        }

        /// <summary>
        /// Flying owl on the crow's flying skeleton. Target frame from the crow mesh: wing axis from the wing bones,
        /// body axis by PCA of the body, head at the narrower end (the tail fans out). The model's body axis is
        /// tilted (it was generated hovering), so it is re-levelled onto the crow's.
        /// </summary>
        public static bool OwlFlying(SkinnedMeshRenderer smr, ModelData d, bool flipForward)
        {
            var src = smr.sharedMesh;
            if (src == null || !src.isReadable)
                return false;
            var sk = new Skeleton(smr);
            var lWing = sk.Find(n => n.StartsWith("l_wing"));
            var rWing = sk.Find(n => n.StartsWith("r_wing"));
            if (lWing.Length == 0 || rWing.Length == 0)
            {
                Plugin.Log.LogWarning("Owl: wing bones not found (" + string.Join(", ", sk.Index.Keys) + ")");
                return false;
            }
            Vector3 lTip = Mean(lWing.Select(i => sk.End[i])), rTip = Mean(rWing.Select(i => sk.End[i]));
            Vector3 left = (lTip - rTip).normalized;

            // target body frame from the crow's own vertices
            var cv = src.vertices;
            Vector3 c = Mean(cv);
            float span = cv.Max(v => Vector3.Dot(v - c, left)) - cv.Min(v => Vector3.Dot(v - c, left));
            var body = cv.Where(v => Mathf.Abs(Vector3.Dot(v - c, left)) < 0.12f * span).ToList();
            Vector3 bc = Mean(body);
            Vector3 axis = Vector3.ProjectOnPlane(Principal(body, bc), left).normalized;
            float tMin = body.Min(v => Vector3.Dot(v - bc, axis)), tMax = body.Max(v => Vector3.Dot(v - bc, axis));
            float range = tMax - tMin;
            float spreadPlus = body.Where(v => Vector3.Dot(v - bc, axis) > tMax - 0.2f * range).Select(v => Mathf.Abs(Vector3.Dot(v - bc, left))).DefaultIfEmpty(0).Max();
            float spreadMinus = body.Where(v => Vector3.Dot(v - bc, axis) < tMin + 0.2f * range).Select(v => Mathf.Abs(Vector3.Dot(v - bc, left))).DefaultIfEmpty(0).Max();
            Vector3 fwd = spreadPlus < spreadMinus ? axis : -axis;   // head = narrower end
            if (flipForward)
                fwd = -fwd;
            Vector3 up = Vector3.Cross(left, fwd).normalized;

            // source frame: left is -x; body axis by PCA, head up
            Vector3 sLeft = Vector3.left;
            float sSpan = d.Bounds.size.x;
            var sBody = d.Pos.Where(p => Mathf.Abs(p.x - d.Bounds.center.x) < 0.12f * sSpan).ToList();
            Vector3 sbc = Mean(sBody);
            Vector3 sFwd = Vector3.ProjectOnPlane(Principal(sBody, sbc), sLeft).normalized;
            if (sFwd.y < 0f)
                sFwd = -sFwd;
            Vector3 sUp = Vector3.Cross(sLeft, sFwd).normalized;

            var rot = Rotation(sFwd, sUp, sLeft, fwd, up, left);
            float scale = span / sSpan;
            Transform(d, rot, scale, sbc, bc, out var pos, out var nrm);

            // wings on the wing bones of their side, body on the others
            var bodyBones = sk.Find(n => !n.Contains("wing"));
            float half = 0.14f * span;
            var weights = Skin(sk, pos, i =>
            {
                float s = Vector3.Dot(pos[i] - bc, left);
                if (s > half) return lWing;
                if (s < -half) return rWing;
                return bodyBones.Length > 0 ? bodyBones : null;
            }, sk.Index.Values.ToArray());
            Apply(smr, d, pos, nrm, weights, "owl_flying");
            Plugin.Log.LogInfo("Owl flying skinned on " + sk.Index.Count + " bones; fwd " + fwd + ", up " + up + ", left " + left + ", scale " + scale.ToString("F4"));
            return true;
        }

        // ------------------------------------------------------------ mouse

        /// <summary>
        /// Mouse on the hare skeleton. Target frame from the feet (up: normal of the feet plane, forward: back to front feet), scaled
        /// so the mouse's body (tail base to head) matches the hare's, feet on the hare's feet. Vertices are
        /// skinned by region of the mouse model: tail, ears, head, each of the four legs, spine.
        /// </summary>
        public static bool MouseOnHare(SkinnedMeshRenderer smr, ModelData d, float scaleMul = 1f, float lift = 0f,
            float forward = 0f, float pitch = 0f)
        {
            if (smr.sharedMesh == null)
                return false;
            var sk = new Skeleton(smr);
            string[] needed = { "Hips", "Head", "Tail", "BackFoot.l", "BackFoot.r", "FrontFoot.l", "FrontFoot.r" };
            var missing = needed.Where(n => !sk.Has(n)).ToArray();
            if (missing.Length > 0)
            {
                Plugin.Log.LogWarning("Mouse: hare bones missing: " + string.Join(", ", missing));
                return false;
            }
            // The hare's bind pose is hunched (hips behind the feet): take "up" from the plane of the four feet.
            Vector3 feet = (sk.Pos("BackFoot.l") + sk.Pos("BackFoot.r") + sk.Pos("FrontFoot.l") + sk.Pos("FrontFoot.r")) / 4f;
            Vector3 front = (sk.Pos("FrontFoot.l") + sk.Pos("FrontFoot.r")) / 2f, back = (sk.Pos("BackFoot.l") + sk.Pos("BackFoot.r")) / 2f;
            Vector3 across = (sk.Pos("FrontFoot.l") + sk.Pos("BackFoot.l")) / 2f - (sk.Pos("FrontFoot.r") + sk.Pos("BackFoot.r")) / 2f;
            Vector3 up = Vector3.Cross((front - back).normalized, across).normalized;
            if (Vector3.Dot(sk.Pos("Hips") - feet, up) < 0f)
                up = -up;
            Vector3 fwd = Vector3.ProjectOnPlane(front - back, up).normalized;
            Vector3 left = Vector3.Cross(fwd, up).normalized;
            float hareLength = Vector3.Dot(sk.Pos("Head") - sk.Pos("Tail"), fwd);
            float ground = new[] { "BackFoot.l", "BackFoot.r", "FrontFoot.l", "FrontFoot.r" }.Min(n => Vector3.Dot(sk.Pos(n), up));
            Vector3 mid = (sk.Pos("Head") + sk.Pos("Tail")) / 2f;
            Vector3 dst = mid - up * (Vector3.Dot(mid, up) - ground);

            // mouse model (normalised z: 0 tail tip .. 1 nose): tail base ~0.36, head centre ~0.88
            float zLen = d.Bounds.size.z;
            float tailBase = d.Bounds.min.z + 0.36f * zLen, headC = d.Bounds.min.z + 0.88f * zLen;
            float scale = hareLength / (headC - tailBase) * scaleMul;
            // live tuning ([MouseFit]): shifts in units of the hare's body length, pitch around the left axis
            dst += up * (lift * hareLength) + fwd * (forward * hareLength);
            Vector3 src = new Vector3(d.Bounds.center.x, d.Bounds.min.y, (tailBase + headC) / 2f);
            var rot = Matrix4x4.Rotate(Quaternion.AngleAxis(pitch, left)) * Rotation(Vector3.forward, Vector3.up, Vector3.left, fwd, up, left);
            Transform(d, rot, scale, src, dst, out var pos, out var nrm);

            int[] B(params string[] names) => names.Where(sk.Has).Select(n => sk.Index[n]).ToArray();
            var tailRoot = B("Tail", "Hips");
            var tailMid = B("Hips", "Root");
            var tailEnd = B("Root");
            // The hare's head bone nods and turns a lot (and rears up when alert): a mouse head on it dropped
            // its nose and twisted. Neck and upper spine carry the head; only the ears keep their own bones.
            var head = B("Neck", "Spine2");
            var earL = B("Ear.l", "ear1.l");
            var earR = B("Ear.r", "ear1.r");
            var spine = B("Hips", "Spine", "Spine1", "Spine2", "Neck", "Shoulder.l", "Shoulder.r");
            // The hare's lower legs and feet reach far beyond a mouse's short legs; skinning mouse feet to them
            // stretched the legs into long sticks. Mouse legs only follow the thighs / shoulders.
            var backL = B("BackUpperLeg.l", "Hips");
            var backR = B("BackUpperLeg.r", "Hips");
            var frontL = B("FrontUpperLeg.l", "Shoulder.l");
            var frontR = B("FrontUpperLeg.r", "Shoulder.r");

            var weights = Skin(sk, pos, i =>
            {
                Vector3 n = d.Norm(i);
                bool isLeft = d.Pos[i].x < d.Bounds.center.x;   // the model's left is -x
                // The hare's tail bone flicks up and its hips tilt a lot: a long mouse tail on them sticks up
                // or hangs straight down. Base on tail+hips, middle on hips+root, tip on the (steady) root, so
                // the tail trails behind the animal.
                if (n.z < 0.33f) return n.z < 0.12f ? tailEnd : n.z < 0.22f ? tailMid : tailRoot;
                if (n.y > 0.78f && n.z > 0.6f) return isLeft ? earL : earR;
                if (n.z > 0.76f) return head;
                if (n.y < 0.32f) return n.z > 0.6f ? (isLeft ? frontL : frontR) : (isLeft ? backL : backR);
                return spine;
            }, spine);
            Apply(smr, d, pos, nrm, weights, "mouse");
            Plugin.Log.LogInfo("Mouse skinned on hare skeleton (" + sk.Index.Count + " bones), scale " + scale.ToString("F4"));
            return true;
        }
    }
}
