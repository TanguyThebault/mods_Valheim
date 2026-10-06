using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// The thrown Thunder Spear. Spawned by the vanilla Attack pipeline like the Returning Axe, so the HitData
    /// carries the spear's damage and modifiers. Like a vanilla spear, the throw takes the item out of the
    /// inventory (m_consumeItem); this projectile carries it and drops it as a pickable item at the end.
    /// - Flight: ballistic (the attack's velocity plus gravity). Friendly creatures are passed through.
    /// - The first enemy or solid obstacle is hit and the spear sticks in it (it rides along with a creature).
    /// - While a lightning call is pending it stays stuck, waiting for the bolt (LightningCall strikes it).
    ///   Otherwise, or shortly after the bolt, it falls where it is, to be picked up.
    /// Local-only visuals, like the axe; damage goes through IDestructible.Damage.
    /// </summary>
    public class ThunderSpearProjectile : MonoBehaviour, IProjectile
    {
        private static readonly Dictionary<Character, ThunderSpearProjectile> s_out =
            new Dictionary<Character, ThunderSpearProjectile>();
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_rightItemInstance =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");

        private const float MaxFlightTime = 4f;
        private const float MaxLifetime = 20f;
        private const float Embed = 0.25f;     // how deep the tip sinks into what it hits
        private const float DropDelayAfterStrike = 0.5f;

        private enum State { Flying, Stuck }

        private Character _owner;
        private HitData _hitData;
        private ItemDrop.ItemData _item;
        private bool _dropped;
        private Vector3 _velocity;
        private State _state;
        private float _age;
        private float _stateAge;
        private bool _struck;
        private readonly HashSet<IDestructible> _passed = new HashSet<IDestructible>();

        // Stuck: follows what it hit.
        private Transform _anchor;
        private Vector3 _anchorPos;
        private Quaternion _anchorRot;

        public bool IsStuck => _state == State.Stuck;
        public bool IsFlying => _state == State.Flying;

        public static ThunderSpearProjectile Get(Character c)
        {
            return c != null && s_out.TryGetValue(c, out var p) && p != null ? p : null;
        }

        public void Setup(Character owner, Vector3 velocity, float hitNoise, HitData hitData,
            ItemDrop.ItemData item, ItemDrop.ItemData ammo)
        {
            _owner = owner;
            _hitData = hitData;
            _item = item;
            _velocity = velocity.sqrMagnitude > 0.01f ? velocity : owner.transform.forward * 25f;
            transform.rotation = Quaternion.LookRotation(_velocity);

            s_out[owner] = this;
            // The attack unequips and removes the item right after this call: copy the hand model now.
            var vis = owner.GetComponent<VisEquipment>();
            BuildVisual(vis != null ? s_rightItemInstance(vis) : null);
            Plugin.Log.LogDebug("Spear thrown at " + _velocity.magnitude.ToString("F1") + " m/s");
        }

        public string GetTooltipString(int itemQuality)
        {
            return "";
        }

        /// <summary>A copy of the hand model, its long axis along +forward with the tip at the origin.</summary>
        private void BuildVisual(GameObject handItem)
        {
            if (handItem == null)
                return;
            var pivot = new GameObject("visual").transform;
            pivot.SetParent(transform, false);
            var copy = Instantiate(handItem, pivot);
            copy.SetActive(true);
            copy.transform.localPosition = Vector3.zero;
            copy.transform.localRotation = Quaternion.identity;
            copy.transform.localScale = handItem.transform.lossyScale;
            // Setup runs inside an animation event, where DestroyImmediate is refused: disable now, destroy later.
            foreach (var c in copy.GetComponentsInChildren<Collider>(true)) { c.enabled = false; Destroy(c); }
            foreach (var rb in copy.GetComponentsInChildren<Rigidbody>(true)) { rb.isKinematic = true; rb.detectCollisions = false; Destroy(rb); }
            foreach (var mb in copy.GetComponentsInChildren<MonoBehaviour>(true)) { mb.enabled = false; Destroy(mb); }
            foreach (var l in copy.GetComponentsInChildren<Light>(true)) Destroy(l.gameObject);

            if (!Geometry.LocalBounds(copy.transform, pivot, out var b))
                return;
            // Longest axis = the shaft. The grip (the model's origin) sits behind the middle, so the tip is on
            // the side the bounds centre leans to.
            Vector3 e = b.extents;
            Vector3 axis = e.x >= e.y && e.x >= e.z ? Vector3.right : (e.y >= e.z ? Vector3.up : Vector3.forward);
            float lean = Vector3.Dot(b.center, axis);
            // our own model knows where its head is (WeaponModels); the vanilla look leans to the tip
            if (WeaponModels.Heads.TryGetValue(Plugin.SpearPrefab, out var head))
                lean = Vector3.Dot(head, axis);
            if (Plugin.SpearFlipVisual.Value) lean = -lean;
            Vector3 tipDir = lean >= 0f ? axis : -axis;
            Quaternion rot = Quaternion.Euler(Plugin.SpearVisualTilt.Value) * Quaternion.FromToRotation(tipDir, Vector3.forward);
            Vector3 tip = b.center + tipDir * Vector3.Dot(e, new Vector3(Mathf.Abs(axis.x), Mathf.Abs(axis.y), Mathf.Abs(axis.z)));
            copy.transform.localRotation = rot;
            copy.transform.localPosition = -(rot * tip);
            Plugin.Log.LogDebug("Spear visual extents " + e + ", tip " + tipDir);
        }

        private void Update()
        {
            if (_owner == null)
            {
                Destroy(gameObject); // logged out: the world is going away
                return;
            }
            float dt = Time.deltaTime;
            _age += dt;
            _stateAge += dt;
            if (_age > MaxLifetime || _owner.IsDead())
            {
                Drop();
                return;
            }

            if (_state == State.Flying)
                Fly(dt);
            else
                Stay();
        }

        private void Fly(float dt)
        {
            if (_stateAge > MaxFlightTime)
            {
                StickTo(null, transform.position, transform.rotation); // lost in the sky: wait there
                return;
            }
            _velocity += Vector3.down * Plugin.SpearGravity.Value * dt;
            Vector3 pos = transform.position;
            Vector3 step = _velocity * dt;
            float len = step.magnitude;
            if (len < 0.0001f)
                return;
            Vector3 dir = step / len;
            transform.rotation = Quaternion.LookRotation(dir);

            var hits = Physics.SphereCastAll(pos, 0.15f, dir, len, Geometry.HitMask, QueryTriggerInteraction.Collide);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            foreach (var h in hits)
            {
                var go = Projectile.FindHitObject(h.collider);
                if (go == null || go.transform.root == _owner.transform.root)
                    continue;
                Vector3 point = h.distance <= 0f && h.point == Vector3.zero ? pos : h.point;
                var destr = go.GetComponent<IDestructible>();
                if (destr is Character character)
                {
                    if (_passed.Contains(character) || !Geometry.CanHit(_owner, character))
                    {
                        _passed.Add(character);
                        continue; // friends and dodgers: fly on
                    }
                    DoHit(character, h.collider, point, dir, character);
                    StickTo(h.collider.transform, point + dir * Embed, transform.rotation);
                    return;
                }
                if (!Geometry.IsObstacle(_owner, h.collider))
                    continue;
                if (destr != null)
                    DoHit(destr, h.collider, point, dir, null);
                else
                    _item.m_shared.m_hitTerrainEffect.Create(point, Quaternion.identity);
                StickTo(h.collider.transform, point + dir * Embed, transform.rotation);
                return;
            }
            transform.position = pos + step;
        }

        private void StickTo(Transform anchor, Vector3 point, Quaternion rot)
        {
            transform.position = point;
            transform.rotation = rot;
            _anchor = anchor;
            if (anchor != null)
            {
                _anchorPos = anchor.InverseTransformPoint(point);
                _anchorRot = Quaternion.Inverse(anchor.rotation) * rot;
            }
            SetState(State.Stuck);
            Plugin.Log.LogDebug("Spear stuck " + (anchor != null ? "in " + anchor.name : "in the air") +
                                " at " + Vector3.Distance(point, _owner.transform.position).ToString("F1") + " m");
        }

        private void Stay()
        {
            if (_anchor != null)
            {
                transform.position = _anchor.TransformPoint(_anchorPos);
                transform.rotation = _anchor.rotation * _anchorRot;
            }
            // Waiting for the bolt: LightningCall strikes us when the call comes due.
            if (!_struck && LightningCall.IsPending(_owner))
                return;
            if (!_struck || _stateAge >= DropDelayAfterStrike)
                Drop();
        }

        /// <summary>The spear falls where it is, as the same item (durability, quality) to pick up.</summary>
        private void Drop()
        {
            if (!_dropped && _item != null && _item.m_dropPrefab != null)
            {
                _dropped = true;
                Vector3 pos = transform.position - transform.forward * (Embed + 0.3f);
                ItemDrop.DropItem(_item, 1, pos, transform.rotation);
                Plugin.Log.LogDebug("Spear dropped at " + pos);
            }
            Destroy(gameObject);
        }

        /// <summary>Called by LightningCall when the bolt hits the spear: it falls shortly after.</summary>
        public void OnStruck()
        {
            _struck = true;
            if (_state != State.Stuck)
                StickTo(null, transform.position, transform.rotation);
            _stateAge = 0f;
        }

        private void SetState(State s)
        {
            _state = s;
            _stateAge = 0f;
        }

        private void DoHit(IDestructible destr, Collider col, Vector3 point, Vector3 dir, Character character)
        {
            var hit = _hitData.Clone();
            hit.m_hitCollider = col;
            hit.m_point = point;
            hit.m_dir = dir;
            hit.m_ranged = true;
            destr.Damage(hit);
            var target = character != null ? character.GetZDOID() : ZDOID.None;
            _item.m_shared.m_hitEffect.Create(point, Quaternion.identity, null, 1f, -1, target);
            Plugin.Log.LogDebug("Spear hit " + ((MonoBehaviour)destr).name);
        }

        private void OnDestroy()
        {
            if (_owner != null && s_out.TryGetValue(_owner, out var p) && p == this)
                s_out.Remove(_owner);
        }
    }
}
