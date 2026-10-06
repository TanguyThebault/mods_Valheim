using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// The Wind Blade (Plains). Hold the secondary attack for HoldTime: wind gathers around the blade, then the sword
    /// swings (the vanilla secondary animation, with no melee hit of its own) and looses a gust that fills a
    /// cone-shaped zone ahead: pointed at the sword, rounding out SlashRange metres away (bulb of BulbRadius).
    /// Everything breakable in it is cut, creatures (no friendly fire), bushes, logs and trees, never buildings,
    /// with damage falling off with the distance from the swordsman: full within 1.5 m, EdgeDamage at the far end.
    /// A shorter press does nothing. Afterwards the wind blows the way of the gust for WindDuration seconds: same
    /// strength as the weather gives (only the direction is ours, as with a ship's wind control), turning in
    /// WindTurnTime seconds instead of the game's 5.
    /// Damage is done once, by the swordsman; everyone sees and hears the gust (routed RPC). The wind is local.
    /// </summary>
    internal static class WindBlade
    {
        public const string RpcName = "LegendaryWeapons_AirSlash";
        private static ZRoutedRpc s_rpcInstance;
        private static float s_windUntil = -1f;
        private static Vector3 s_windDir;
        private static float s_savedTransition = -1f;

        private static readonly AccessTools.FieldRef<EnvMan, Vector4> s_wind = AccessTools.FieldRefAccess<EnvMan, Vector4>("m_wind");
        private static readonly AccessTools.FieldRef<EnvMan, Vector4> s_windDir1 = AccessTools.FieldRefAccess<EnvMan, Vector4>("m_windDir1");
        private static readonly AccessTools.FieldRef<EnvMan, float> s_windTimer = AccessTools.FieldRefAccess<EnvMan, float>("m_windTransitionTimer");

        /// <summary>
        /// The Wind Blade's power (HoldPower): a copy of the sword's own secondary swing that hits nothing (attack
        /// type None), whose blow looses the gust. The sword keeps its real secondary attack for short presses.
        /// </summary>
        public static void Register(ItemDrop.ItemData.SharedData shared)
        {
            var swing = shared.m_secondaryAttack.Clone();
            swing.m_attackType = Attack.AttackType.None;
            HoldPower.Register(new HoldPower.Spec
            {
                Token = Plugin.SwordToken, HoldTime = () => Plugin.HoldTime.Value, PowerAttack = swing, Fire = Fire,
                Theme = ChargeFx.Theme.Wind, Cooldown = () => Plugin.GustCooldown.Value, Icon = shared.m_icons?.FirstOrDefault(),
                RestName = "$se_swordwind_rest", RestTooltip = "$se_swordwind_rest_tooltip",
            });
        }

        /// <summary>The gust: hits (timed with the visual), the wind, and the RPC.</summary>
        public static void Fire(Player owner, ItemDrop.ItemData weapon)
        {
            Vector3 dir = Vector3.ProjectOnPlane(owner.transform.forward, Vector3.up).normalized;
            if (dir.sqrMagnitude < 0.01f)
                return;
            Vector3 origin = owner.transform.position + Vector3.up * 1.1f + dir * 0.4f;
            Broadcast(origin, dir);
            var hits = Collect(owner, weapon, origin, dir);
            if (hits.Count > 0)
                owner.RaiseSkill(Skills.SkillType.Swords, 1f);
            AirSlash.Spawn(origin, dir, hits);
            TurnWind(dir);
            Plugin.Log.LogInfo("Wind Blade gust: " + hits.Count + " target(s) in the " + Plugin.SlashRange.Value + " m, " + Plugin.ConeAngle.Value + " degree cone; wind turned to " +
                               Mathf.RoundToInt(Angle(dir)) + " degrees for " + Plugin.WindDuration.Value + " s");
        }

        private static float Angle(Vector3 dir) => Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg;

        // ------------------------------------------------------------------ the cone

        private static float HalfAngle => Mathf.Clamp(Plugin.ConeAngle.Value, 5f, 120f) * 0.5f * Mathf.Deg2Rad;

        /// <summary>Half-width of the cone at this distance ahead (0 outside): straight sides, a round far end.</summary>
        internal static float HalfWidth(float along)
        {
            float L = Plugin.SlashRange.Value, h = HalfAngle;
            if (along < 0f || along > L)
                return 0f;
            if (along <= L * Mathf.Cos(h))
                return along * Mathf.Tan(h);
            return Mathf.Sqrt(Mathf.Max(0f, L * L - along * along));
        }

        internal static bool Inside(Vector3 origin, Vector3 dir, Vector3 p)
        {
            var v = p - origin;
            if (Mathf.Abs(v.y) > 1.8f)
                return false;
            v.y = 0f;
            return v.magnitude <= Plugin.SlashRange.Value && Vector3.Angle(dir, v) * Mathf.Deg2Rad <= HalfAngle + 0.02f;
        }

        /// <summary>The far end of the cone, used to test big colliders reaching into it.</summary>
        internal static Vector3 Far(Vector3 origin, Vector3 dir) => origin + dir * Plugin.SlashRange.Value * 0.75f;

        /// <summary>Damage share at this distance from the swordsman: 1 within 1.5 m, then linear to EdgeDamage.</summary>
        internal static float Share(float dist) =>
            Mathf.Lerp(1f, Plugin.SlashEdgeDamage.Value, Mathf.Clamp01((dist - 1.5f) / Mathf.Max(0.1f, Plugin.SlashRange.Value - 1.5f)));

        private static List<AirSlash.Target> Collect(Player owner, ItemDrop.ItemData weapon, Vector3 origin, Vector3 dir)
        {
            float range = Plugin.SlashRange.Value;
            var bulb = Far(origin, dir);
            var seen = new HashSet<GameObject>();
            var list = new List<AirSlash.Target>();
            foreach (var col in Physics.OverlapSphere(origin + dir * (range / 2f), range / 2f + 1.5f, Geometry.HitMask, QueryTriggerInteraction.Collide))
            {
                var go = Projectile.FindHitObject(col);
                if (go == null || go.transform.root == owner.transform.root || seen.Contains(go))
                    continue;
                var destr = go.GetComponent<IDestructible>();
                if (destr == null || go.GetComponent<WearNTear>() != null)       // never the buildings
                    continue;
                if (destr is Character c && !Geometry.CanHit(owner, c))
                    continue;
                bool convex = !(col is MeshCollider mc) || mc.convex;
                // the part of the thing nearest the zone's axis decides whether it is caught, and how close it is
                Vector3 nearSword = convex ? col.ClosestPoint(origin) : col.bounds.center;
                Vector3 nearBulb = convex ? col.ClosestPoint(bulb) : col.bounds.center;
                Vector3 point = Inside(origin, dir, nearSword) ? nearSword : Inside(origin, dir, nearBulb) ? nearBulb : Vector3.zero;
                if (point == Vector3.zero)
                    continue;
                seen.Add(go);
                var flat = point - origin;
                flat.y = 0f;
                float dist = flat.magnitude;
                float share = Share(dist);

                var hit = new HitData();
                hit.m_damage = weapon.GetDamage();
                hit.m_damage.Modify(Plugin.SlashDamage.Value * share * owner.GetRandomSkillFactor(Skills.SkillType.Swords));
                if (destr.GetDestructibleType() == DestructibleType.Tree)
                    hit.m_damage.m_chop = hit.m_damage.m_slash * 0.6f;              // it cuts wood too
                hit.m_toolTier = (short)Mathf.Max(weapon.m_shared.m_toolTier, 2);
                hit.m_point = point;
                hit.m_dir = flat.sqrMagnitude > 0.01f ? flat.normalized : dir;
                hit.m_pushForce = weapon.m_shared.m_attackForce * 1.5f * share;
                hit.m_staggerMultiplier = share;
                hit.m_blockable = true;
                hit.m_dodgeable = true;
                hit.m_skill = Skills.SkillType.Swords;
                hit.m_hitType = HitData.HitType.PlayerHit;
                hit.m_itemLevel = (short)weapon.m_quality;
                hit.SetAttacker(owner);
                list.Add(new AirSlash.Target { Obj = destr, Go = go, Hit = hit, Along = Vector3.Dot(flat, dir), Effect = weapon.m_shared.m_hitEffect });
                Plugin.Log.LogDebug("  " + go.name + " at " + dist.ToString("F1") + " m: " + (share * 100f).ToString("F0") + " % damage");
            }
            list.Sort((a, b) => a.Along.CompareTo(b.Along));
            return list;
        }

        // ------------------------------------------------------------------ the wind

        /// <summary>
        /// From now on, the weather's wind blows our way (WindOverride patch, keeps its strength); the turn starts at
        /// once from the wind as it is, and takes WindTurnTime seconds.
        /// </summary>
        private static void TurnWind(Vector3 dir)
        {
            var env = EnvMan.instance;
            if (env == null)
                return;
            s_windDir = dir;
            s_windUntil = Time.time + Plugin.WindDuration.Value;
            if (s_savedTransition < 0f)
                s_savedTransition = env.m_windTransitionDuration;
            env.m_windTransitionDuration = Mathf.Max(0.2f, Plugin.WindTurnTime.Value);
            s_windDir1(env) = s_wind(env);       // start from the wind blowing now, even mid-transition
            s_windTimer(env) = -1f;              // and accept the new target on the next update
        }

        /// <summary>The direction the wind must blow now, if a gust is steering it.</summary>
        internal static bool Override(out Vector3 dir)
        {
            dir = s_windDir;
            return s_windUntil >= 0f && Time.time < s_windUntil;
        }

        /// <summary>Every frame, from the plugin: hands the wind back to the weather when the time is up.</summary>
        public static void Tick()
        {
            if (s_windUntil < 0f || Time.time < s_windUntil)
                return;
            s_windUntil = -1f;
            var env = EnvMan.instance;
            if (env != null && s_savedTransition >= 0f)
                env.m_windTransitionDuration = s_savedTransition;
            s_savedTransition = -1f;
            Plugin.Log.LogInfo("Wind Blade: the wind is the weather's again");
        }

        // ------------------------------------------------------------------ seen by everyone

        public static void RegisterRpc()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == s_rpcInstance)
                return;
            ZRoutedRpc.instance.Register<Vector3, Vector3>(RpcName, (sender, pos, dir) =>
            {
                if (sender != ZNet.GetUID())       // the swordsman spawns its own
                    AirSlash.Spawn(pos, dir, null);
            });
            s_rpcInstance = ZRoutedRpc.instance;
        }

        private static void Broadcast(Vector3 pos, Vector3 dir)
        {
            if (ZRoutedRpc.instance != null && ZRoutedRpc.instance == s_rpcInstance)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, RpcName, pos, dir);
        }
    }

    /// <summary>The weather's wind, steered by a gust: our direction, its own strength (cf. ship wind control).</summary>
    [HarmonyPatch(typeof(EnvMan), "SetTargetWind")]
    internal static class WindOverride
    {
        private static void Prefix(ref Vector3 dir)
        {
            if (WindBlade.Override(out var d))
                dir = d;
        }
    }

    /// <summary>
    /// The gust: a wind front sweeping through the cone, as wide as the zone where it is; speed lines and dust
    /// blown ahead; it vanishes at the end of its reach. On the swordsman, each target is hit when the front reaches it.
    /// </summary>
    internal class AirSlash : MonoBehaviour
    {
        internal class Target
        {
            public IDestructible Obj;
            public GameObject Go;
            public HitData Hit;
            public float Along;
            public EffectList Effect;
        }

        private const float Speed = 32f;
        private const float FadeTime = 0.08f;      // the front vanishes as soon as it has gone the whole way
        private const int FrontPoints = 25;
        private const int OutlinePoints = 64;
        private static Material s_material;
        private static int s_ground;

        private Vector3 _origin, _dir, _right;
        private float _range, _age, _front;
        private List<Target> _targets;
        private int _next;
        private LineRenderer _core, _glow;
        private readonly List<LineRenderer> _streaks = new List<LineRenderer>();
        private readonly List<float> _streakOffsets = new List<float>();
        private ParticleSystem _dust;

        public static void Spawn(Vector3 origin, Vector3 dir, List<Target> targets)
        {
            var go = new GameObject("windblade_gust");
            go.transform.position = origin;
            var s = go.AddComponent<AirSlash>();
            s.Build(origin, dir, targets);
            Sfx.Play(Sfx.Slash, origin, null);
            Destroy(go, 2.5f);
        }

        internal static Material Mat()
        {
            if (s_material == null)
                s_material = new Material(Shader.Find("Sprites/Default"));
            return s_material;
        }

        private static float GroundY(Vector3 p, float fallback)
        {
            if (s_ground == 0)
                s_ground = LayerMask.GetMask("terrain", "static_solid", "Default", "piece");
            return Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out var hit, 8f, s_ground, QueryTriggerInteraction.Ignore)
                ? hit.point.y : fallback;
        }

        private LineRenderer Line(string name, float width)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Mat();
            lr.useWorldSpace = true;
            lr.widthMultiplier = width;
            lr.numCapVertices = 3;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private void Build(Vector3 origin, Vector3 dir, List<Target> targets)
        {
            _origin = origin;
            _dir = dir.normalized;
            _right = Vector3.Cross(Vector3.up, _dir).normalized;
            _range = Plugin.SlashRange.Value;
            _targets = targets;
            var crescent = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.05f));
            _glow = Line("glow", 0.7f);
            _glow.widthCurve = crescent;
            _core = Line("core", 0.14f);
            _core.widthCurve = crescent;
            _core.positionCount = _glow.positionCount = FrontPoints;
            for (int i = 0; i < 7; i++)
            {
                var s = Line("streak", 0.05f);
                s.positionCount = 2;
                _streaks.Add(s);
                _streakOffsets.Add(Random.Range(-0.8f, 0.8f));
            }
            _dust = Dust();
            Place();
        }

        private Vector3 Ground(Vector3 p)
        {
            p.y = GroundY(p, _origin.y - 1.1f) + 0.12f;
            return p;
        }

        private ParticleSystem Dust()
        {
            var go = new GameObject("dust");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1f, 3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.15f, 0.45f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.85f, 0.85f, 0.8f, 0.35f), new Color(0.95f, 0.97f, 1f, 0.55f));
            main.gravityModifier = -0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission;
            em.rateOverTime = 0f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Soft;
            return ps;
        }

        private void Place()
        {
            float fade = Mathf.Clamp01((_age - _range / Speed) / FadeTime);
            float a = 1f - fade;
            // the front: an arc across the zone at chest height, as wide as the cone there, its tips lagging
            float half = Mathf.Max(0.3f, WindBlade.HalfWidth(Mathf.Min(_front, _range - 0.01f)));
            var centre = _origin + _dir * _front;
            for (int i = 0; i < FrontPoints; i++)
            {
                float s = (float)i / (FrontPoints - 1) * 2f - 1f;
                var p = centre + _right * s * half - _dir * (0.5f * s * s);
                _core.SetPosition(i, p);
                _glow.SetPosition(i, p - _dir * 0.05f);
            }
            _core.startColor = _core.endColor = new Color(0.95f, 0.98f, 1f, 0.85f * a);
            _glow.startColor = _glow.endColor = new Color(0.7f, 0.85f, 1f, 0.25f * a);
            for (int k = 0; k < _streaks.Count; k++)
            {
                float s = _streakOffsets[k];
                var head = centre + _right * s * half - _dir * (0.5f * s * s + 0.15f) + Vector3.up * Random.Range(-0.3f, 0.3f) * 0.2f;
                var tail = head - _dir * Mathf.Min(2.5f, _front);
                _streaks[k].SetPosition(0, tail);
                _streaks[k].SetPosition(1, head);
                _streaks[k].startColor = new Color(0.9f, 0.95f, 1f, 0f);
                _streaks[k].endColor = new Color(0.9f, 0.95f, 1f, 0.5f * a);
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float before = _front;
            _front = Mathf.Min(_range, _age * Speed);
            if (_front > before)
            {
                // dust and leaves blown up across the zone under the front
                float half = WindBlade.HalfWidth(Mathf.Min(_front, _range - 0.01f));
                var emit = new ParticleSystem.EmitParams { applyShapeToPosition = false };
                for (int i = 0; i < 6; i++)
                {
                    var p = Ground(_origin + _dir * _front + _right * Random.Range(-half, half));
                    emit.position = p + Vector3.up * 0.1f;
                    emit.velocity = _dir * Random.Range(3f, 6f) + Vector3.up * Random.Range(0.5f, 2f);
                    _dust.Emit(emit, 1);
                }
            }
            while (_targets != null && _next < _targets.Count && _targets[_next].Along <= _front)
            {
                var t = _targets[_next++];
                if (t.Go == null)
                    continue;
                t.Obj.Damage(t.Hit);
                t.Effect?.Create(t.Hit.m_point, Quaternion.identity);
            }
            if (_front >= _range)
            {
                // gone the whole way: the front and its streaks vanish at once (no blade left at the end)
                _core.enabled = _glow.enabled = false;
                foreach (var s in _streaks) s.enabled = false;
                return;
            }
            Place();
        }

    }
}
