using Jotunn.Entities;
using UnityEngine;

namespace Caca
{
    /// <summary>
    /// The poop in the hand. The item is an ooze-bomb clone, whose mesh node sits where the bomb's sphere needed to
    /// be (-0.03, -0.10, -0.11) with the bomb's rotation: our poop ended up in the wrist. Vanilla grips (club, torch)
    /// put the hand at the attach origin with the handle along +z, so the poop goes there, held in the middle. The
    /// bomb's dripping ooze child goes, and the round collider becomes a capsule around the poop, so a dropped one
    /// still lies flat. HeldPos / HeldRot ([Tuning]) are applied live to the poop in the local player's hand.
    /// </summary>
    internal static class Hold
    {
        public static void FixPrefab(GameObject item)
        {
            var attach = item.transform.Find("attach");
            if (attach == null)
            {
                Plugin.Log.LogWarning("Poop: no attach node");
                return;
            }
            foreach (var ps in attach.GetComponentsInChildren<ParticleSystem>(true))
                Object.DestroyImmediate(ps.gameObject);
            var mesh = PoopNode(attach.gameObject);
            if (mesh != null) Place(mesh);
            var col = attach.Find("collider");
            if (col != null)
            {
                foreach (var c in col.GetComponents<Collider>()) Object.DestroyImmediate(c);
                col.localPosition = Pos();
                col.localRotation = Quaternion.Euler(Rot());
                col.localScale = Vector3.one;
                var cap = col.gameObject.AddComponent<CapsuleCollider>();
                cap.direction = 2;
                cap.radius = 0.038f;
                cap.height = 0.21f;
                cap.center = new Vector3(0f, 0.004f, 0f);
            }
            Plugin.Log.LogInfo("Poop held at " + Pos() + " rot " + Rot());
        }

        private static Vector3 Pos() => Body.Vec(Plugin.HeldPos.Value, new Vector3(0f, 0f, 0.03f));
        private static Vector3 Rot() => Body.Vec(Plugin.HeldRot.Value, Vector3.zero);

        private static Transform PoopNode(GameObject root)
        {
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && mf.sharedMesh.name == "caca_mesh") return mf.transform;
            return null;
        }

        private static void Place(Transform t)
        {
            t.localPosition = Pos();
            t.localRotation = Quaternion.Euler(Rot());
            t.localScale = Vector3.one;
        }

        private static GameObject s_lastItem;
        private static Transform s_lastNode;

        /// <summary>Keeps the poop in the local player's hand where [Tuning] says (live tuning).</summary>
        public static void Apply(Player p)
        {
            var item = Body.RightItem(p.GetComponent<VisEquipment>());
            if (item != s_lastItem)
            {
                s_lastItem = item;
                s_lastNode = item != null ? PoopNode(item) : null;
            }
            if (s_lastNode != null) Place(s_lastNode);
        }
    }

    internal class HoldCommand : ConsoleCommand
    {
        public override string Name => "caca_hold";
        public override string Help => "caca_hold x y z [rx ry rz] - poop position/rotation in the hand (tests, saved)";

        public override void Run(string[] args)
        {
            if (args.Length >= 3) Plugin.HeldPos.Value = args[0] + " " + args[1] + " " + args[2];
            if (args.Length >= 6) Plugin.HeldRot.Value = args[3] + " " + args[4] + " " + args[5];
            Console.instance.Print("held " + Plugin.HeldPos.Value + " / " + Plugin.HeldRot.Value);
        }
    }
}
