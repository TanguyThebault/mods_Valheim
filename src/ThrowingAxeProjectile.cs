using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// Spawned by the vanilla Attack pipeline (Attack.FireProjectileBurst -> IProjectile.Setup), so the
    /// HitData already carries the axe's damage, skill, quality and status-effect modifiers.
    ///
    /// Flight is a horizontal ellipse, like a boomerang:
    /// - At launch the range R is MaxRange, or the distance to the first solid obstacle on the aim line.
    /// - Outbound: half an ellipse from the throw point to start + aim * R, bulging to one side.
    /// - Return: the other half, from wherever it turned to the (moving) right hand, bulging to the other side.
    /// - Creatures are passed through (one hit each per leg). A solid obstacle met on the way out is hit and
    ///   the axe turns back from there. The return ignores obstacles so it can't get stuck.
    /// The visual lies flat (blade plane horizontal) and spins around the world vertical.
    /// Local-only visuals: no ZNetView. Damage goes through IDestructible.Damage, which the game routes to
    /// the target's owner.
    /// </summary>
    public class ThrowingAxeProjectile : MonoBehaviour, IProjectile
    {
        private static readonly Dictionary<Character, ThrowingAxeProjectile> s_inFlight =
            new Dictionary<Character, ThrowingAxeProjectile>();
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_rightItemInstance =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");

        private const float MaxFlightTime = 8f;
        private const float CatchDistance = 0.6f;

        private Character _owner;
        private VisEquipment _ownerVis;
        private HitData _hitData;
        private ItemDrop.ItemData _item;
        private float _age;
        private float _spin;
        private readonly HashSet<IDestructible> _hitThisLeg = new HashSet<IDestructible>();
        private Transform _visual;
        private GameObject _handItem;

        // Path
        private Vector3 _start;
        private Vector3 _dir;
        private Vector3 _side;      // horizontal, perpendicular to the aim; outbound bulges this way
        private float _range;
        private float _halfWidth;
        private bool _returning;
        private float _u;           // progress of the current leg, 0..1
        private Vector3 _turnPoint;
        private float _returnHalfWidth;
        private RaycastHit _endObstacle;
        private bool _hasEndObstacle;

        public static bool IsInFlight(Character c)
        {
            return c != null && s_inFlight.TryGetValue(c, out var p) && p != null;
        }

        public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData,
            ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            _owner = owner;
            _hitData = hitData;
            _item = item;
            _start = transform.position;
            _dir = velocity.sqrMagnitude > 0.001f ? velocity.normalized : owner.transform.forward;
            Vector3 flat = Vector3.ProjectOnPlane(_dir, Vector3.up);
            if (flat.sqrMagnitude < 0.001f)
                flat = owner.transform.forward;
            _side = Vector3.Cross(Vector3.up, flat.normalized).normalized * Mathf.Sign(Plugin.CurveSide.Value);

            _range = Plugin.MaxRange.Value;
            _hasEndObstacle = FirstObstacle(_start, _dir, _range, out _endObstacle);
            if (_hasEndObstacle)
                _range = Mathf.Max(1f, _endObstacle.distance);
            _halfWidth = _range * Plugin.CurveWidth.Value;

            s_inFlight[owner] = this;
            _ownerVis = owner.GetComponent<VisEquipment>();
            _handItem = _ownerVis != null ? s_rightItemInstance(_ownerVis) : null;
            BuildVisual();
            if (_handItem != null)
                _handItem.SetActive(false);

            Plugin.Log.LogDebug("Throw from " + _start + " range " + _range.ToString("F1") + " m" +
                                (_hasEndObstacle ? " (obstacle " + _endObstacle.collider.name + ")" : ""));
        }

        public string GetTooltipString(int itemQuality)
        {
            return "";
        }

        private void BuildVisual()
        {
            if (_handItem == null)
                return;

            var pivot = new GameObject("spin").transform;
            pivot.SetParent(transform, false);
            var copy = Instantiate(_handItem, pivot);
            copy.SetActive(true);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = _handItem.transform.lossyScale;
            // Setup runs inside an animation event, where DestroyImmediate is refused: disable now, destroy later.
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Destroy(c); }
            foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.detectCollisions = false; Destroy(rb); }
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) { mb.enabled = false; Destroy(mb); }

            // Lay it flat: the thinnest axis of the model (blade thickness) points up, and spin around the
            // model's centre rather than the grip.
            if (Geometry.LocalBounds(copy.transform, pivot, out var b))
            {
                Vector3 e = b.extents;
                Vector3 thin = e.x <= e.y && e.x <= e.z ? Vector3.right : (e.y <= e.z ? Vector3.up : Vector3.forward);
                Quaternion rot = Quaternion.FromToRotation(thin, Vector3.up) * Quaternion.Euler(Plugin.VisualTilt.Value);
                copy.transform.localRotation = rot;
                copy.transform.localPosition = -(rot * b.center);
                Plugin.Log.LogDebug("Visual extents " + e + ", thin axis " + thin);
            }
            _visual = pivot;
        }

        private void Update()
        {
            if (_owner == null || _owner.IsDead())
            {
                Destroy(gameObject);
                return;
            }

            float dt = Time.deltaTime;
            _age += dt;
            if (_age > MaxFlightTime)
            {
                Destroy(gameObject);
                return;
            }

            Vector3 pos = transform.position;
            float speed = _returning ? Plugin.ReturnSpeed.Value : Plugin.OutSpeed.Value;
            float newU = Advance(_u, speed * dt);
            Vector3 next = PathPoint(newU);
            Vector3 delta = next - pos;
            float len = delta.magnitude;

            if (len > 0.0001f)
            {
                Vector3 dir = delta / len;
                if (Sweep(pos, dir, len, out float stopAt))
                {
                    // Unexpected obstacle on the curve: turn back from the contact point.
                    transform.position = pos + dir * stopAt;
                    TurnBack();
                    return;
                }
                Vector3 flat = Vector3.ProjectOnPlane(dir, Vector3.up);
                if (flat.sqrMagnitude > 0.0001f)
                    transform.rotation = Quaternion.LookRotation(flat, Vector3.up);
            }
            transform.position = next;
            _u = newU;

            _spin += Plugin.SpinSpeed.Value * dt * Mathf.Sign(Plugin.CurveSide.Value);
            if (_visual != null)
                _visual.rotation = Quaternion.AngleAxis(_spin, Vector3.up);

            if (!_returning && _u >= 1f)
            {
                if (_hasEndObstacle)
                    HitObstacle(_endObstacle.collider, transform.position, _dir);
                TurnBack();
            }
            else if (_returning && (_u >= 1f || Vector3.Distance(transform.position, HandPosition()) <= CatchDistance))
            {
                Destroy(gameObject); // caught
            }
        }

        private Vector3 PathPoint(float u)
        {
            float a = Mathf.PI * Mathf.Clamp01(u);
            float along = (1f - Mathf.Cos(a)) * 0.5f;   // 0 -> 1, slow at the ends like an ellipse
            float bulge = Mathf.Sin(a);
            if (!_returning)
                return _start + _dir * (_range * along) + _side * (_halfWidth * bulge);
            return Vector3.Lerp(_turnPoint, HandPosition(), along) - _side * (_returnHalfWidth * bulge);
        }

        /// <summary>Moves u forward so the point travels about `distance` metres along the curve.</summary>
        private float Advance(float u, float distance)
        {
            const float eps = 0.002f;
            float d = (PathPoint(Mathf.Min(1f, u + eps)) - PathPoint(u)).magnitude / eps; // metres per unit u
            return Mathf.Min(1f, u + distance / Mathf.Max(d, 0.5f));
        }

        private void TurnBack()
        {
            _turnPoint = transform.position;
            float back = Vector3.Distance(_turnPoint, HandPosition());
            _returnHalfWidth = back * Plugin.CurveWidth.Value;
            _returning = true;
            _u = 0f;
            _hitThisLeg.Clear();
        }

        private bool FirstObstacle(Vector3 from, Vector3 dir, float range, out RaycastHit first)
        {
            first = default;
            var hits = Physics.SphereCastAll(from, Plugin.HitRadius.Value, dir, range, Geometry.HitMask,
                QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                if (Geometry.IsObstacle(_owner, h.collider))
                {
                    first = h;
                    return true;
                }
            }
            return false;
        }

        /// <summary>Hits creatures along the segment; returns true at the first solid obstacle (outbound only).</summary>
        private bool Sweep(Vector3 from, Vector3 dir, float length, out float stopAt)
        {
            stopAt = length;
            var hits = Physics.SphereCastAll(from, Plugin.HitRadius.Value, dir, length, Geometry.HitMask,
                QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var h in hits)
            {
                var go = Projectile.FindHitObject(h.collider);
                if (go == null || go.transform.root == _owner.transform.root)
                    continue;
                Vector3 point = h.distance <= 0f && h.point == Vector3.zero ? from : h.point;

                if (go.GetComponent<IDestructible>() is Character character)
                {
                    if (CanHit(character))
                        DoHit(character, h.collider, point, dir, character);
                    continue; // creatures don't stop the axe
                }
                if (_returning || !Geometry.IsObstacle(_owner, h.collider))
                    continue;
                // The planned end obstacle is handled at the turn point.
                if (_hasEndObstacle && h.collider == _endObstacle.collider)
                    continue;

                HitObstacle(h.collider, point, dir);
                stopAt = Mathf.Max(0f, h.distance - 0.05f);
                return true;
            }
            return false;
        }

        private void HitObstacle(Collider col, Vector3 point, Vector3 dir)
        {
            var go = Projectile.FindHitObject(col);
            var destr = go != null ? go.GetComponent<IDestructible>() : null;
            if (destr != null)
                DoHit(destr, col, point, dir, null);
            else
                _item.m_shared.m_hitTerrainEffect.Create(point, Quaternion.identity);
            Plugin.Log.LogDebug("Obstacle " + (go != null ? go.name : col.name) + " at " +
                                Vector3.Distance(_start, point).ToString("F1") + " m");
        }

        private bool CanHit(Character c)
        {
            return !_hitThisLeg.Contains(c) && Geometry.CanHit(_owner, c);
        }

        private void DoHit(IDestructible destr, Collider col, Vector3 point, Vector3 dir, Character character)
        {
            if (_hitThisLeg.Contains(destr))
                return;
            _hitThisLeg.Add(destr);

            var hit = _hitData.Clone();
            hit.m_hitCollider = col;
            hit.m_point = point;
            hit.m_dir = dir;
            hit.m_ranged = true;
            destr.Damage(hit);

            var target = character != null ? character.GetZDOID() : ZDOID.None;
            _item.m_shared.m_hitEffect.Create(point, Quaternion.identity, null, 1f, -1, target);
            Plugin.Log.LogDebug("Hit " + ((MonoBehaviour)destr).name + (_returning ? " (return)" : ""));
        }

        private Vector3 HandPosition()
        {
            if (_ownerVis != null && _ownerVis.m_rightHand != null)
                return _ownerVis.m_rightHand.position;
            return _owner.GetCenterPoint();
        }

        private void OnDestroy()
        {
            if (_handItem != null)
                _handItem.SetActive(true);
            if (_owner != null && s_inFlight.TryGetValue(_owner, out var p) && p == this)
                s_inFlight.Remove(_owner);
        }
    }
}
