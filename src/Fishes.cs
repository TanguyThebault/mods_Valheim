using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Livelier vanilla fish without touching their models. Vanilla fish are rigid meshes that just slide.
    /// - Readable meshes (Fish1-3): a 6-joint spine along the body, smooth weights, and a lateral swimming wave
    ///   growing toward the tail (real side-to-side fish swimming).
    /// - Non-readable meshes (the rest; their vertices can't be read in game): the visual node ("attachobj") yaws back
    ///   and forth around a point just behind the head, so the tail sweeps, with a little roll. The prefab
    ///   hierarchy is left untouched: moving meshes under a new pivot made Unity crash natively when
    ///   instantiating Fish12 (three meshes, two inactive) from a save.
    /// Beat frequency and amplitude follow the fish's actual speed.
    /// </summary>
    internal static class Fishes
    {
        public static void Register()
        {
            int rigged = 0, swayed = 0;   // swayed: counted per prefab
            for (int i = 1; i <= 12; i++)
            {
                // Fish assets are soft references: load them (and so their materials, in other bundles) first,
                // otherwise the meshes have no material at the main menu.
                try
                {
                    var sr = Jotunn.Managers.AssetManager.Instance.GetSoftReference<GameObject>("Fish" + i);
                    if (sr.IsValid)
                        sr.Load();
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogWarning("Fish" + i + ": soft reference load failed: " + e.Message);
                }
                var prefab = Jotunn.Managers.PrefabManager.Cache.GetPrefab<GameObject>("Fish" + i);
                if (prefab == null || prefab.GetComponent<Fish>() == null)
                    continue;
                bool any = false, sway = false;
                foreach (var mf in prefab.GetComponentsInChildren<MeshFilter>(true))
                {
                    var mr = mf.GetComponent<MeshRenderer>();
                    if (mf.sharedMesh == null || mr == null)
                        continue;
                    if (mf.sharedMesh.isReadable && Rig(prefab, mf, mr))
                        rigged++;
                    else
                        sway = true;
                    any = true;
                }
                if (sway)
                    swayed++;
                if (any && prefab.GetComponent<ProcFish>() == null)
                    prefab.AddComponent<ProcFish>();
            }
            Plugin.Log.LogInfo("Fish: " + rigged + " meshes on a spine, " + swayed + " fish swaying");
        }

        /// <summary>Spine rig in the fish root's space (forward = +z = head).</summary>
        private static bool Rig(GameObject root, MeshFilter mf, MeshRenderer mr)
        {
            var src = mf.sharedMesh;
            var toRoot = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
            var v = src.vertices.Select(p => toRoot.MultiplyPoint3x4(p)).ToArray();
            var n = src.normals.Select(p => toRoot.MultiplyVector(p).normalized).ToArray();
            if (v.Length == 0)
                return false;
            float zMin = v.Min(p => p.z), zMax = v.Max(p => p.z);
            const int J = 6;
            var rigRoot = new GameObject("FishRig_" + mf.name).transform;
            rigRoot.SetParent(root.transform, false);
            var bones = new Transform[J];
            var jz = new float[J];
            Transform parent = rigRoot;
            Vector3 parentPos = Vector3.zero;
            for (int j = 0; j < J; j++)
            {
                float z = Mathf.Lerp(zMax, zMin, j / (float)(J - 1) * 0.92f);   // head first; last joint near the tail fin
                var slice = v.Where(p => Mathf.Abs(p.z - z) < (zMax - zMin) / J).ToArray();
                Vector3 c = slice.Length > 0 ? new Vector3(slice.Average(p => p.x), slice.Average(p => p.y), z) : new Vector3(0f, 0f, z);
                bones[j] = new GameObject("FishSpine" + j).transform;
                bones[j].SetParent(parent, false);
                bones[j].localPosition = c - parentPos;
                parent = bones[j];
                parentPos = c;
                jz[j] = z;
            }
            var weights = new BoneWeight[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                float z = v[i].z;
                if (z >= jz[0]) { weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f }; continue; }
                int k = J - 1;
                for (int j = 0; j < J - 1; j++)
                    if (z <= jz[j] && z >= jz[j + 1]) { k = j; break; }
                if (k == J - 1) { weights[i] = new BoneWeight { boneIndex0 = J - 1, weight0 = 1f }; continue; }
                float t = Mathf.InverseLerp(jz[k], jz[k + 1], z);
                weights[i] = new BoneWeight { boneIndex0 = k, weight0 = 1f - t, boneIndex1 = k + 1, weight1 = t };
            }
            var mesh = new Mesh { name = src.name + "_fishrig" };
            mesh.vertices = v;
            mesh.normals = n;
            mesh.uv = src.uv;
            if (src.uv2 != null && src.uv2.Length == v.Length) mesh.uv2 = src.uv2;
            if (src.colors != null && src.colors.Length == v.Length) mesh.colors = src.colors;
            mesh.subMeshCount = src.subMeshCount;
            for (int s = 0; s < src.subMeshCount; s++) mesh.SetTriangles(src.GetTriangles(s), s);
            mesh.boneWeights = weights;
            mesh.bindposes = bones.Select(b => b.worldToLocalMatrix * rigRoot.localToWorldMatrix).ToArray();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            var smr = rigRoot.gameObject.AddComponent<SkinnedMeshRenderer>();
            smr.sharedMesh = mesh;
            smr.bones = bones;
            smr.rootBone = bones[0];
            smr.sharedMaterials = mr.sharedMaterials;
            var b2 = mesh.bounds;
            b2.Expand(b2.size.magnitude * 0.5f);
            smr.localBounds = b2;
            mr.enabled = false;
            return true;
        }

    }

    /// <summary>Fish swimming wave (spine rigs) or head-pivot sway, driven by the fish's speed.</summary>
    public class ProcFish : MonoBehaviour, IProcAnimated
    {
        private readonly List<Transform[]> _spines = new List<Transform[]>();
        private Transform _visual;            // swaying fish: the node that holds the meshes
        private Vector3 _basePos, _pivot;     // in the visual's parent space
        private Quaternion _baseRot;
        private Vector3 _lastPos;
        private float _speed, _phase, _seed;
        private bool _lab;

        public float[] LabSpeeds => new[] { 0f, 1.5f };

        private void Awake()
        {
            foreach (Transform child in transform)
            {
                if (child.name.StartsWith("FishRig_"))
                {
                    // materials resolved by now: copy them from the hidden original renderer
                    var smr = child.GetComponent<SkinnedMeshRenderer>();
                    var original = GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => "FishRig_" + r.name == child.name);
                    if (smr != null && original != null && original.sharedMaterials.Any(m => m != null))
                        smr.sharedMaterials = original.sharedMaterials;
                    var list = new List<Transform>();
                    var t = child.Find("FishSpine0");
                    while (t != null) { list.Add(t); t = t.childCount > 0 ? t.Find("FishSpine" + list.Count) : null; }
                    _spines.Add(list.ToArray());
                }
            }
            if (_spines.Count == 0)
                SetUpSway();
            _lastPos = transform.position;
            _seed = Random.Range(0f, 10f);
        }

        private void SetUpSway()
        {
            var mr = GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(r => r.enabled && r.gameObject.activeSelf)
                     ?? GetComponentInChildren<MeshRenderer>(true);
            if (mr == null || mr.transform.parent == null || mr.transform.parent == transform)
                return;
            _visual = mr.transform.parent;                 // "attachobj"
            var parent = _visual.parent;
            _basePos = _visual.localPosition;
            _baseRot = _visual.localRotation;
            var b = mr.GetComponent<MeshFilter>().sharedMesh.bounds;
            var toRoot = transform.worldToLocalMatrix * mr.transform.localToWorldMatrix;
            float zMin = float.MaxValue, zMax = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                float z = toRoot.MultiplyPoint3x4(corner).z;
                zMin = Mathf.Min(zMin, z);
                zMax = Mathf.Max(zMax, z);
            }
            Vector3 c = toRoot.MultiplyPoint3x4(b.center);
            var pivotRoot = new Vector3(c.x, c.y, zMin + (zMax - zMin) * 0.8f);   // just behind the head
            _pivot = parent.InverseTransformPoint(transform.TransformPoint(pivotRoot));
        }

        public void LabPose(float speed, float time)
        {
            _lab = true;
            _speed = speed;
            _phase = 0f;
            const float step = 1f / 60f;
            for (float t = 0f; t < time; t += step) Animate(step);
            Animate(0f);
        }

        private void LateUpdate()
        {
            if (_lab)
                return;
            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 v = (transform.position - _lastPos) / dt;
            _lastPos = transform.position;
            float sp = v.magnitude > 20f ? 0f : v.magnitude;
            _speed = Mathf.Lerp(_speed, sp, 1f - Mathf.Exp(-dt * 4f));
            Animate(dt);
        }

        private void Animate(float dt)
        {
            float fast = Mathf.Clamp01(_speed / 1.5f);
            float freq = 0.9f + fast * 2.2f;                       // tail beats per second
            _phase += dt * freq * Mathf.PI * 2f;
            float amp = 10f + 22f * fast;                         // total bend at the tail
            foreach (var spine in _spines)
            {
                float prev = 0f;
                for (int i = 0; i < spine.Length; i++)
                {
                    float k = (float)i / (spine.Length - 1);
                    float bend = amp * Mathf.Pow(k, 1.8f) * Mathf.Sin(_phase - k * 2.6f);
                    spine[i].localRotation = Quaternion.Euler(0f, bend - prev, 0f);
                    prev = bend;
                }
            }
            float yaw = (5f + 7f * fast) * Mathf.Sin(_phase);
            float roll = 3f * Mathf.Sin(_phase + 1f + _seed);
            if (_visual != null)
            {
                // rotate the visual node about the pivot: q * (x - pivot) + pivot
                var q = Quaternion.Euler(0f, yaw, roll);
                _visual.localRotation = q * _baseRot;
                _visual.localPosition = _pivot + q * (_basePos - _pivot);
            }
        }
    }
}
