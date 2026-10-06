using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// Ymir's Bite (Deep North atgeir) and the Giant's Form. Hold the secondary attack: the wielder raises the atgeir
    /// (a staff's ward gesture) and for [YmirBite] GiantDuration becomes a frost giant:
    /// - GiantScale times taller, the weapon frosted over, the frost aura round them, the ground shaking under
    ///   their steps (seen on every client: the end time is in the player's ZDO, each client scales and tints);
    /// - much tougher: physical damage very resisted, elemental damage resisted (so no cold either), barely staggered,
    ///   a little faster, and never out of stamina;
    /// - walking breaks everything in the way: trees, logs, rocks, bushes, and creatures (hit hard and thrown aside);
    ///   never buildings unless [YmirBite] BreakBuildings. The local giant does the breaking.
    /// The player isn't turned into a troll: a troll's skeleton, animations and controls aren't a player's.
    /// </summary>
    internal static class YmirBite
    {
        internal static readonly int UntilHash = "lw_ymir_until".GetStableHashCode();
        internal static GameObject CastSfx, EndSfx, GiantSfx;
        private static SE_Stats s_giant;
        private static float s_nextSmash, s_nextStep;
        private static readonly Dictionary<Player, YmirAura> s_auras = new Dictionary<Player, YmirAura>();
        private static readonly Dictionary<Player, float> s_scale = new Dictionary<Player, float>();
        private static readonly Dictionary<GameObject, float> s_hitAt = new Dictionary<GameObject, float>();
        private static readonly HashSet<Player> s_giants = new HashSet<Player>();

        public static void Register(ItemDrop.ItemData.SharedData atgeir, Sprite icon)
        {
            s_giant = ScriptableObject.CreateInstance<SE_Stats>();
            s_giant.name = "SE_LW_YmirGiant";
            s_giant.m_name = "$se_atgeirymir_giant";
            s_giant.m_tooltip = "$se_atgeirymir_giant_tooltip";
            s_giant.m_icon = icon;
            s_giant.m_ttl = Plugin.GiantDuration.Value;
            s_giant.m_speedModifier = 0.15f;
            s_giant.m_staggerModifier = -0.8f;
            s_giant.m_mods = new List<HitData.DamageModPair>();
            foreach (var t in new[] { HitData.DamageType.Blunt, HitData.DamageType.Slash, HitData.DamageType.Pierce })
                s_giant.m_mods.Add(new HitData.DamageModPair { m_type = t, m_modifier = HitData.DamageModifier.VeryResistant });
            foreach (var t in new[] { HitData.DamageType.Fire, HitData.DamageType.Frost, HitData.DamageType.Lightning, HitData.DamageType.Poison, HitData.DamageType.Spirit })
                s_giant.m_mods.Add(new HitData.DamageModPair { m_type = t, m_modifier = HitData.DamageModifier.Resistant });
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(s_giant, false));
            HoldPower.Register(new HoldPower.Spec
            {
                Token = Plugin.YmirToken, HoldTime = () => Plugin.YmirHoldTime.Value, Fire = (p, w) => Activate(p),
                PowerAttack = RaiseAttack(atgeir),
                Theme = ChargeFx.Theme.Frost, Cooldown = () => Plugin.GiantCooldown.Value, Icon = icon,
                RestName = "$se_atgeirymir_rest", RestTooltip = "$se_atgeirymir_rest_tooltip",
            });
        }

        /// <summary>
        /// The activation: the wielder raises the weapon like a staff calling a ward (the first vanilla staff found),
        /// hitting nothing; the giant's form comes at the blow. No staff: the power comes at once.
        /// </summary>
        private static Attack RaiseAttack(ItemDrop.ItemData.SharedData atgeir)
        {
            foreach (var name in new[] { "StaffShield", "StaffGreenRoots", "StaffSkeleton" })
            {
                var staff = PrefabManager.Cache.GetPrefab<ItemDrop>(name);
                if (staff == null)
                    continue;
                var raise = staff.m_itemData.m_shared.m_attack.Clone();
                raise.m_attackType = Attack.AttackType.None;
                raise.m_startEffect = new EffectList();
                raise.m_triggerEffect = new EffectList();
                raise.m_hitEffect = new EffectList();
                raise.m_hitTerrainEffect = new EffectList();
                raise.m_attackEitr = 0f;
                raise.m_attackHealth = 0f;
                raise.m_attackHealthPercentage = 0f;
                raise.m_attackStamina = atgeir.m_secondaryAttack.m_attackStamina * 0.5f;
                raise.m_attackChainLevels = 0;
                raise.m_attackProjectile = null;
                Plugin.Log.LogInfo("Giant's Form: raised like " + name + " (animation " + raise.m_attackAnimation + ")");
                return raise;
            }
            Plugin.Log.LogWarning("No vanilla staff found: the Giant's Form comes without an animation");
            return null;
        }

        private static void Activate(Player p)
        {
            float duration = Plugin.GiantDuration.Value;
            var nview = p.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid())
                return;
            nview.GetZDO().Set(UntilHash, ZNet.instance.GetTime().Ticks + (long)(duration * 1e7));
            s_giant.m_ttl = duration;
            p.GetSEMan().AddStatusEffect(s_giant, true);
            p.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectFreezing, true);
            p.GetSEMan().RemoveStatusEffect(SEMan.s_statusEffectCold, true);
            Knockback(p);
            Plugin.Log.LogInfo("Giant's Form: x" + Plugin.GiantScale.Value + " for " + duration + " s");
        }

        /// <summary>The blast of the transformation throws back the creatures close by.</summary>
        private static void Knockback(Player p)
        {
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c == p || !Geometry.CanHit(p, c))
                    continue;
                Vector3 d = c.transform.position - p.transform.position;
                float dist = d.magnitude;
                if (dist > 7f)
                    continue;
                var hit = new HitData();
                hit.m_damage.m_frost = 30f * (1f - dist / 7f) + 5f;
                hit.m_point = c.GetCenterPoint();
                hit.m_dir = (d.sqrMagnitude > 0.01f ? d : p.transform.forward).normalized;
                hit.m_pushForce = 120f * (1f - dist / 9f);
                hit.m_staggerMultiplier = 6f;
                hit.m_hitType = HitData.HitType.PlayerHit;
                hit.SetAttacker(p);
                c.Damage(hit);
            }
        }

        /// <summary>Seconds left of a player's giant form (any client), 0 if none.</summary>
        internal static float Left(Player p)
        {
            var nview = p != null ? p.GetComponent<ZNetView>() : null;
            if (nview == null || !nview.IsValid() || ZNet.instance == null)
                return 0f;
            long until = nview.GetZDO().GetLong(UntilHash);
            return until == 0 ? 0f : Mathf.Max(0f, (float)((until - ZNet.instance.GetTime().Ticks) / 1e7));
        }

        /// <summary>Every frame, from the plugin: every player's size, look and aura; the local giant's smashing.</summary>
        public static void Tick()
        {
            var local = Player.m_localPlayer;
            foreach (var p in Player.GetAllPlayers())
            {
                if (p == null)
                    continue;
                float left = Left(p);
                bool giant = left > 0f;
                // the moment it starts or ends, on every client
                if (giant && s_giants.Add(p))
                    GiantBurst.Start(p);
                else if (!giant && s_giants.Remove(p))
                    GiantBurst.End(p);
                // grow in about a second, shrink back in about a second
                s_scale.TryGetValue(p, out var sc);
                if (sc <= 0f) sc = 1f;
                float target = giant ? Mathf.Max(1f, Plugin.GiantScale.Value) : 1f;
                if (!Mathf.Approximately(sc, target) || !Mathf.Approximately(p.transform.localScale.x, target))
                {
                    sc = Mathf.MoveTowards(sc, target, Time.deltaTime * Mathf.Max(1f, Plugin.GiantScale.Value - 1f));
                    p.transform.localScale = Vector3.one * sc;
                }
                s_scale[p] = sc;
                GiantLook.Set(p, giant);
                s_auras.TryGetValue(p, out var aura);
                if (giant)
                {
                    if (aura == null)
                        s_auras[p] = aura = YmirAura.Attach(p);
                    aura.Left = left;
                }
                else if (aura != null)
                {
                    aura.Fade();
                    s_auras.Remove(p);
                    if (p == local)
                    {
                        Sfx.Play(EndSfx, p.transform.position + Vector3.up, p.transform);
                        p.GetSEMan().RemoveStatusEffect(s_giant.NameHash(), true);
                    }
                }
            }
            var gone = new List<Player>();
            foreach (var kv in s_scale)
                if (kv.Key == null) gone.Add(kv.Key);
            foreach (var g in gone) { s_auras.Remove(g); s_scale.Remove(g); s_giants.Remove(g); }
            if (local != null && Left(local) > 0f && !local.IsDead())
                Rampage(local);
        }

        /// <summary>The local giant breaks what stands in its way while it moves (and shakes the ground).</summary>
        private static void Rampage(Player p)
        {
            var vel = p.GetVelocity();
            vel.y = 0f;
            if (vel.magnitude < 1f || Time.time < s_nextSmash)
                return;
            s_nextSmash = Time.time + 0.2f;
            float scale = Mathf.Max(1f, p.transform.localScale.x);
            if (Time.time >= s_nextStep && GameCamera.instance != null && p.IsOnGround())
            {
                s_nextStep = Time.time + 0.55f;
                GameCamera.instance.AddShake(p.transform.position, 8f, 0.35f, false);
            }
            Vector3 dir = vel.normalized;
            Vector3 centre = p.transform.position + dir * (0.7f * scale) + Vector3.up * (0.8f * scale);
            float radius = 0.75f * scale;
            var done = new HashSet<GameObject>();
            int broke = 0;
            foreach (var col in Physics.OverlapSphere(centre, radius, Geometry.HitMask, QueryTriggerInteraction.Ignore))
            {
                var go = Projectile.FindHitObject(col);
                if (go == null || go.transform.root == p.transform.root || !done.Add(go))
                    continue;
                if (s_hitAt.TryGetValue(go, out var at) && Time.time - at < 0.6f)
                    continue;
                var destr = go.GetComponent<IDestructible>();
                if (destr == null || (go.GetComponent<WearNTear>() != null && !Plugin.GiantBreakBuildings.Value))
                    continue;
                var c = destr as Character;
                if (c != null && !Geometry.CanHit(p, c))
                    continue;
                bool convex = !(col is MeshCollider mc) || mc.convex;
                var hit = new HitData();
                float dmg = Plugin.GiantSmashDamage.Value;
                hit.m_damage.m_blunt = dmg;
                hit.m_damage.m_chop = dmg * 3f;
                hit.m_damage.m_pickaxe = dmg * 3f;
                hit.m_toolTier = 10;
                hit.m_point = convex ? col.ClosestPoint(centre) : col.bounds.center;
                hit.m_dir = dir;
                hit.m_pushForce = c != null ? 60f : 0f;
                hit.m_staggerMultiplier = 4f;
                hit.m_skill = Skills.SkillType.Polearms;
                hit.m_hitType = HitData.HitType.PlayerHit;
                hit.SetAttacker(p);
                destr.Damage(hit);
                s_hitAt[go] = Time.time;
                broke++;
            }
            if (broke > 0)
            {
                Sfx.Play(Sfx.Quake, centre, null);
                if (s_hitAt.Count > 200)
                    s_hitAt.Clear();
            }
        }

        /// <summary>Lifted by lw_reset: the giant's form ends at once (the cooldown is HoldPower's).</summary>
        internal static void End(Player p)
        {
            var nview = p != null ? p.GetComponent<ZNetView>() : null;
            if (nview != null && nview.IsValid() && nview.IsOwner())
                nview.GetZDO().Set(UntilHash, 0L);
        }
    }

    /// <summary>The giant never tires: stamina isn't spent while the form lasts.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.UseStamina))]
    internal static class GiantStamina
    {
        private static bool Prefix(Player __instance) => !(__instance == Player.m_localPlayer && YmirBite.Left(__instance) > 0f);
    }

    /// <summary>A giant's weapon frosted over (tinted copies of its materials), on every client; the player keeps their look.</summary>
    internal static class GiantLook
    {
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_rightInstance = AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");
        private static readonly Dictionary<Player, Dictionary<Renderer, Material[]>> s_saved = new Dictionary<Player, Dictionary<Renderer, Material[]>>();
        private static readonly Dictionary<Material, Material> s_frost = new Dictionary<Material, Material>();
        private static float s_next;

        internal static void Set(Player p, bool giant)
        {
            bool shown = s_saved.ContainsKey(p);
            if (!giant)
            {
                if (shown) Restore(p);
                return;
            }
            if (shown && Time.time < s_next)
                return;
            s_next = Time.time + 0.5f;
            Apply(p);
        }

        private static Material Frost(Material src)
        {
            if (src == null)
                return null;
            if (s_frost.TryGetValue(src, out var m))
                return m;
            m = new Material(src) { name = src.name + "_frost" };
            if (m.HasProperty("_Color"))
                m.SetColor("_Color", m.GetColor("_Color") * new Color(0.72f, 0.88f, 1.15f, 1f));
            if (m.HasProperty("_SkinColor"))
                m.SetColor("_SkinColor", new Color(0.62f, 0.78f, 0.95f));
            if (m.HasProperty("_EmissionColor"))
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", new Color(0.05f, 0.09f, 0.14f));
            }
            s_frost[src] = m;
            return m;
        }

        private static void Apply(Player p)
        {
            if (!s_saved.TryGetValue(p, out var saved))
                s_saved[p] = saved = new Dictionary<Renderer, Material[]>();
            var vis = p.GetComponent<VisEquipment>();
            var weapon = vis != null ? s_rightInstance(vis) : null;
            if (weapon == null)
                return;
            foreach (var r in weapon.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || saved.ContainsKey(r))
                    continue;
                var mats = r.sharedMaterials;
                saved[r] = mats;
                var frost = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++) frost[i] = Frost(mats[i]);
                r.sharedMaterials = frost;
            }
        }

        private static void Restore(Player p)
        {
            foreach (var kv in s_saved[p])
                if (kv.Key != null)
                    kv.Key.sharedMaterials = kv.Value;
            s_saved.Remove(p);
        }
    }

    /// <summary>
    /// The frost aura round a giant: slow frost crystals drifting up round the body, a thin pale mist on
    /// the ground, a faint cold light (no bloom by day). Fades over the last seconds.
    /// </summary>
    internal class YmirAura : MonoBehaviour
    {
        internal float Left = 999f;
        private ParticleSystem _crystals, _mist;
        private Light _light;
        private Transform _player;
        private bool _fading;

        internal static YmirAura Attach(Player p)
        {
            var go = new GameObject("ymir_aura");
            go.transform.SetParent(p.transform, false);
            var aura = go.AddComponent<YmirAura>();
            aura._player = p.transform;
            aura.Build();
            return aura;
        }

        internal void Fade()
        {
            _fading = true;
            foreach (var ps in new[] { _crystals, _mist })
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (_light != null) _light.enabled = false;
            Destroy(gameObject, 3f);
        }

        private ParticleSystem System(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 150;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Soft;
            return ps;
        }

        private void Build()
        {
            _crystals = System("crystals");
            var main = _crystals.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.8f, 3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.9f, 1f, 0.75f), new Color(1f, 1f, 1f, 0.9f));
            main.gravityModifier = -0.02f;
            var shape = _crystals.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 0f;
            shape.radius = Plugin.YmirAuraVisual.Value;
            shape.radiusThickness = 0.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            _crystals.transform.localPosition = new Vector3(0f, 0.1f, 0f);
            var vel = _crystals.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.orbitalY = new ParticleSystem.MinMaxCurve(0.6f);
            vel.y = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            var col = _crystals.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var twinkle = _crystals.sizeOverLifetime;
            twinkle.enabled = true;
            twinkle.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(0.3f, 1.2f),
                new Keyframe(0.5f, 0.7f), new Keyframe(0.75f, 1.1f), new Keyframe(1f, 0.4f)));
            var em = _crystals.emission;
            em.rateOverTime = 14f;
            _crystals.Play();

            _mist = System("mist");
            var mm = _mist.main;
            mm.startLifetime = new ParticleSystem.MinMaxCurve(2f, 3f);
            mm.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            mm.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.2f);
            mm.startColor = new ParticleSystem.MinMaxGradient(new Color(0.78f, 0.88f, 1f, 0.06f), new Color(0.9f, 0.95f, 1f, 0.1f));
            mm.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var ms = _mist.shape;
            ms.shapeType = ParticleSystemShapeType.Circle;
            ms.radius = Plugin.YmirAuraVisual.Value * 0.8f;
            ms.rotation = new Vector3(-90f, 0f, 0f);
            _mist.transform.localPosition = new Vector3(0f, 0.08f, 0f);
            var mc = _mist.colorOverLifetime;
            mc.enabled = true;
            var mg = new Gradient();
            mg.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0f, 1f) });
            mc.color = mg;
            var mem = _mist.emission;
            mem.rateOverTime = 5f;
            _mist.Play();

            var lg = new GameObject("light");
            lg.transform.SetParent(transform, false);
            lg.transform.localPosition = new Vector3(0f, 1f, 0f);
            _light = lg.AddComponent<Light>();
            _light.type = LightType.Point;
            _light.color = new Color(0.6f, 0.8f, 1f);
            _light.range = 3f;
            _light.intensity = 0.25f;
        }

        private void Update()
        {
            if (_fading || _player == null)
                return;
            float k = Mathf.Clamp01(Left / 5f);                      // the last seconds fade
            var em = _crystals.emission;
            em.rateOverTime = 3f + 11f * k;
            var mem = _mist.emission;
            mem.rateOverTime = 1f + 4f * k;
            _light.intensity = 0.25f * k;
        }

        /// <summary>The cast: a ring of frost bursting out from the wielder and a puff of frozen breath.</summary>
        internal static void Cast(Player p)
        {
            var go = new GameObject("ymir_cast");
            go.transform.position = p.transform.position + Vector3.up * 0.3f;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.7f, 0.88f, 1f, 0.9f), new Color(1f, 1f, 1f, 0.95f));
            main.gravityModifier = 0.05f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 300;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 160) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.4f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.5f;
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Soft;
            ps.Play();
            Destroy(go, 2f);
        }
    }

    /// <summary>
    /// The transformation, seen by everyone: a flash of cold light, a ring of frost racing out over the ground, ice
    /// shards and snow thrown up, a column of frozen mist rising round the growing giant, a deep boom and the ground
    /// shaking. Its end: a softer puff of snow as the giant shrinks back.
    /// </summary>
    internal static class GiantBurst
    {
        private static Mesh s_cube;

        internal static void Start(Player p)
        {
            var at = p.transform.position;
            Sfx.Play(YmirBite.GiantSfx, at + Vector3.up, p.transform);      // dull and deep (Lekinox)
            if (GameCamera.instance != null)
                GameCamera.instance.AddShake(at, 25f, 2f, false);
            YmirAura.Cast(p);
            var root = new GameObject("ymir_giant_burst");
            root.transform.position = at;
            Object.Destroy(root, 4f);
            Flash(root.transform);
            Ring(root.transform);
            Shards(root.transform);
            Snow(root.transform, 160, 7f, 1.6f);
            Column(root.transform);
        }

        internal static void End(Player p)
        {
            var root = new GameObject("ymir_giant_end");
            root.transform.position = p.transform.position;
            Object.Destroy(root, 3f);
            Snow(root.transform, 70, 3f, 1.2f);
        }

        private static ParticleSystem System(Transform parent, string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.2f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            var em = ps.emission;
            em.rateOverTime = 0f;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            return ps;
        }

        private static void Fade(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
        }

        private static void Burst(ParticleSystem ps, int n)
        {
            var em = ps.emission;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)n) });
            ps.Play();
        }

        private static void Flash(Transform root)
        {
            var go = new GameObject("flash");
            go.transform.SetParent(root, false);
            go.transform.localPosition = Vector3.up * 1.5f;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.65f, 0.85f, 1f);
            light.range = 14f;
            light.intensity = 3.5f;
            go.AddComponent<LightFade>().Seconds = 0.8f;
        }

        /// <summary>A ring of frost racing out over the ground (a burst on a circle, flying outwards).</summary>
        private static void Ring(Transform root)
        {
            var ps = System(root, "ring", Fx.Soft);
            ps.transform.localPosition = Vector3.up * 0.15f;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 13f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.92f, 1f, 0.55f), new Color(1f, 1f, 1f, 0.7f));
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 0.6f;
            shape.radiusThickness = 0f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 3f;
            Fade(ps);
            Burst(ps, 220);
        }

        /// <summary>Ice shards (small tumbling cubes, pale blue) thrown up and falling back.</summary>
        private static void Shards(Transform root)
        {
            var ps = System(root, "shards", Fx.Plain);
            ps.transform.localPosition = Vector3.up * 0.5f;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 11f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.16f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.65f, 0.85f, 1f, 0.95f), new Color(0.9f, 0.97f, 1f, 0.95f));
            main.gravityModifier = 1.4f;
            main.startRotation3D = true;
            main.startRotationX = main.startRotationY = main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.6f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.separateAxes = true;
            rot.x = rot.y = rot.z = new ParticleSystem.MinMaxCurve(-8f, 8f);
            if (s_cube == null)
            {
                var tmp = GameObject.CreatePrimitive(PrimitiveType.Cube);
                s_cube = tmp.GetComponent<MeshFilter>().sharedMesh;
                Object.Destroy(tmp);
            }
            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Mesh;
            r.mesh = s_cube;
            Burst(ps, 70);
        }

        /// <summary>Snow thrown out and hanging in the air.</summary>
        private static void Snow(Transform root, int count, float speed, float life)
        {
            var ps = System(root, "snow", Fx.Soft);
            ps.transform.localPosition = Vector3.up * 0.8f;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.3f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.12f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.95f, 1f, 0.9f), Color.white);
            main.gravityModifier = 0.15f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2f;
            Fade(ps);
            Burst(ps, count);
        }

        /// <summary>A column of frozen mist swirling up round the player while they grow.</summary>
        private static void Column(Transform root)
        {
            var ps = System(root, "column", Fx.Soft);
            var main = ps.main;
            main.duration = 1.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1f, 1.6f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.5f, 1.1f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.75f, 0.88f, 1f, 0.22f), new Color(0.95f, 0.98f, 1f, 0.3f));
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = 1.3f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.orbitalY = new ParticleSystem.MinMaxCurve(2.5f);
            vel.y = new ParticleSystem.MinMaxCurve(2.5f, 4f);
            vel.radial = new ParticleSystem.MinMaxCurve(-0.3f);
            Fade(ps);
            var em = ps.emission;
            em.rateOverTime = 90f;
            ps.Play();
        }
    }

    /// <summary>A light that fades out and goes.</summary>
    internal class LightFade : MonoBehaviour
    {
        internal float Seconds = 0.5f;
        private Light _light;
        private float _start, _age;

        private void Start()
        {
            _light = GetComponent<Light>();
            _start = _light != null ? _light.intensity : 0f;
        }

        private void Update()
        {
            _age += Time.deltaTime;
            if (_light != null)
                _light.intensity = _start * Mathf.Clamp01(1f - _age / Seconds);
            if (_age >= Seconds)
                Destroy(gameObject);
        }
    }

    /// <summary>
    /// A giant is heavy: its animations play at [YmirBite] AnimationSpeed (0.8). The game sets the local player's
    /// animator back to 1 every physics step when not attacking, and attacks set their own speed: both are scaled.
    /// </summary>
    internal static class GiantAnimation
    {
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Animator> s_animator = AccessTools.FieldRefAccess<CharacterAnimEvent, Animator>("m_animator");
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Character> s_character = AccessTools.FieldRefAccess<CharacterAnimEvent, Character>("m_character");

        private static bool Giant(CharacterAnimEvent e)
        {
            var c = s_character(e);
            return c != null && c == Player.m_localPlayer && YmirBite.Left((Player)c) > 0f;
        }

        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomFixedUpdate))]
        private static class Idle
        {
            private static void Postfix(CharacterAnimEvent __instance)
            {
                if (!Giant(__instance))
                    return;
                var a = s_animator(__instance);
                if (a != null && Mathf.Approximately(a.speed, 1f))
                    a.speed = Plugin.GiantAnimSpeed.Value;
            }
        }

        [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.Speed))]
        private static class Attacks
        {
            private static void Prefix(CharacterAnimEvent __instance, ref float speedScale)
            {
                if (Giant(__instance))
                    speedScale *= Plugin.GiantAnimSpeed.Value;
            }
        }
    }
}
