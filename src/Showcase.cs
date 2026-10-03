using System.Collections.Generic;
using System.Globalization;
using Jotunn.Entities;
using UnityEngine;

namespace ThrowingAxe
{
    /// <summary>
    /// Test scene for screenshots: `ta_show &lt;prefab&gt; [distance] [height] [fly|front]` spawns a creature in front of
    /// the camera, in profile, raised above the grass and frozen (AI off, no gravity). `ta_clear` removes them.
    /// Birds show their perched model, or their flying model with `fly`.
    /// </summary>
    internal class ShowCommand : ConsoleCommand
    {
        internal static readonly List<GameObject> Shown = new List<GameObject>();

        public override string Name => "ta_show";
        public override string Help => "ta_show <prefab> [distance=2.5] [height=0.8] [fly] [front] - frozen creature in front of the camera";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            if (args.Length < 1 || ZNetScene.instance == null || GameCamera.instance == null)
            {
                Console.instance.Print(Help);
                return;
            }
            var prefab = ZNetScene.instance.GetPrefab(args[0]);
            if (prefab == null)
            {
                Console.instance.Print("Unknown prefab " + args[0]);
                return;
            }
            float dist = args.Length > 1 ? Parse(args[1], 2.5f) : 2.5f;
            float height = args.Length > 2 ? Parse(args[2], 0.8f) : 0.8f;
            bool fly = System.Array.IndexOf(args, "fly") >= 0;
            bool front = System.Array.IndexOf(args, "front") >= 0;

            var cam = GameCamera.instance.transform;
            Vector3 flat = Vector3.ProjectOnPlane(cam.forward, Vector3.up).normalized;
            Vector3 pos = cam.position + flat * dist;
            pos.y = ZoneSystem.instance.GetGroundHeight(pos) + height;
            var facing = front ? -flat : Vector3.Cross(Vector3.up, flat);   // profile by default
            var go = Object.Instantiate(prefab, pos, Quaternion.LookRotation(facing, Vector3.up));
            Freeze(go, fly);
            Shown.Add(go);
            Console.instance.Print("Showing " + args[0] + " at " + pos.ToString("F1"));
        }

        private static float Parse(string s, float fallback)
        {
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        }

        private static void Freeze(GameObject go, bool fly)
        {
            foreach (var ai in go.GetComponentsInChildren<BaseAI>()) ai.enabled = false;
            foreach (var rb in go.GetComponentsInChildren<Rigidbody>())
            {
                rb.useGravity = false;
                rb.isKinematic = true;
            }
            var bird = go.GetComponent<RandomFlyingBird>();
            if (bird != null)
            {
                bird.enabled = false;
                if (!bird.m_singleModel)
                {
                    bird.m_flyingModel.SetActive(fly);
                    bird.m_landedModel.SetActive(!fly);
                }
            }
            foreach (var c in go.GetComponentsInChildren<OwlHunter>()) c.enabled = false;
        }
    }

    internal class ClearCommand : ConsoleCommand
    {
        public override string Name => "ta_clear";
        public override string Help => "ta_clear - remove the creatures placed with ta_show";
        public override bool IsCheat => true;

        public override void Run(string[] args)
        {
            foreach (var go in ShowCommand.Shown)
            {
                if (go == null)
                    continue;
                var nview = go.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid())
                    nview.Destroy();
                else
                    Object.Destroy(go);
            }
            Console.instance.Print("Cleared " + ShowCommand.Shown.Count);
            ShowCommand.Shown.Clear();
        }
    }
}
