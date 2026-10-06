using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>Hit rules and helpers shared by the thrown weapons.</summary>
    internal static class Geometry
    {
        private static int s_hitMask;
        private static int s_characterMask;

        public static int HitMask
        {
            get
            {
                if (s_hitMask == 0)
                    s_hitMask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid",
                        "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv",
                        "vehicle");
                return s_hitMask;
            }
        }

        public static int CharacterMask
        {
            get
            {
                if (s_characterMask == 0)
                    s_characterMask = LayerMask.GetMask("character", "character_net", "character_ghost", "hitbox",
                        "character_noenv");
                return s_characterMask;
            }
        }

        /// <summary>Same rule as vanilla projectiles: no friendly fire on tames or players without PvP.</summary>
        public static bool CanHit(Character owner, Character c)
        {
            if (c == owner || c.IsDead())
                return false;
            bool enemy = BaseAI.IsEnemy(owner, c) ||
                         (c.GetBaseAI() != null && c.GetBaseAI().IsAggravatable() && owner.IsPlayer());
            if (owner.IsPlayer() && !owner.IsPVPEnabled() && !enemy)
                return false;
            return !c.IsDodgeInvincible();
        }

        /// <summary>Solid things that stop a throw: not creatures, not the thrower, not trigger zones.</summary>
        public static bool IsObstacle(Character owner, Collider col)
        {
            var go = Projectile.FindHitObject(col);
            if (go == null || go.transform.root == owner.transform.root)
                return false;
            var destr = go.GetComponent<IDestructible>();
            if (destr is Character)
                return false;
            if (col.isTrigger && destr == null)
                return false; // trigger zones (wards, areas) aren't obstacles
            return true;
        }

        public static bool LocalBounds(Transform root, Transform space, out Bounds bounds)
        {
            bounds = default;
            bool any = false;
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
            {
                Mesh mesh = null;
                if (r is SkinnedMeshRenderer smr) mesh = smr.sharedMesh;
                else if (r is MeshRenderer) mesh = r.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null)
                    continue;
                Bounds mb = mesh.bounds;
                for (int i = 0; i < 8; i++)
                {
                    Vector3 corner = mb.center + Vector3.Scale(mb.extents,
                        new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                    Vector3 p = space.InverseTransformPoint(r.transform.TransformPoint(corner));
                    if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; }
                    else bounds.Encapsulate(p);
                }
            }
            return any;
        }
    }
}
