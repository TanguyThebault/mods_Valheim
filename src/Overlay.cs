using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// `ta_overlay [filter|off] [range]`: draws a frame, the name and the distance of every creature and bird
    /// around, through walls and terrain. Our creatures in green, vanilla ones in white. The filter matches the
    /// prefab or display name (e.g. "Mouse", "Owl").
    /// </summary>
    internal class OverlayCommand : ConsoleCommand
    {
        public override string Name => "ta_overlay";
        public override string Help => "ta_overlay [filter|off] [range=100] - frames and names of creatures, seen through walls";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (args.Length > 0 && args[0] == "off")
            {
                CreatureOverlay.Disable();
                Console.instance.Print("Overlay off");
                return;
            }
            string filter = args.Length > 0 && args[0] != "all" ? args[0] : "";
            float range = args.Length > 1 && float.TryParse(args[1], out var r) ? r : 100f;
            CreatureOverlay.Enable(filter, range);
            Console.instance.Print("Overlay on" + (filter.Length > 0 ? " (" + filter + ")" : "") + ", range " + range + " m");
        }
    }

    internal class CreatureOverlay : MonoBehaviour
    {
        private static CreatureOverlay s_instance;
        private static readonly string[] OurPrefabs =
        {
            Rabbits.CreaturePrefab, Foxes.CreaturePrefab, Mice.CreaturePrefab, Birds.SparrowPrefab, Birds.OwlPrefab,
        };

        private string _filter = "";
        private float _range = 100f;
        private Texture2D _white;
        private GUIStyle _label;
        private readonly Dictionary<GameObject, Renderer[]> _renderers = new Dictionary<GameObject, Renderer[]>();

        public static void Enable(string filter, float range)
        {
            if (s_instance == null)
            {
                var go = new GameObject("Wildlife_overlay");
                DontDestroyOnLoad(go);
                s_instance = go.AddComponent<CreatureOverlay>();
            }
            s_instance._filter = filter.ToLowerInvariant();
            s_instance._range = range;
        }

        public static void Disable()
        {
            if (s_instance != null)
                Destroy(s_instance.gameObject);
            s_instance = null;
        }

        private IEnumerable<KeyValuePair<GameObject, string>> Targets()
        {
            foreach (var c in Character.GetAllCharacters())
                if (c != null && !c.IsPlayer())
                    yield return new KeyValuePair<GameObject, string>(c.gameObject, Localization.instance.Localize(c.m_name));
            foreach (var u in RandomFlyingBird.Instances)
                if (u is RandomFlyingBird b && b != null)
                {
                    var h = b.GetComponent<HoverText>();
                    yield return new KeyValuePair<GameObject, string>(b.gameObject, h != null ? Localization.instance.Localize(h.m_text) : b.name);
                }
        }

        private void OnGUI()
        {
            var cam = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
            if (cam == null || Event.current.type != EventType.Repaint)
                return;
            if (_white == null)
            {
                _white = new Texture2D(1, 1);
                _white.SetPixel(0, 0, Color.white);
                _white.Apply();
                _label = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
            }
            Vector3 eye = cam.transform.position;
            foreach (var kv in Targets().ToList())
            {
                var go = kv.Key;
                string prefab = go.name.Replace("(Clone)", "").Trim();
                if (_filter.Length > 0 && !prefab.ToLowerInvariant().Contains(_filter) && !kv.Value.ToLowerInvariant().Contains(_filter))
                    continue;
                float dist = Vector3.Distance(eye, go.transform.position);
                if (dist > _range)
                    continue;
                if (!ScreenRect(cam, go, out var rect))
                    continue;
                bool ours = OurPrefabs.Contains(prefab);
                var color = ours ? new Color(0.3f, 1f, 0.4f) : Color.white;
                Frame(rect, color);
                GUI.color = Color.black;
                GUI.Label(new Rect(rect.x + 1, rect.y - 19, 260, 20), kv.Value + "  " + dist.ToString("F0") + " m", _label);
                GUI.color = color;
                GUI.Label(new Rect(rect.x, rect.y - 20, 260, 20), kv.Value + "  " + dist.ToString("F0") + " m", _label);
                GUI.color = Color.white;
            }
        }

        /// <summary>Screen rectangle (GUI coordinates) around the object's renderers; false if behind the camera.</summary>
        private bool ScreenRect(Camera cam, GameObject go, out Rect rect)
        {
            rect = default;
            if (!_renderers.TryGetValue(go, out var rs) || rs.Any(r => r == null))
                _renderers[go] = rs = go.GetComponentsInChildren<Renderer>().Where(r => !(r is ParticleSystemRenderer)).ToArray();
            Bounds b;
            var visible = rs.Where(r => r.enabled && r.gameObject.activeInHierarchy).ToArray();
            if (visible.Length == 0)
                b = new Bounds(go.transform.position + Vector3.up * 0.3f, Vector3.one * 0.6f);
            else
            {
                b = visible[0].bounds;
                foreach (var r in visible) b.Encapsulate(r.bounds);
            }
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                Vector3 s = cam.WorldToScreenPoint(corner);
                if (s.z <= 0f)
                    return false;
                minX = Mathf.Min(minX, s.x); maxX = Mathf.Max(maxX, s.x);
                minY = Mathf.Min(minY, s.y); maxY = Mathf.Max(maxY, s.y);
            }
            // tiny creatures far away still get a visible frame
            float w = Mathf.Max(maxX - minX, 10f), h = Mathf.Max(maxY - minY, 10f);
            float cx = (minX + maxX) / 2f, cy = (minY + maxY) / 2f;
            rect = new Rect(cx - w / 2f, Screen.height - (cy + h / 2f), w, h);
            return true;
        }

        private void Frame(Rect r, Color c)
        {
            GUI.color = c;
            const float t = 2f;
            GUI.DrawTexture(new Rect(r.x, r.y, r.width, t), _white);
            GUI.DrawTexture(new Rect(r.x, r.yMax - t, r.width, t), _white);
            GUI.DrawTexture(new Rect(r.x, r.y, t, r.height), _white);
            GUI.DrawTexture(new Rect(r.xMax - t, r.y, t, r.height), _white);
            GUI.color = Color.white;
        }

        private void OnDestroy()
        {
            if (_white != null)
                Destroy(_white);
        }
    }
}
