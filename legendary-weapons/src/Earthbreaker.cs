using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Jotunn.Managers;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// The Earthbreaker (Meadows mace). Hold the secondary attack (dust and grit whirl round the mace), the mace
    /// slams the ground (vanilla secondary swing, no melee hit) and the earth splits in a line straight ahead:
    /// a crack racing QuakeLength metres, QuakeWidth wide. Whatever stands on it is hit when the crack reaches it:
    /// blunt damage falling off with the distance (full within 2 m, QuakeEdgeDamage at the far end), creatures
    /// thrown up into the air (less for heavy ones, never bosses) and staggered as they come down. Never buildings.
    /// Damage and launches are done by the wielder; everyone sees and hears the quake (routed RPC).
    /// </summary>
    internal static class Earthbreaker
    {
        public const string RpcName = "LegendaryWeapons_Quake";
        private static ZRoutedRpc s_rpcInstance;
        /// <summary>
        /// The power attack: the overhead ground slam of the Stagbreaker sledge (the mace's own secondary is an
        /// upward swing), hitting nothing (type None), stripped of the sledge's lightning effects; the mace's own
        /// swing sound at the start, our quake at the blow.
        /// </summary>
        public static void Register(ItemDrop.ItemData.SharedData mace)
        {
            var sledge = PrefabManager.Cache.GetPrefab<ItemDrop>("SledgeStagbreaker");
            var slam = sledge != null ? sledge.m_itemData.m_shared.m_attack.Clone() : mace.m_secondaryAttack.Clone();
            if (sledge == null)
                Plugin.Log.LogWarning("SledgeStagbreaker not found: the quake uses the mace's own secondary swing");
            slam.m_attackType = Attack.AttackType.None;
            slam.m_startEffect = mace.m_secondaryAttack.m_startEffect;
            slam.m_triggerEffect = new EffectList();
            slam.m_hitEffect = new EffectList();
            slam.m_hitTerrainEffect = new EffectList();
            slam.m_attackStamina = mace.m_secondaryAttack.m_attackStamina * 1.3f;
            slam.m_attackChainLevels = 0;
            HoldPower.Register(new HoldPower.Spec
            {
                Token = Plugin.MaceToken, HoldTime = () => Plugin.QuakeHoldTime.Value, PowerAttack = slam, Fire = Fire,
                Theme = ChargeFx.Theme.Earth, Cooldown = () => Plugin.QuakeCooldown.Value, Icon = mace.m_icons?.FirstOrDefault(),
                RestName = "$se_maceearth_rest", RestTooltip = "$se_maceearth_rest_tooltip",
            });
            Plugin.Log.LogInfo("Earthbreaker slam: animation " + slam.m_attackAnimation + " (mace secondary: " + mace.m_secondaryAttack.m_attackAnimation + ")");
        }

        internal static float Share(float along) =>
            Mathf.Lerp(1f, Plugin.QuakeEdgeDamage.Value, Mathf.Clamp01((along - 2f) / Mathf.Max(0.1f, Plugin.QuakeLength.Value - 2f)));

        public static void Fire(Player owner, ItemDrop.ItemData weapon)
        {
            if (weapon == null)
                return;
            Vector3 dir = Vector3.ProjectOnPlane(owner.transform.forward, Vector3.up).normalized;
            if (dir.sqrMagnitude < 0.01f)
                return;
            Vector3 origin = owner.transform.position + dir * 0.9f;
            Broadcast(origin, dir);
            var targets = Collect(owner, weapon, origin, dir);
            if (targets.Count > 0)
                owner.RaiseSkill(Skills.SkillType.Clubs, 1f);
            Quake.Spawn(origin, dir, targets);
            Plugin.Log.LogInfo("Earthbreaker quake: " + targets.Count + " target(s) on the " + Plugin.QuakeLength.Value + " m crack");
        }

        private static List<Quake.Target> Collect(Player owner, ItemDrop.ItemData weapon, Vector3 origin, Vector3 dir)
        {
            float length = Plugin.QuakeLength.Value, half = Plugin.QuakeWidth.Value / 2f;
            var centre = origin + dir * (length / 2f) + Vector3.up * 0.8f;
            var seen = new HashSet<GameObject>();
            var list = new List<Quake.Target>();
            foreach (var col in Physics.OverlapBox(centre, new Vector3(half, 2f, length / 2f), Quaternion.LookRotation(dir),
                         Geometry.HitMask, QueryTriggerInteraction.Collide))
            {
                var go = Projectile.FindHitObject(col);
                if (go == null || go.transform.root == owner.transform.root || !seen.Add(go))
                    continue;
                var destr = go.GetComponent<IDestructible>();
                if (destr == null || go.GetComponent<WearNTear>() != null)      // never the buildings
                    continue;
                var c = destr as Character;
                if (c != null && !Geometry.CanHit(owner, c))
                    continue;
                // the point of the thing nearest the crack's line
                float a = Mathf.Clamp(Vector3.Dot(col.bounds.center - origin, dir), 0f, length);
                bool convex = !(col is MeshCollider mc) || mc.convex;
                Vector3 point = convex ? col.ClosestPoint(origin + dir * a) : col.bounds.center;
                float along = Mathf.Clamp(Vector3.Dot(point - origin, dir), 0f, length);
                float share = Share(along);

                var hit = new HitData();
                hit.m_damage = weapon.GetDamage();
                hit.m_damage.Modify(Plugin.QuakeDamage.Value * share * owner.GetRandomSkillFactor(Skills.SkillType.Clubs));
                hit.m_toolTier = (short)weapon.m_shared.m_toolTier;
                hit.m_point = point;
                hit.m_dir = dir;
                hit.m_pushForce = weapon.m_shared.m_attackForce * 0.5f * share;
                hit.m_staggerMultiplier = 3f;
                hit.m_blockable = true;
                hit.m_dodgeable = true;
                hit.m_skill = Skills.SkillType.Clubs;
                hit.m_hitType = HitData.HitType.PlayerHit;
                hit.m_itemLevel = (short)weapon.m_quality;
                hit.SetAttacker(owner);
                list.Add(new Quake.Target { Obj = destr, Char = c, Go = go, Hit = hit, Along = along, Share = share, Effect = weapon.m_shared.m_hitEffect });
                Plugin.Log.LogDebug("  " + go.name + " at " + along.ToString("F1") + " m: " + (share * 100f).ToString("F0") + " % damage");
            }
            list.Sort((x, y) => x.Along.CompareTo(y.Along));
            return list;
        }

        public static void RegisterRpc()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == s_rpcInstance)
                return;
            ZRoutedRpc.instance.Register<Vector3, Vector3>(RpcName, (sender, pos, dir) =>
            {
                if (sender != ZNet.GetUID())       // the wielder spawns its own
                    Quake.Spawn(pos, dir, null);
            });
            s_rpcInstance = ZRoutedRpc.instance;
        }

        private static void Broadcast(Vector3 pos, Vector3 dir)
        {
            if (ZRoutedRpc.instance != null && ZRoutedRpc.instance == s_rpcInstance)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, pos, dir);
        }
    }

    /// <summary>
    /// The quake: a dark, jagged crack drawing itself along the ground with short side cracks, rock chips and dust
    /// thrown up at its tip, the camera shaking, a crack and a rumble. On the
    /// wielder, each target is hit, thrown up and staggered when the crack reaches it.
    /// </summary>
    internal class Quake : MonoBehaviour
    {
        internal class Target
        {
            public IDestructible Obj;
            public Character Char;
            public GameObject Go;
            public HitData Hit;
            public float Along, Share;
            public EffectList Effect;
        }

        private const float Speed = 16f;
        private const float Life = 4.5f;
        private static readonly AccessTools.FieldRef<Character, Rigidbody> s_body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");
        private static int s_ground;
        private static Mesh s_cube, s_rock;
        private static Material s_rockMat;

        private class Spike
        {
            public float At, Height, Born = -1f;
            public Vector3 Base;
            public Quaternion Rot;
            public Vector3 Scale;
            public GameObject Go;
        }

        private readonly List<Spike> _spikes = new List<Spike>();

        private Vector3 _origin, _dir, _right;
        private float _length, _age, _front;
        private List<Target> _targets;
        private int _next;
        private LineRenderer _crack;
        private Vector3[] _crackPts;
        private readonly List<(LineRenderer line, float at, Vector3[] pts)> _branches = new List<(LineRenderer, float, Vector3[])>();
        private ParticleSystem _chips, _dust;

        public static void Spawn(Vector3 origin, Vector3 dir, List<Target> targets)
        {
            var go = new GameObject("earthbreaker_quake");
            go.transform.position = origin;
            go.AddComponent<Quake>().Build(origin, dir, targets);
            Sfx.Play(Sfx.Quake, origin, null);
            if (GameCamera.instance != null)
                GameCamera.instance.AddShake(origin, 25f, 1.5f, false);
            Destroy(go, Life + 1f);
        }

        private static float GroundY(Vector3 p, float fallback)
        {
            if (s_ground == 0)
                s_ground = LayerMask.GetMask("terrain", "static_solid", "Default", "piece");
            return Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, s_ground, QueryTriggerInteraction.Ignore)
                ? hit.point.y : fallback;
        }

        private Vector3 Ground(Vector3 p)
        {
            p.y = GroundY(p, _origin.y) + 0.04f;
            return p;
        }

        private LineRenderer Line(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = AirSlash.Mat();
            lr.useWorldSpace = true;
            lr.widthMultiplier = width;
            lr.numCornerVertices = 1;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.startColor = lr.endColor = new Color(0.07f, 0.05f, 0.04f, 0.95f);
            lr.positionCount = 0;
            return lr;
        }

        private void Build(Vector3 origin, Vector3 dir, List<Target> targets)
        {
            _origin = origin;
            _dir = dir.normalized;
            _right = Vector3.Cross(Vector3.up, _dir).normalized;
            _length = Plugin.QuakeLength.Value;
            _targets = targets;
            // the crack: a point every 0.25 m, wandering a little side to side, on the ground
            int n = Mathf.Max(4, (int)(_length / 0.25f));
            _crackPts = new Vector3[n + 1];
            float wander = 0f;
            for (int i = 0; i <= n; i++)
            {
                wander = Mathf.Clamp(wander + Random.Range(-0.12f, 0.12f), -0.35f, 0.35f);
                _crackPts[i] = Ground(_origin + _dir * (_length * i / n) + _right * (i == 0 ? 0f : wander + Random.Range(-0.06f, 0.06f)));
            }
            _crack = Line("crack", 0.2f);
            _crack.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.85f, 0.7f), new Keyframe(1f, 0.15f));
            for (int k = 0; k < 7; k++)
            {
                float at = Random.Range(0.8f, _length * 0.95f);
                int i0 = Mathf.Clamp((int)(at / _length * n), 0, n);
                float side = Random.value < 0.5f ? -1f : 1f;
                var bdir = (Quaternion.Euler(0f, side * Random.Range(35f, 65f), 0f) * _dir).normalized;
                float len = Random.Range(0.5f, 1.5f);
                var pts = new Vector3[5];
                for (int j = 0; j < pts.Length; j++)
                    pts[j] = Ground(_crackPts[i0] + bdir * (len * j / (pts.Length - 1)) + _right * Random.Range(-0.05f, 0.05f));
                var b = Line("branch", 0.09f);
                b.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
                _branches.Add((b, at, pts));
            }
            _chips = Particles("chips", true);
            _dust = Particles("dust", false);
            // rock spikes bursting up along the crack as it runs, then sinking back
            for (float at = 1.2f; at < _length; at += Random.Range(0.8f, 1.3f))
            {
                int i0 = Mathf.Clamp((int)(at / _length * n), 0, n);
                float side = Random.Range(-0.45f, 0.45f);
                float size = Mathf.Lerp(1f, 0.6f, at / _length) * Random.Range(0.75f, 1.2f);
                _spikes.Add(new Spike
                {
                    At = at,
                    Base = _crackPts[i0] + _right * side,
                    Height = 0.45f * size,
                    Rot = Quaternion.AngleAxis(Random.Range(0f, 360f), Vector3.up) *
                          Quaternion.AngleAxis(Random.Range(-22f, 22f), _right) * Quaternion.AngleAxis(side * 40f, _dir),
                    Scale = new Vector3(0.4f, 0.75f, 0.4f) * size,
                });
            }
            Impact();
        }

        private ParticleSystem Particles(string name, bool chips)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            if (chips)
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.5f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.28f);
                main.startRotation3D = true;
                main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.35f, 0.32f, 0.28f), new Color(0.55f, 0.5f, 0.42f));
                main.gravityModifier = 1.6f;
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.separateAxes = true;
                rot.x = rot.y = rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            }
            else
            {
                main.startLifetime = new ParticleSystem.MinMaxCurve(1.0f, 1.8f);
                main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.5f);
                main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.48f, 0.38f, 0.3f), new Color(0.7f, 0.64f, 0.52f, 0.45f));
                main.gravityModifier = -0.02f;
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.7f, 1f, 1.6f));
            }
            var em = ps.emission;
            em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = chips ? Fx.Plain : Fx.Soft;
            if (chips)
            {
                if (s_cube == null)
                {
                    var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    s_cube = tmp.GetComponent<MeshFilter>().sharedMesh;
                    Destroy(tmp);
                }
                r.renderMode = ParticleSystemRenderMode.Mesh;
                r.mesh = s_cube;
            }
            return ps;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float before = _front;
            _front = Mathf.Min(_length, _age * Speed);
            // the crack draws itself up to the front
            int shown = Mathf.Clamp(Mathf.CeilToInt(_front / _length * (_crackPts.Length - 1)) + 1, 0, _crackPts.Length);
            if (shown != _crack.positionCount)
            {
                _crack.positionCount = shown;
                for (int i = 0; i < shown; i++) _crack.SetPosition(i, _crackPts[i]);
            }
            foreach (var (line, at, pts) in _branches)
                if (line.positionCount == 0 && _front >= at)
                {
                    line.positionCount = pts.Length;
                    line.SetPositions(pts);
                }
            // chips and dust thrown up at the tip
            if (_front > before)
            {
                var tip = Ground(_origin + _dir * _front);
                var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                for (int i = 0; i < 5; i++)
                {
                    emit.position = tip + _right * Random.Range(-0.5f, 0.5f);
                    emit.velocity = Vector3.up * Random.Range(4f, 8f) + _right * Random.Range(-2f, 2f) + _dir * Random.Range(0f, 2f);
                    _chips.Emit(emit, 1);
                }
                for (int i = 0; i < 4; i++)
                {
                    emit.position = tip + _right * Random.Range(-0.6f, 0.6f) + Vector3.up * 0.3f;
                    emit.velocity = Vector3.up * Random.Range(0.5f, 1.5f) + _right * Random.Range(-0.8f, 0.8f);
                    _dust.Emit(emit, 1);
                }
            }
            UpdateSpikes();
            // the wielder's hits, as the crack gets there
            while (_targets != null && _next < _targets.Count && _targets[_next].Along <= _front)
                Strike(_targets[_next++]);
            // the crack closes up again, slowly
            float fade = Mathf.Clamp01((_age - (Life - 1.5f)) / 1.5f);
            if (fade > 0f)
            {
                var c = new Color(0.07f, 0.05f, 0.04f, 0.95f * (1f - fade));
                _crack.startColor = _crack.endColor = c;
                foreach (var (line, _, _) in _branches) line.startColor = line.endColor = c;
            }
        }

        /// <summary>The ground's own stone: the vanilla Stone item's mesh and material (cubes if it can't be found).</summary>
        private static void RockLook()
        {
            if (s_rock != null)
                return;
            var stone = ObjectDB.instance != null ? ObjectDB.instance.GetItemPrefab("Stone") : null;
            var mf = stone != null ? stone.GetComponentInChildren<MeshFilter>(true) : null;
            var mr = mf != null ? mf.GetComponent<MeshRenderer>() : null;
            if (mf != null && mr != null && mf.sharedMesh != null)
            {
                s_rock = mf.sharedMesh;
                s_rockMat = mr.sharedMaterial;
            }
            else
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s_rock = tmp.GetComponent<MeshFilter>().sharedMesh;
                Destroy(tmp);
                s_rockMat = new Material(Fx.Plain) { color = new Color(0.4f, 0.37f, 0.33f) };
            }
        }

        private void UpdateSpikes()
        {
            foreach (var sp in _spikes)
            {
                if (sp.Go == null && sp.Born < 0f && _front >= sp.At)
                {
                    RockLook();
                    sp.Born = _age;
                    sp.Go = new GameObject("quake_spike");
                    sp.Go.transform.SetParent(transform, true);
                    sp.Go.AddComponent<MeshFilter>().sharedMesh = s_rock;
                    var r = sp.Go.AddComponent<MeshRenderer>();
                    r.sharedMaterial = s_rockMat;
                    sp.Go.transform.rotation = sp.Rot;
                    // normalise the stone's own size to a 1 m block, then stretch it into a spike
                    var b = s_rock.bounds.size;
                    sp.Go.transform.localScale = new Vector3(sp.Scale.x / Mathf.Max(0.01f, b.x), sp.Scale.y / Mathf.Max(0.01f, b.y),
                        sp.Scale.z / Mathf.Max(0.01f, b.z));
                }
                if (sp.Go == null)
                    continue;
                float age = _age - sp.Born;
                float rise = Mathf.Clamp01(age / 0.12f);
                float sink = Mathf.Clamp01((_age - (Life - 1.4f)) / 0.9f);
                float up = sp.Height * (1f - (1f - rise) * (1f - rise)) - sp.Height * 1.3f * sink - sp.Height * 0.55f;
                sp.Go.transform.position = sp.Base + Vector3.up * up;
                if (sink >= 1f)
                {
                    Destroy(sp.Go);
                    sp.Go = null;
                }
            }
        }

        /// <summary>The blow itself: a ring of dust rolling out from the mace and a spray of bigger chunks.</summary>
        private void Impact()
        {
            var ring = Particles("impact_dust", false);
            var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
            for (int i = 0; i < 48; i++)
            {
                float a = i * Mathf.PI * 2f / 48f + Random.Range(-0.05f, 0.05f);
                var outDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                emit.position = _origin + outDir * 0.4f + Vector3.up * 0.2f;
                emit.velocity = outDir * Random.Range(4f, 6.5f) + Vector3.up * Random.Range(0.2f, 0.8f);
                emit.startSize = Random.Range(0.7f, 1.4f);
                ring.Emit(emit, 1);
            }
            emit.startSize = -1f;
            for (int i = 0; i < 25; i++)
            {
                emit.position = _origin + Random.insideUnitSphere * 0.3f;
                emit.velocity = Vector3.up * Random.Range(5f, 9f) + Random.insideUnitSphere * 3f;
                emit.startSize = Random.Range(0.12f, 0.3f);
                _chips.Emit(emit, 1);
            }
        }

        private void Strike(Target t)
        {
            if (t.Go == null)
                return;
            t.Obj.Damage(t.Hit);
            t.Effect?.Create(t.Hit.m_point, Quaternion.identity);
            var c = t.Char;
            if (c == null || c.IsDead() || c.IsBoss())
                return;
            c.Stagger(_dir);
            Daze.On(c, _dir, Plugin.QuakeStun.Value);
            var nview = c.GetComponent<ZNetView>();
            var body = s_body(c);
            if (body == null || nview == null || !nview.IsOwner())
            {
                Plugin.Log.LogDebug("  " + c.name + " not launched (not ours to move)");
                return;
            }
            float heavy = Mathf.Clamp(50f / Mathf.Max(1f, body.mass), 0.3f, 1f);
            float up = Plugin.QuakeLaunch.Value * t.Share * heavy;
            body.linearVelocity = new Vector3(body.linearVelocity.x, Mathf.Max(body.linearVelocity.y, 0f) + up, body.linearVelocity.z);
            c.TimeoutGroundForce(1f);
            Plugin.Log.LogDebug("  " + c.name + " launched at " + up.ToString("F1") + " m/s (mass " + body.mass.ToString("F0") + ")");
        }
    }

    /// <summary>
    /// The Earthbreaker's daze: one stagger (Lekinox: one is enough), little stars circling the creature's head while
    /// it reels. Never bosses.
    /// </summary>
    internal class Daze : MonoBehaviour
    {
        private Character _c;
        private Vector3 _dir;
        private float _until, _nextStagger;
        private ParticleSystem _stars;

        internal static void On(Character c, Vector3 dir, float seconds)
        {
            if (c == null || c.IsDead() || c.IsBoss() || seconds <= 0f)
                return;
            var d = c.GetComponent<Daze>() ?? c.gameObject.AddComponent<Daze>();
            d._c = c;
            d._dir = dir;
            d._until = Mathf.Max(d._until, Time.time + seconds);
            d._nextStagger = Time.time + 0.8f;
            if (d._stars == null)
                d.Build();
        }

        private void Build()
        {
            var go = new GameObject("earthbreaker_daze");
            go.transform.SetParent(transform, false);
            _stars = go.AddComponent<ParticleSystem>();
            _stars.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _stars.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.5f, 0.95f), new Color(1f, 1f, 0.85f, 1f));
            main.maxParticles = 60;
            var shape = _stars.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            float r = Mathf.Clamp(_c.GetRadius() * 0.8f, 0.25f, 1.2f);
            shape.radius = r;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var orbit = _stars.velocityOverLifetime;
            orbit.enabled = true;
            orbit.space = ParticleSystemSimulationSpace.Local;
            orbit.orbitalY = new ParticleSystem.MinMaxCurve(5f);
            var twinkle = _stars.sizeOverLifetime;
            twinkle.enabled = true;
            twinkle.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.3f, 1.2f), new Keyframe(1f, 0.2f)));
            var em = _stars.emission;
            em.rateOverTime = 25f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Soft;
            _stars.Play();
        }

        private void Update()
        {
            if (_c == null || _c.IsDead() || Time.time >= _until)
            {
                if (_stars != null)
                {
                    _stars.Stop(true, ParticleSystemStopBehavior.StopEmitting);
                    Destroy(_stars.gameObject, 1f);
                }
                Destroy(this);
                return;
            }
            // above the head, wherever the creature reels
            _stars.transform.position = _c.GetTopPoint() + Vector3.up * 0.15f;
            _stars.transform.rotation = Quaternion.identity;
        }
    }
}
