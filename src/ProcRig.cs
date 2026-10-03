using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Wildlife
{
    /// <summary>A creature visual animated in code (no Animator clips); the lab can drive it at a given speed.</summary>
    public interface IProcAnimated
    {
        /// <summary>Pose the rig as if moving at <paramref name="speed"/> m/s, <paramref name="time"/> seconds into the motion.</summary>
        void LabPose(float speed, float time);
        float[] LabSpeeds { get; }
    }

    /// <summary>
    /// Our own skeletons: built from the generated model's geometry (joint = centroid of the vertices of a body
    /// region), skinned to it, and animated procedurally. Visuals live under a "Visual_rig" child, facing +z,
    /// feet (or belly) at y = 0.
    /// </summary>
    internal static class ProcRig
    {
        internal class Bone
        {
            public string Name;
            public int Parent;
            public Vector3 Pos;     // model space
        }

        // ------------------------------------------------------------ build

        /// <summary>
        /// RecalculateTangents, then repair the tangents that came out NaN, zero or parallel to the normal (triangles
        /// with degenerate UVs). The game's shaders apply a normal map, so a bad tangent turns a pixel black: it was
        /// the whale's black fluke.
        /// </summary>
        public static int SafeTangents(Mesh mesh)
        {
            mesh.RecalculateTangents();
            var t = mesh.tangents;
            var n = mesh.normals;
            if (n == null || n.Length != t.Length)
                return 0;
            int fixedCount = 0;
            for (int i = 0; i < t.Length; i++)
            {
                var tv = new Vector3(t[i].x, t[i].y, t[i].z);
                bool bad = float.IsNaN(tv.x) || float.IsNaN(tv.y) || float.IsNaN(tv.z) || float.IsNaN(t[i].w) ||
                           tv.sqrMagnitude < 1e-8f || Vector3.Cross(tv.normalized, n[i]).sqrMagnitude < 1e-4f;
                if (!bad)
                {
                    // keep it, but exactly perpendicular to the normal
                    var o = (tv - n[i] * Vector3.Dot(n[i], tv)).normalized;
                    t[i] = new Vector4(o.x, o.y, o.z, t[i].w < 0f ? -1f : 1f);
                    continue;
                }
                var axis = Mathf.Abs(n[i].y) < 0.9f ? Vector3.up : Vector3.right;
                var p = Vector3.Cross(axis, n[i]).normalized;
                t[i] = new Vector4(p.x, p.y, p.z, 1f);
                fixedCount++;
            }
            mesh.tangents = t;
            return fixedCount;
        }

        /// <summary>The rig's bones by name, found under the "Visual_rig" child (after Instantiate).</summary>
        public static Dictionary<string, Transform> Collect(Transform owner)
        {
            var root = owner.Find("Visual_rig");
            if (root == null)
                return null;
            var d = new Dictionary<string, Transform>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t != root)
                    d[t.name] = t;
            return d;
        }

        /// <summary>Creates the visual: bone hierarchy + SkinnedMeshRenderer. Returns the bone transforms by name.</summary>
        public static Dictionary<string, Transform> Build(GameObject owner, ModelData d, List<Bone> bones, Func<int, int[]> allowed,
            Material template, float size, string name, Func<int, BoneWeight> weights = null, float glossiness = 0.3f)
        {
            var root = new GameObject("Visual_rig").transform;
            root.SetParent(owner.transform, false);
            float s = size / d.Bounds.size.z;                  // model length -> metres (before the creature's scale)
            Vector3 origin = new Vector3(d.Bounds.center.x, d.Bounds.min.y, d.Bounds.center.z);
            root.localScale = Vector3.one * s;
            root.localPosition = Vector3.zero;

            var t = new Transform[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                t[i] = new GameObject(bones[i].Name).transform;
                t[i].SetParent(bones[i].Parent < 0 ? root : t[bones[i].Parent], false);
            }
            for (int i = 0; i < bones.Count; i++)
            {
                Vector3 parentPos = bones[i].Parent < 0 ? origin : bones[bones[i].Parent].Pos;
                t[i].localPosition = bones[i].Pos - parentPos;
                t[i].localRotation = Quaternion.identity;
            }

            // mesh in root space (model space shifted so the feet sit at y = 0)
            var pos = d.Pos.Select(p => p - origin).ToArray();
            var mesh = new Mesh { name = name };
            mesh.vertices = pos;
            mesh.normals = d.Nrm;
            mesh.uv = d.Uv;
            mesh.triangles = d.Idx;
            mesh.boneWeights = weights != null ? Enumerable.Range(0, d.Pos.Length).Select(weights).ToArray() : Skin(d, bones, allowed);
            mesh.bindposes = bones.Select(b => Matrix4x4.Translate(-(b.Pos - origin))).ToArray();
            mesh.RecalculateBounds();
            int badTangents = SafeTangents(mesh);
            if (badTangents > 0)
                Plugin.Log.LogInfo(name + ": repaired " + badTangents + " tangents");

            var smr = root.gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = t;
            smr.rootBone = t[0];
            var b = mesh.bounds;
            b.Expand(b.size.magnitude);
            smr.localBounds = b;
            smr.updateWhenOffscreen = false;
            var mat = new Material(template) { name = name + "_mat" };
            if (mat.HasProperty("_MainTex")) mat.SetTexture("_MainTex", d.Tex);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", Color.white);
            if (mat.HasProperty("_BumpMap")) mat.SetTexture("_BumpMap", Models.FlatNormalMap);
            if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", Color.black);
            // The template's gloss map belongs to the hare's UVs: on our models it scattered shiny patches that
            // mirrored a dark sky (the whale's "stains" and black fluke). Uniform gloss instead. And the shader
            // draws both faces (_Cull 0): let back faces use flipped normals, or thin fins go black from behind.
            if (mat.HasProperty("_MetallicGlossMap")) mat.SetTexture("_MetallicGlossMap", null);
            if (mat.HasProperty("_UseGlossmap")) mat.SetFloat("_UseGlossmap", 0f);
            mat.DisableKeyword("_USEGLOSSMAP_ON");
            if (mat.HasProperty("_Glossiness")) mat.SetFloat("_Glossiness", glossiness);
            if (mat.HasProperty("_Metallic")) mat.SetFloat("_Metallic", 0f);
            if (mat.HasProperty("_TwoSidedNormals")) mat.SetFloat("_TwoSidedNormals", 1f);
            smr.sharedMaterial = mat;
            if (!s_materialLogged)
            {
                s_materialLogged = true;
                var sb = new System.Text.StringBuilder("Rig material from " + template.name + " (" + mat.shader.name + "): textures");
                foreach (var prop in mat.GetTexturePropertyNames())
                {
                    var tx = mat.GetTexture(prop);
                    sb.Append(" " + prop + "=" + (tx != null ? tx.name : "null"));
                }
                sb.Append("; keywords " + string.Join(",", mat.shaderKeywords));
                for (int i = 0; i < mat.shader.GetPropertyCount(); i++)
                    if (mat.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Float || mat.shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Range)
                        sb.Append(" " + mat.shader.GetPropertyName(i) + "=" + mat.GetFloat(mat.shader.GetPropertyName(i)).ToString("0.##"));
                Plugin.Log.LogInfo(sb.ToString());
            }

            var dict = new Dictionary<string, Transform>();
            for (int i = 0; i < bones.Count; i++) dict[bones[i].Name] = t[i];
            return dict;
        }

        private static bool s_materialLogged;

        /// <summary>Two nearest allowed bones (segment to first child, or the joint itself), inverse-square weights.</summary>
        private static BoneWeight[] Skin(ModelData d, List<Bone> bones, Func<int, int[]> allowed)
        {
            var child = new int[bones.Count];
            for (int i = 0; i < bones.Count; i++) child[i] = -1;
            for (int i = bones.Count - 1; i >= 0; i--)
                if (bones[i].Parent >= 0) child[bones[i].Parent] = i;
            var all = Enumerable.Range(0, bones.Count).ToArray();
            var w = new BoneWeight[d.Pos.Length];
            for (int v = 0; v < d.Pos.Length; v++)
            {
                var set = allowed(v);
                if (set == null || set.Length == 0) set = all;
                int b0 = set[0], b1 = set[0];
                float d0 = float.MaxValue, d1 = float.MaxValue;
                foreach (int b in set)
                {
                    Vector3 a = bones[b].Pos, e = child[b] >= 0 ? bones[child[b]].Pos : a, ab = e - a;
                    float tt = ab.sqrMagnitude > 1e-12f ? Mathf.Clamp01(Vector3.Dot(d.Pos[v] - a, ab) / ab.sqrMagnitude) : 0f;
                    float dist = Vector3.Distance(d.Pos[v], a + tt * ab);
                    if (dist < d0) { d1 = d0; b1 = b0; d0 = dist; b0 = b; }
                    else if (dist < d1) { d1 = dist; b1 = b; }
                }
                float w0 = 1f / (d0 * d0 + 1e-8f), w1 = (b1 == b0 || d1 > 2f * d0) ? 0f : 1f / (d1 * d1 + 1e-8f);
                float sum = w0 + w1;
                w[v] = new BoneWeight { boneIndex0 = b0, weight0 = w0 / sum, boneIndex1 = b1, weight1 = w1 / sum };
            }
            return w;
        }

        private static Vector3 Centroid(ModelData d, Func<Vector3, bool> pick, Vector3 fallback)
        {
            Vector3 s = Vector3.zero;
            int n = 0;
            for (int i = 0; i < d.Pos.Length; i++)
                if (pick(d.Norm(i))) { s += d.Pos[i]; n++; }
            return n > 0 ? s / n : fallback;
        }

        private static Vector3 At(ModelData d, float x, float y, float z)
        {
            return d.Bounds.min + Vector3.Scale(d.Bounds.size, new Vector3(x, y, z));
        }

        // ------------------------------------------------------- quadruped

        /// <summary>Mouse skeleton from the mouse model's regions (y up, facing +z, left towards -x).</summary>
        public static List<Bone> MouseBones(ModelData d, out Func<int, int[]> allowed)
        {
            var bones = new List<Bone>();
            int Add(string name, int parent, Vector3 p) { bones.Add(new Bone { Name = name, Parent = parent, Pos = p }); return bones.Count - 1; }
            bool Left(Vector3 n) => n.x < 0.5f;
            bool Leg(Vector3 n) => n.y < 0.32f && n.z >= 0.33f;
            bool Tail(Vector3 n) => n.z < 0.33f;
            bool Ear(Vector3 n) => n.y > 0.78f && n.z > 0.6f;
            bool Head(Vector3 n) => n.z > 0.76f && !Ear(n) && !Leg(n);
            bool Body(Vector3 n) => !Leg(n) && !Tail(n) && !Ear(n);

            int root = Add("Root", -1, Centroid(d, Body, At(d, 0.5f, 0.5f, 0.55f)));
            int hips = Add("Hips", root, Centroid(d, n => Body(n) && n.z > 0.33f && n.z < 0.48f, At(d, 0.5f, 0.5f, 0.42f)));
            int chest = Add("Chest", hips, Centroid(d, n => Body(n) && n.z > 0.56f && n.z < 0.7f, At(d, 0.5f, 0.5f, 0.64f)));
            int neck = Add("Neck", chest, Centroid(d, n => Body(n) && n.z > 0.7f && n.z < 0.78f, At(d, 0.5f, 0.55f, 0.75f)));
            int head = Add("Head", neck, Centroid(d, Head, At(d, 0.5f, 0.6f, 0.86f)));
            Add("Nose", head, Centroid(d, n => n.z > 0.96f, At(d, 0.5f, 0.5f, 1f)));
            foreach (var side in new[] { "L", "R" })
            {
                bool isL = side == "L";
                bool E(Vector3 n) => Ear(n) && Left(n) == isL;
                float earTop = 0f;
                for (int i = 0; i < d.Pos.Length; i++) { var n = d.Norm(i); if (E(n)) earTop = Mathf.Max(earTop, n.y); }
                int ear = Add("Ear" + side, head, Centroid(d, n => E(n) && n.y < 0.78f + (earTop - 0.78f) * 0.35f, At(d, isL ? 0.3f : 0.7f, 0.8f, 0.75f)));
                Add("EarTip" + side, ear, Centroid(d, n => E(n) && n.y > earTop - (earTop - 0.78f) * 0.3f, At(d, isL ? 0.25f : 0.75f, 0.95f, 0.75f)));
                foreach (var end in new[] { "Front", "Back" })
                {
                    bool front = end == "Front";
                    bool L(Vector3 n) => Leg(n) && (n.z > 0.6f) == front && Left(n) == isL;
                    var parent = front ? chest : hips;
                    int up = Add(end + "Leg" + side, parent, Centroid(d, n => L(n) && n.y > 0.22f, At(d, isL ? 0.35f : 0.65f, 0.3f, front ? 0.68f : 0.42f)));
                    int low = Add(end + "Knee" + side, up, Centroid(d, n => L(n) && n.y > 0.09f && n.y <= 0.2f, At(d, isL ? 0.35f : 0.65f, 0.15f, front ? 0.68f : 0.42f)));
                    Add(end + "Foot" + side, low, Centroid(d, n => L(n) && n.y <= 0.07f, At(d, isL ? 0.35f : 0.65f, 0.02f, front ? 0.7f : 0.44f)));
                }
            }
            int prev = hips;
            float[] tz = { 0.33f, 0.24f, 0.15f, 0.07f, 0.0f };
            for (int k = 0; k < 5; k++)
            {
                float z0 = tz[k];
                prev = Add(k < 4 ? "Tail" + (k + 1) : "TailTip", prev, Centroid(d, n => Tail(n) && Mathf.Abs(n.z - z0) < 0.03f, At(d, 0.5f, 0.3f, z0)));
            }

            int B(string n) => bones.FindIndex(b => b.Name == n);
            int[] S(params string[] names) => names.Select(B).Where(i => i >= 0).ToArray();
            var sets = new Dictionary<string, int[]>
            {
                { "tail", S("Hips", "Tail1", "Tail2", "Tail3", "Tail4") },
                { "earL", S("EarL", "Head") }, { "earR", S("EarR", "Head") },
                { "head", S("Head", "Neck") },
                { "body", S("Root", "Hips", "Chest", "Neck") },
            };
            foreach (var side in new[] { "L", "R" })
                foreach (var end in new[] { "Front", "Back" })
                    sets[end + side] = S(end + "Leg" + side, end + "Knee" + side, end == "Front" ? "Chest" : "Hips");
            allowed = v =>
            {
                Vector3 n = d.Norm(v);
                if (Tail(n)) return sets["tail"];
                if (Ear(n)) return Left(n) ? sets["earL"] : sets["earR"];
                if (Head(n)) return sets["head"];
                if (Leg(n)) return sets[(n.z > 0.6f ? "Front" : "Back") + (Left(n) ? "L" : "R")];
                return sets["body"];
            };
            return bones;
        }

        // --------------------------------------------------------- swimmer

        /// <summary>Spine of 8 joints head to fluke (centroids of slices along z), plus the two pectoral fins.</summary>
        /// <summary>Normalised z (0 = tail tip) of the narrowest point of the tail stock, between z 0.05 and 0.3.</summary>
        public static float Peduncle(ModelData d)
        {
            float best = 0.1f, bestWidth = float.MaxValue;
            for (float z = 0.05f; z <= 0.3f; z += 0.01f)
            {
                float lo = float.MaxValue, hi = float.MinValue;
                for (int v = 0; v < d.Pos.Length; v++)
                {
                    var n = d.Norm(v);
                    if (Mathf.Abs(n.z - z) > 0.006f) continue;
                    lo = Mathf.Min(lo, n.x);
                    hi = Mathf.Max(hi, n.x);
                }
                if (hi > lo && hi - lo < bestWidth)
                {
                    bestWidth = hi - lo;
                    best = z;
                }
            }
            return best;
        }

        public static List<Bone> SwimmerBones(ModelData d, float finZMin, float finZMax, out Func<int, int[]> allowed)
        {
            return SwimmerBones(d, finZMin, finZMax, out allowed, out _);
        }

        /// <summary>
        /// Same, plus smooth spine weights: each vertex is shared between the two joints that bracket it along the
        /// body (linear in z), so the body bends as one continuous curve instead of rigid segments.
        /// </summary>
        public static List<Bone> SwimmerBones(ModelData d, float finZMin, float finZMax, out Func<int, int[]> allowed,
            out Func<int, BoneWeight> weights)
        {
            var bones = new List<Bone>();
            int Add(string name, int parent, Vector3 p) { bones.Add(new Bone { Name = name, Parent = parent, Pos = p }); return bones.Count - 1; }
            // Cetaceans bend mostly over the rear third and pitch their flukes at the peduncle: joints are packed
            // toward the tail, and the Fluke joint sits at the peduncle (the narrowest point before the flukes),
            // so the flukes turn as one rigid hydrofoil.
            float[] along = { 0.10f, 0.30f, 0.48f, 0.62f, 0.73f, 0.82f, 0.90f, 0.96f };   // fractions of head..peduncle
            int N = along.Length;
            float halfWidth = 0.5f;
            bool Fin(Vector3 n) => n.z > finZMin && n.z < finZMax && Mathf.Abs(n.x - 0.5f) > 0.28f && n.y < 0.6f;
            float peduncle = Peduncle(d);
            var spine = new int[N];
            var spineZ = new float[N];
            int prev = -1;
            for (int i = 0; i < N; i++)
            {
                float z = 1f - along[i] * (1f - peduncle);   // head first
                spineZ[i] = z;
                prev = spine[i] = Add("Spine" + i, prev, Centroid(d, n => !Fin(n) && Mathf.Abs(n.z - z) < 0.03f, At(d, 0.5f, 0.5f, z)));
            }
            Add("Fluke", prev, Centroid(d, n => Mathf.Abs(n.z - peduncle) < 0.02f, At(d, 0.5f, 0.5f, peduncle)));
            int Near(float z)
            {
                int best = 0;
                for (int i = 1; i < N; i++)
                    if (Mathf.Abs(spineZ[i] - z) < Mathf.Abs(spineZ[best] - z)) best = i;
                return spine[best];
            }
            float finZ = (finZMin + finZMax) / 2f;
            foreach (var side in new[] { "L", "R" })
            {
                bool isL = side == "L";
                bool F(Vector3 n) => Fin(n) && (n.x < 0.5f) == isL;
                int root = Add("Fin" + side, Near(finZ), Centroid(d, n => F(n) && Mathf.Abs(n.x - 0.5f) < 0.36f, At(d, isL ? 0.3f : 0.7f, 0.35f, finZ)));
                Add("FinTip" + side, root, Centroid(d, n => F(n) && Mathf.Abs(n.x - 0.5f) > 0.42f, At(d, isL ? 0.05f : 0.95f, 0.25f, finZ - 0.05f)));
            }
            _ = halfWidth;
            int finL = bones.FindIndex(b => b.Name == "FinL"), finR = bones.FindIndex(b => b.Name == "FinR");
            int fluke = bones.FindIndex(b => b.Name == "Fluke");
            var spineSet = spine.Concat(new[] { fluke }).ToArray();
            allowed = v =>
            {
                Vector3 n = d.Norm(v);
                if (Fin(n)) return new[] { n.x < 0.5f ? finL : finR, Near(n.z) };
                return spineSet;
            };
            // chain head -> tail with each joint's normalised z (decreasing)
            var chain = spineSet;
            var cz = chain.Select(i => (bones[i].Pos.z - d.Bounds.min.z) / d.Bounds.size.z).ToArray();
            // smooth skin: each vertex shared by up to 4 joints (tent kernels 1.6 joint spacings wide), so a bent tail
            // keeps its volume instead of creasing on the inside of the bend; the flukes stay rigid on their joint
            BoneWeight Along(float z)
            {
                int last = chain.Length - 1;
                if (z <= cz[last]) return new BoneWeight { boneIndex0 = chain[last], weight0 = 1f };
                if (z >= cz[0]) return new BoneWeight { boneIndex0 = chain[0], weight0 = 1f };
                var w = new List<KeyValuePair<int, float>>();
                for (int i = 0; i <= last; i++)
                {
                    float h = 0.5f * ((i > 0 ? cz[i - 1] - cz[i] : cz[i] - cz[i + 1]) + (i < last ? cz[i] - cz[i + 1] : cz[i - 1] - cz[i]));
                    float k = 1f - Mathf.Abs(z - cz[i]) / (1.6f * h);
                    if (k > 0f) w.Add(new KeyValuePair<int, float>(chain[i], k));
                }
                w.Sort((a, b) => b.Value.CompareTo(a.Value));
                if (w.Count > 4) w.RemoveRange(4, w.Count - 4);
                float sum = w.Sum(p => p.Value);
                var bw = new BoneWeight();
                for (int i = 0; i < w.Count; i++)
                {
                    float v = w[i].Value / sum;
                    if (i == 0) { bw.boneIndex0 = w[i].Key; bw.weight0 = v; }
                    else if (i == 1) { bw.boneIndex1 = w[i].Key; bw.weight1 = v; }
                    else if (i == 2) { bw.boneIndex2 = w[i].Key; bw.weight2 = v; }
                    else { bw.boneIndex3 = w[i].Key; bw.weight3 = v; }
                }
                return bw;
            }
            weights = v =>
            {
                Vector3 n = d.Norm(v);
                var w = Along(n.z);
                if (!Fin(n))
                    return w;
                // fins: the fin bone, blended with the body near the root
                float out_ = Mathf.InverseLerp(0.28f, 0.45f, Mathf.Abs(n.x - 0.5f));
                return new BoneWeight { boneIndex0 = n.x < 0.5f ? finL : finR, weight0 = 0.4f + 0.6f * out_, boneIndex1 = w.boneIndex0, weight1 = 0.6f - 0.6f * out_ };
            };
            return bones;
        }
    }

    // ===================================================================== animators

    /// <summary>
    /// Procedural quadruped gait for the mouse: trot when slow (diagonal pairs), bounding gallop when fast (front
    /// pair then back pair), body bob, spine flex, level head with sniffing and ear twitches when still, tail
    /// trailing with lag. Speed comes from the creature's actual movement, so it also works on other clients.
    /// </summary>
    public class ProcQuadruped : MonoBehaviour, IProcAnimated
    {
        private Dictionary<string, Transform> _b;
        private readonly Dictionary<Transform, Vector3> _restPos = new Dictionary<Transform, Vector3>();
        private Vector3 _lastPos;
        private float _speed, _phase, _sniff, _twitchL, _twitchR, _time;
        private readonly float[] _tailLag = new float[5];
        private bool _lab;
        private float _labSpeed;

        public float[] LabSpeeds => new[] { 0f, 1.6f, 6.5f };

        internal void Init(Dictionary<string, Transform> bones)
        {
            _b = bones;
            _restPos.Clear();
            foreach (var t in bones.Values) _restPos[t] = t.localPosition;
            _lastPos = transform.position;
        }

        private void Awake()
        {
            var bones = ProcRig.Collect(transform);
            if (bones != null)
                Init(bones);
        }

        public void LabPose(float speed, float time)
        {
            _lab = true;
            _labSpeed = speed;
            _speed = speed;
            _phase = 0f;
            _time = 0f;
            const float step = 1f / 60f;
            for (float t = 0f; t < time; t += step) Animate(step);
            Animate(0f);
        }

        private void LateUpdate()
        {
            if (_b == null || _lab)
                return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 p = transform.position;
            Vector3 v = (p - _lastPos) / dt;
            _lastPos = p;
            float planar = new Vector2(v.x, v.z).magnitude;
            if (planar > 30f) planar = 0f;   // teleport / spawn
            _speed = Mathf.Lerp(_speed, planar, 1f - Mathf.Exp(-dt * 8f));
            Animate(dt);
        }

        private Transform B(string n) => _b.TryGetValue(n, out var t) ? t : null;

        private static void Rot(Transform t, Vector3 euler)
        {
            if (t != null) t.localRotation = Quaternion.Euler(euler);
        }

        private void Animate(float dt)
        {
            _time += dt;
            float speed = _lab ? _labSpeed : _speed;
            bool run = speed > 3.5f;
            float freq = run ? 3.2f + speed * 0.25f : 1.2f + speed * 1.4f;   // steps per second
            _phase += dt * freq * Mathf.PI * 2f;
            float move = Mathf.Clamp01(speed / 1.2f);
            float swing = (run ? 45f : 28f) * move;

            // legs: trot = diagonal pairs; bound = front pair and back pair in turn
            float FL, FR, BL, BR;
            if (run) { FL = 0f; FR = 0.3f; BL = Mathf.PI; BR = Mathf.PI + 0.3f; }
            else { FL = 0f; BR = 0f; FR = Mathf.PI; BL = Mathf.PI; }
            Leg("Front", "L", FL, swing);
            Leg("Front", "R", FR, swing);
            Leg("Back", "L", BL, swing);
            Leg("Back", "R", BR, swing);

            // body: bob and spine flex (strong when bounding)
            var root = B("Root");
            if (root != null && _restPos.TryGetValue(root, out var rp))
                root.localPosition = rp + Vector3.up * (Mathf.Abs(Mathf.Sin(_phase)) * (run ? 0.06f : 0.02f) * move);
            float flex = run ? Mathf.Sin(_phase) * 14f * move : 0f;
            Rot(B("Hips"), new Vector3(-flex * 0.5f, 0f, 0f));
            Rot(B("Chest"), new Vector3(flex, 0f, 0f));

            // head: level while moving; sniffing nods and look-around when still
            if (speed < 0.3f)
            {
                _sniff += dt;
                float nod = Mathf.Sin(_sniff * 14f) * 4f * Mathf.Clamp01(Mathf.Sin(_sniff * 0.7f) * 2f);
                float look = Mathf.Sin(_sniff * 0.45f) * 25f;
                Rot(B("Neck"), new Vector3(-8f, look * 0.4f, 0f));
                Rot(B("Head"), new Vector3(nod - 4f, look * 0.6f, 0f));
            }
            else
            {
                Rot(B("Neck"), new Vector3(-flex * 0.6f, 0f, 0f));
                Rot(B("Head"), new Vector3(-flex * 0.4f + Mathf.Sin(_phase * 2f) * 2f, 0f, 0f));
            }

            // ears: random twitches, flattened when running
            if (!_lab && UnityEngine.Random.value < dt * 0.5f) _twitchL = 1f;
            if (!_lab && UnityEngine.Random.value < dt * 0.5f) _twitchR = 1f;
            _twitchL = Mathf.MoveTowards(_twitchL, 0f, dt * 4f);
            _twitchR = Mathf.MoveTowards(_twitchR, 0f, dt * 4f);
            float back = run ? -25f : 0f;
            Rot(B("EarL"), new Vector3(back - _twitchL * 20f, 0f, _twitchL * 10f));
            Rot(B("EarR"), new Vector3(back - _twitchR * 20f, 0f, -_twitchR * 10f));

            // tail: each segment lags the motion; lifted a little when running
            float sway = Mathf.Sin(_time * 2.1f) * (speed < 0.3f ? 10f : 4f);
            for (int i = 0; i < 4; i++)
            {
                float target = sway * (i + 1) * 0.5f + Mathf.Sin(_phase - i * 0.8f) * 6f * move;
                _tailLag[i] = dt > 0f ? Mathf.Lerp(_tailLag[i], target, 1f - Mathf.Exp(-dt * (8f - i * 1.5f))) : target;
                Rot(B("Tail" + (i + 1)), new Vector3(run ? -6f : 4f, _tailLag[i], 0f));
            }
        }

        private void Leg(string end, string side, float offset, float swing)
        {
            float s = Mathf.Sin(_phase + offset);
            float lift = Mathf.Max(0f, Mathf.Cos(_phase + offset));   // knee bends while the foot travels forward
            Rot(B(end + "Leg" + side), new Vector3(s * swing, 0f, 0f));
            Rot(B(end + "Knee" + side), new Vector3((end == "Front" ? 1f : -1f) * lift * swing * 0.9f, 0f, 0f));
            Rot(B(end + "Foot" + side), new Vector3(-s * swing * 0.4f, 0f, 0f));
        }
    }

    /// <summary>
    /// Procedural swimming for whales and orcas. Each spine joint only adds its share of the body curve (joint
    /// rotations add up down the chain). Layers: the main vertical stroke (cetaceans swim with up-and-down
    /// flukes), a slower horizontal undulation, the body bending into turns, a roll when turning, a freer fluke,
    /// pectoral strokes, and a head-up arch while blowing at the surface.
    /// </summary>
    /// <summary>
    /// Cetacean swimming, driven by the body's centre line rather than by joint angles:
    ///   y(s, t) = A(s) sin(phase - k s),  s = 0 head .. 1 tail tip, y in body lengths,
    /// with A(s) small up front and growing over the rear of the body to TipAmplitude at the flukes (peak-to-peak
    /// fluke travel of 15-25 % of the body length in real cetaceans). Each joint gets the angle that makes its
    /// segment follow the line. The flukes pitch as a hydrofoil at the peduncle: their angle leads the heave by a
    /// quarter stroke (trailing edge up on the downstroke). Beat frequency follows a Strouhal number of about 0.25.
    /// </summary>
    public class ProcSwimmer : MonoBehaviour, IProcAnimated
    {
        public float TipAmplitude = 0.10f;     // half the peak-to-peak heave at the peduncle, in body lengths
        public float FlukePitch = 30f;         // fluke angle to the path at mid-stroke, degrees
        public float WaveNumber = 2.4f;        // phase lag head -> tail, radians
        public float Strouhal = 0.25f;
        public float MinFrequency = 0.15f;     // idle beat, Hz
        public float LengthMeters = 10f;
        public float Horizontal = 4f;          // slow side sway, degrees
        public float RigidFront = 0.3f;        // the front this fraction of the body barely bends
        public float HeadHeave = 0.02f;        // whole-body bob against the tail, in body lengths
        private Transform[] _spine;
        private float[] _s;                    // joint positions along the body (spine joints, then the fluke joint)
        private Transform _finL, _finR, _fluke, _rig;
        private Vector3 _rigBase;
        private Vector3 _lastPos;
        private float _lastYaw, _yawRate, _speed, _phase, _spout;
        private bool _lab;

        public float[] LabSpeeds => new[] { 2.5f, 6f };

        /// <summary>One stroke at that speed, in seconds (the lab samples a whole stroke).</summary>
        public float Cycle(float speed) => 1f / Frequency(speed);

        private float Frequency(float speed) => Mathf.Max(MinFrequency, Strouhal * speed / (2f * TipAmplitude * LengthMeters));

        private void Awake()
        {
            var bones = ProcRig.Collect(transform);
            if (bones != null)
                Init(bones);
        }

        internal void Init(Dictionary<string, Transform> bones)
        {
            var list = new List<Transform>();
            for (int i = 0; bones.TryGetValue("Spine" + i, out var t); i++) list.Add(t);
            _spine = list.ToArray();
            bones.TryGetValue("FinL", out _finL);
            bones.TryGetValue("FinR", out _finR);
            bones.TryGetValue("Fluke", out _fluke);
            _lastPos = transform.position;
            _lastYaw = transform.eulerAngles.y;
            // joint positions along the body, from the bind pose and the mesh's length
            var smr = GetComponentInChildren<SkinnedMeshRenderer>(true);
            var rig = transform.Find("Visual_rig");
            _rig = rig;
            if (rig != null) _rigBase = rig.localPosition;
            if (smr == null || smr.sharedMesh == null || rig == null || _spine.Length == 0)
                return;
            var b = smr.sharedMesh.bounds;
            float zMin = float.MaxValue, zMax = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var c = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float z = rig.InverseTransformPoint(smr.transform.TransformPoint(c)).z;
                zMin = Mathf.Min(zMin, z);
                zMax = Mathf.Max(zMax, z);
            }
            var joints = _fluke != null ? _spine.Concat(new[] { _fluke }).ToArray() : _spine;
            _s = joints.Select(j => Mathf.Clamp01((zMax - rig.InverseTransformPoint(j.position).z) / (zMax - zMin))).ToArray();
        }

        /// <summary>Head-up arch for a couple of seconds while the animal blows.</summary>
        public void Spout()
        {
            _spout = 2.5f;
        }

        public void LabPose(float speed, float time)
        {
            _lab = true;
            _speed = speed;
            _yawRate = speed > 3f ? 12f : 0f;   // show the turn bend in the fast sheet
            _phase = 0f;
            const float step = 1f / 60f;
            for (float t = 0f; t < time; t += step) Animate(step);
            Animate(0f);
        }

        private void LateUpdate()
        {
            if (_spine == null || _spine.Length == 0 || _lab)
                return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 v = (transform.position - _lastPos) / dt;
            _lastPos = transform.position;
            float sp = v.magnitude > 40f ? 0f : v.magnitude;
            _speed = Mathf.Lerp(_speed, sp, 1f - Mathf.Exp(-dt * 3f));
            float yaw = transform.eulerAngles.y;
            float rate = Mathf.DeltaAngle(_lastYaw, yaw) / dt;
            _lastYaw = yaw;
            _yawRate = Mathf.Lerp(_yawRate, Mathf.Clamp(rate, -60f, 60f), 1f - Mathf.Exp(-dt * 2f));
            Animate(dt);
        }

        private float Envelope(float s)
        {
            // a little heave up front, then a steep rise over the rear of the body
            // full amplitude at the peduncle: the flukes beyond it feather along the path instead of following the line
            float end = _s != null && _spine != null && _s.Length > _spine.Length ? _s[_spine.Length] : 1f;
            float r = Mathf.Clamp((s - RigidFront) / Mathf.Max(end - RigidFront, 0.05f), 0f, 1.15f);
            return TipAmplitude * (0.015f + 0.985f * r * r);                     // grows with the square of the distance
        }

        private float Line(float s, float calm) => Envelope(s) * calm * Mathf.Sin(_phase - WaveNumber * s);

        private void Animate(float dt)
        {
            if (_s == null || _s.Length < _spine.Length)
                return;
            _spout = Mathf.Max(0f, _spout - dt);
            float calm = _spout > 0f ? 0.4f : 0.85f + 0.15f * Mathf.Clamp01(_speed / 6f);
            _phase += dt * Frequency(_speed) * Mathf.PI * 2f;
            float turn = Mathf.Clamp(_yawRate * 0.5f, -22f, 22f);         // bend into the turn
            float roll = Mathf.Clamp(-_yawRate * 1.2f, -20f, 20f);        // bank into the turn
            float arch = _spout > 0f ? Mathf.Sin(Mathf.Clamp01((2.5f - _spout) / 2.5f) * Mathf.PI) : 0f;

            int n = _spine.Length;
            bool hasFluke = _fluke != null && _s.Length > n;
            // points along the line: every joint, then the tail tip; segment i runs from point i to point i + 1
            int pts = n + (hasFluke ? 1 : 0) + 1;
            var sp = new float[pts];
            for (int i = 0; i < pts - 1; i++) sp[i] = _s[i];
            sp[pts - 1] = 1f;
            float prevPitch = 0f, prevYaw = 0f;
            for (int i = 0; i < n; i++)
            {
                float ds = Mathf.Max(sp[i + 1] - sp[i], 1e-3f);
                float pitch = Mathf.Atan2(Line(sp[i + 1], calm) - Line(sp[i], calm), ds) * Mathf.Rad2Deg;   // + = tail up
                pitch += arch * (i == 0 ? -10f : i == n - 1 ? 8f : 0f);                                      // blow: head up
                float mid = (sp[i] + sp[i + 1]) / 2f;
                float yaw = Horizontal * calm * mid * mid * Mathf.Sin(_phase * 0.5f - mid * 1.6f) + turn * mid;
                _spine[i].localRotation = Quaternion.Euler(pitch - prevPitch, yaw - prevYaw, i == 0 ? roll : 0f);
                prevPitch = pitch;
                prevYaw = yaw;
            }
            if (hasFluke)
            {
                // hydrofoil feathering: the flukes stay within +-FlukePitch of the swim path (flat at the top and
                // bottom of the stroke, steepest at mid-stroke, trailing edge lagging), so against the tail stock
                // they turn the other way: flatter than the stock, never steeper
                float sf = _s[n];
                float heaveVel = Mathf.Cos(_phase - WaveNumber * sf);
                float flukeToPath = -FlukePitch * calm * heaveVel;
                // spread the turn over the last spine joint (40 %) and the fluke joint (60 %): a curve, not a kink
                float rel = flukeToPath - prevPitch;
                _spine[n - 1].localRotation = Quaternion.Euler(0.4f * rel, 0f, 0f) * _spine[n - 1].localRotation;
                _fluke.localRotation = Quaternion.Euler(0.6f * rel, turn * 0.3f, 0f);
            }
            // the whole body bobs a little against the tail
            if (_rig != null)
                _rig.localPosition = _rigBase + Vector3.up * (-HeadHeave * LengthMeters * calm * Mathf.Sin(_phase - WaveNumber * 0.9f));
            // flippers: small paddle lagging the stroke by ~0.2 cycle, and steering in turns
            float fin = Mathf.Sin(_phase - 1.25f) * 4f;
            float steer = turn * 0.4f;
            if (_finL != null) _finL.localRotation = Quaternion.Euler(0f, 0f, fin - 3f + steer);
            if (_finR != null) _finR.localRotation = Quaternion.Euler(0f, 0f, -fin + 3f + steer);
        }
    }
}
