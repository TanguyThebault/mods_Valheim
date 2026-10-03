using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ThrowingAxe
{
    /// <summary>
    /// Spawned by the vanilla Attack pipeline (Attack.FireProjectileBurst -> IProjectile.Setup), so the
    /// HitData already carries the axe's damage, skill, quality and status-effect modifiers.
    /// Outbound: flies straight along the aim, passes through creatures (one hit each) and turns back at
    /// MaxRange or at the first solid obstacle (which it hits). Return: homes on the right hand, hitting
    /// creatures again, ignoring obstacles so it can't get stuck.
    /// Local-only visuals: no ZNetView. Damage goes through IDestructible.Damage, which the game routes to
    /// the target's owner.
    /// </summary>
    public class ThrowingAxeProjectile : MonoBehaviour, IProjectile
    {
        private static readonly Dictionary<Character, ThrowingAxeProjectile> s_inFlight =
            new Dictionary<Character, ThrowingAxeProjectile>();
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_rightItemInstance =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static int s_mask;

        private const float MaxFlightTime = 8f;
        private const float CatchDistance = 0.6f;

        private Character _owner;
        private VisEquipment _ownerVis;
        private HitData _hitData;
        private ItemDrop.ItemData _item;
        private Vector3 _dir;
        private float _travelled;
        private float _age;
        private float _spin;
        private bool _returning;
        private readonly HashSet<IDestructible> _hitThisLeg = new HashSet<IDestructible>();
        private Transform _visual;
        private GameObject _handItem;

        public static bool IsInFlight(Character c)
        {
            return c != null && s_inFlight.TryGetValue(c, out var p) && p != null;
        }

        public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData,
            ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            if (s_mask == 0)
                s_mask = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "piece_nonsolid",
                    "terrain", "character", "character_net", "character_ghost", "hitbox", "character_noenv",
                    "vehicle");

            _owner = owner;
            _hitData = hitData;
            _item = item;
            _dir = velocity.sqrMagnitude > 0.001f ? velocity.normalized : owner.transform.forward;
            transform.rotation = Quaternion.LookRotation(_dir);
            s_inFlight[owner] = this;

            _ownerVis = owner.GetComponent<VisEquipment>();
            _handItem = _ownerVis != null ? s_rightItemInstance(_ownerVis) : null;
            BuildVisual();
            if (_handItem != null)
                _handItem.SetActive(false);

            Plugin.Log.LogDebug("Throw from " + transform.position + " dir " + _dir);
        }

        public string GetTooltipString(int itemQuality)
        {
            return "";
        }

        private void BuildVisual()
        {
            if (_handItem == null)
                return;
            var copy = Instantiate(_handItem, transform);
            copy.SetActive(true);
            copy.transform.localPosition = Vector3.zero;
            // Lay the axe flat-ish across the flight direction; the spin axis is the local right.
            copy.transform.localRotation = Quaternion.Euler(0f, 90f, 90f);
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(rb);
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);

            var pivot = new GameObject("spin").transform;
            pivot.SetParent(transform, false);
            copy.transform.SetParent(pivot, false);
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
                Catch();
                return;
            }

            _spin += Plugin.SpinSpeed.Value * dt;
            if (_visual != null)
                _visual.localRotation = Quaternion.AngleAxis(_spin, Vector3.right);

            Vector3 pos = transform.position;
            if (!_returning)
            {
                float step = Mathf.Min(Plugin.OutSpeed.Value * dt, Plugin.MaxRange.Value - _travelled);
                if (Sweep(pos, _dir, step, out float stopAt))
                {
                    transform.position = pos + _dir * stopAt;
                    TurnBack();
                    return;
                }
                transform.position = pos + _dir * step;
                _travelled += step;
                if (_travelled >= Plugin.MaxRange.Value - 0.01f)
                    TurnBack();
            }
            else
            {
                Vector3 hand = HandPosition();
                Vector3 to = hand - pos;
                float dist = to.magnitude;
                float step = Plugin.ReturnSpeed.Value * dt;
                if (dist <= CatchDistance + step)
                {
                    Catch();
                    return;
                }
                Vector3 dir = to / dist;
                Sweep(pos, dir, step, out _);
                transform.position = pos + dir * step;
                transform.rotation = Quaternion.LookRotation(dir);
            }
        }

        /// <summary>Hits creatures along the segment; returns true at the first solid obstacle (outbound only).</summary>
        private bool Sweep(Vector3 from, Vector3 dir, float length, out float stopAt)
        {
            stopAt = length;
            if (length <= 0f)
                return false;

            var hits = Physics.SphereCastAll(from, Plugin.HitRadius.Value, dir, length, s_mask,
                QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var h in hits)
            {
                var go = Projectile.FindHitObject(h.collider);
                if (go == null || go.transform.root == _owner.transform.root)
                    continue;

                var destr = go.GetComponent<IDestructible>();
                Vector3 point = h.distance <= 0f && h.point == Vector3.zero ? from : h.point;

                if (destr is Character character)
                {
                    if (CanHit(character))
                        DoHit(destr, h.collider, point, dir, character);
                    continue; // creatures don't stop the axe
                }

                if (_returning)
                    continue; // the way back ignores obstacles
                if (h.collider.isTrigger && destr == null)
                    continue; // trigger zones (wards, areas) aren't obstacles

                if (destr != null)
                    DoHit(destr, h.collider, point, dir, null);
                else
                    _item.m_shared.m_hitTerrainEffect.Create(point, Quaternion.identity);
                stopAt = Mathf.Max(0f, h.distance - 0.05f);
                Plugin.Log.LogDebug("Obstacle " + go.name + " at " + (_travelled + stopAt).ToString("F1") + " m");
                return true;
            }
            return false;
        }

        private bool CanHit(Character c)
        {
            if (c == _owner || _hitThisLeg.Contains(c) || c.IsDead())
                return false;
            // Same rule as vanilla projectiles: no friendly fire on tames or players without PvP.
            bool enemy = BaseAI.IsEnemy(_owner, c) ||
                         (c.GetBaseAI() != null && c.GetBaseAI().IsAggravatable() && _owner.IsPlayer());
            if (_owner.IsPlayer() && !_owner.IsPVPEnabled() && !enemy)
                return false;
            if (c.IsDodgeInvincible())
                return false;
            return true;
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

        private void TurnBack()
        {
            _returning = true;
            _hitThisLeg.Clear();
        }

        private Vector3 HandPosition()
        {
            if (_ownerVis != null && _ownerVis.m_rightHand != null)
                return _ownerVis.m_rightHand.position;
            return _owner.GetCenterPoint();
        }

        private void Catch()
        {
            Destroy(gameObject);
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
