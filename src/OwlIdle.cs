using System.Collections.Generic;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// The perched owl's idle, played from the clips made in Blender (tools/blender_owl_idle.py, models/owl_perched.rig):
    /// a breathing loop all the time, and every few seconds a gesture layered on top: a head swivel (look), a curious
    /// tilt, the sideways head bob owls use to judge distance, or a feather ruffle. A player within LookRange draws the
    /// swivels toward them. Purely visual and local to each client: it only runs while the perched model is shown and
    /// on screen.
    /// </summary>
    public class OwlIdle : MonoBehaviour
    {
        private const float LookRange = 30f;
        private const float MaxLookDegrees = 90f;   // the clip's swivel; the game scales it down to the target

        private struct Rest
        {
            public Transform T;
            public Vector3 Pos;
        }

        private RigClip _breathe;
        private readonly Dictionary<string, RigClip> _gestures = new Dictionary<string, RigClip>();
        private readonly Dictionary<string, Rest> _bones = new Dictionary<string, Rest>();
        private readonly Dictionary<string, Vector3> _rot = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Vector3> _off = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, float> _scale = new Dictionary<string, float>();
        private SkinnedMeshRenderer _smr;
        [SerializeField] private Quaternion _frame = Quaternion.identity;   // model space -> this object's space
        [SerializeField] private float _unit = 1f;                         // model units -> this object's units
        private float _time, _next;
        private RigClip _gesture;
        private float _gestureTime, _amount = 1f, _rate = 1f;
        private bool _mirror;

        internal void Setup(Quaternion frame, float unit)
        {
            _frame = frame;
            _unit = unit;
        }

        private void Awake()
        {
            var rig = RigFile.Get("owl_perched");
            _smr = GetComponent<SkinnedMeshRenderer>();
            if (rig == null || _smr == null)
            {
                enabled = false;
                return;
            }
            rig.Clips.TryGetValue("breathe", out _breathe);
            foreach (var kv in rig.Clips)
                if (kv.Key != "breathe")
                    _gestures[kv.Key] = kv.Value;
            foreach (var t in _smr.bones)
                if (t != null)
                    _bones[t.name] = new Rest { T = t, Pos = t.localPosition };
            _time = Random.Range(0f, 10f);
            _next = Random.Range(1.5f, 5f);
        }

        private void LateUpdate()
        {
            if (!_smr.isVisible)
                return;
            float dt = Time.deltaTime;
            _time += dt;
            _next -= dt;
            if (_gesture == null && _next <= 0f)
                Pick();

            foreach (var name in _bones.Keys)
            {
                _rot[name] = Vector3.zero;
                _off[name] = Vector3.zero;
                _scale[name] = 1f;
            }
            if (_breathe != null)
                Apply(_breathe, _time, 1f, false);
            if (_gesture != null)
            {
                _gestureTime += dt * _rate;
                Apply(_gesture, _gestureTime, _amount, _mirror);
                if (_gestureTime >= _gesture.Length)
                {
                    _gesture = null;
                    _next = Random.Range(2.5f, 7f);
                }
            }
            Quaternion inv = Quaternion.Inverse(_frame);
            foreach (var kv in _bones)
            {
                var r = _rot[kv.Key];
                kv.Value.T.localRotation = _frame * Quaternion.Euler(r.x, r.y, r.z) * inv;
                kv.Value.T.localPosition = kv.Value.Pos + _frame * (_off[kv.Key] * _unit);
                kv.Value.T.localScale = Vector3.one * _scale[kv.Key];
            }
        }

        /// <summary>A gesture: a look toward a nearby player when there is one, otherwise a weighted random pick.</summary>
        private void Pick()
        {
            var player = Player.GetClosestPlayer(transform.position, LookRange);
            float r = Random.value;
            string name;
            _amount = Random.Range(0.55f, 1f);
            _mirror = Random.value < 0.5f;
            if (player != null && r < 0.6f && _gestures.ContainsKey("look"))
            {
                // angle to the player around the owl's up, measured from where the model faces
                Vector3 d = transform.InverseTransformPoint(player.transform.position);
                Vector3 fwd = _frame * Vector3.forward, side = _frame * Vector3.right;
                d -= Vector3.Project(d, _frame * Vector3.up);
                float ang = Mathf.Atan2(Vector3.Dot(d, side), Vector3.Dot(d, fwd)) * Mathf.Rad2Deg;
                name = "look";
                _mirror = ang < 0f;
                _amount = Mathf.Clamp(Mathf.Abs(ang) / MaxLookDegrees, 0.35f, 1f);
            }
            else if (r < 0.45f) name = "look";
            else if (r < 0.65f) name = "tilt";
            else if (r < 0.82f) name = "bob";
            else name = "ruffle";
            if (!_gestures.TryGetValue(name, out _gesture))
                return;
            _gestureTime = 0f;
            _rate = Random.Range(0.8f, 1.15f);   // the same gesture is never played twice exactly alike
        }

        /// <summary>Adds a clip's pose (rotations and offsets add up, scales multiply). Mirror swaps left and right.</summary>
        private void Apply(RigClip clip, float t, float amount, bool mirror)
        {
            foreach (var ch in clip.Keys)
            {
                float v = RigClip.Sample(ch.Values, t, clip.Fps, clip.Loop);
                string bone = ch.Bone;
                if (mirror)
                {
                    if (ch.Kind == "ry" || ch.Kind == "rz" || ch.Kind == "px") v = -v;
                    if (bone == "WingL") bone = "WingR";
                    else if (bone == "WingR") bone = "WingL";
                }
                if (!_bones.ContainsKey(bone))
                    continue;
                if (ch.Kind == "s")
                {
                    _scale[bone] *= v;
                    continue;
                }
                v *= amount;
                var r = _rot[bone];
                var o = _off[bone];
                switch (ch.Kind)
                {
                    case "rx": r.x += v; break;
                    case "ry": r.y += v; break;
                    case "rz": r.z += v; break;
                    case "px": o.x += v; break;
                    case "py": o.y += v; break;
                    case "pz": o.z += v; break;
                }
                _rot[bone] = r;
                _off[bone] = o;
            }
        }
    }
}
