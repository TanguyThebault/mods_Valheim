using System.Collections.Generic;
using HarmonyLib;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// The Fox Fang (Black Forest knife): hold the secondary attack to vanish into the forest for CamoDuration
    /// seconds, CamoCooldown seconds apart. A short press keeps the knife's own secondary attack (the lunge).
    /// - Creatures neither see nor hear a camouflaged player further than RevealDistance; those hunting them
    ///   within 40 m lose the trail.
    /// - The first blow struck while camouflaged (any weapon) does AmbushMultiplier times the damage and ends it;
    ///   the vanilla sneak attack bonus comes on top, the target being unaware.
    /// - The player becomes a dim, see-through green silhouette, on every client (the state is in the player's
    ///   ZDO), with a swirl of dead leaves and a rustle as they fade out and back in.
    /// </summary>
    internal static class FoxFang
    {
        private static readonly int s_camoHash = "lw_camo".GetStableHashCode();
        private static float s_until = -1f, s_cooldownUntil = -1f;
        private static SE_Stats s_effect, s_rest;

        public static void Register(Sprite icon)
        {
            // the cooldown, shown discreetly as a status icon with its timer (no message)
            s_rest = ScriptableObject.CreateInstance<SE_Stats>();
            s_rest.name = "SE_LW_FoxRest";
            s_rest.m_name = "$se_knifefox_rest";
            s_rest.m_tooltip = "$se_knifefox_rest_tooltip";
            s_rest.m_icon = icon;
            s_rest.m_ttl = Plugin.CamoCooldown.Value;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(s_rest, false));
            s_effect = ScriptableObject.CreateInstance<SE_Stats>();
            s_effect.name = "SE_LW_Camouflage";
            s_effect.m_name = "$se_knifefox_camo";
            s_effect.m_tooltip = "$se_knifefox_camo_tooltip";
            s_effect.m_icon = icon;
            s_effect.m_ttl = Plugin.CamoDuration.Value;
            s_effect.m_noiseModifier = -0.5f;
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(s_effect, false));
            HoldPower.Register(new HoldPower.Spec
            {
                Token = Plugin.KnifeToken, HoldTime = () => Plugin.CamoHoldTime.Value, Fire = (p, w) => Activate(p),
                CanStart = CanStart, Theme = ChargeFx.Theme.Leaves,
            });
        }

        /// <summary>Whether this player (anyone, on any client) is camouflaged.</summary>
        internal static bool Camouflaged(Player p)
        {
            var nview = p != null ? p.GetComponent<ZNetView>() : null;
            return nview != null && nview.IsValid() && nview.GetZDO().GetBool(s_camoHash);
        }

        internal static bool ActiveLocally => s_until >= 0f;

        internal static void ResetCooldown() => s_cooldownUntil = -1f;

        private static bool CanStart(Player p)
        {
            if (ActiveLocally)
                return false;
            return Time.time >= s_cooldownUntil;       // resting: the press is the knife's own secondary attack
        }

        private static void Activate(Player p)
        {
            s_until = Time.time + Plugin.CamoDuration.Value;
            p.GetComponent<ZNetView>().GetZDO().Set(s_camoHash, true);
            s_effect.m_ttl = Plugin.CamoDuration.Value;
            p.GetSEMan().AddStatusEffect(s_effect, true);
            int lost = 0;
            foreach (var c in Character.GetAllCharacters())
            {
                if (c == null || c.IsPlayer() || Vector3.Distance(c.transform.position, p.transform.position) > 40f)
                    continue;
                if (CamoAI.Forget(c.GetBaseAI(), p))
                    lost++;
            }
            Plugin.Log.LogInfo("Fox Fang: camouflaged for " + Plugin.CamoDuration.Value + " s, " + lost + " creature(s) lost the trail");
        }

        internal static void End(string why)
        {
            if (!ActiveLocally)
                return;
            s_until = -1f;
            s_cooldownUntil = Time.time + Plugin.CamoCooldown.Value;
            var p = Player.m_localPlayer;
            if (p != null)
            {
                var nview = p.GetComponent<ZNetView>();
                if (nview != null && nview.IsValid())
                    nview.GetZDO().Set(s_camoHash, false);
                p.GetSEMan().RemoveStatusEffect(s_effect.NameHash(), true);
                s_rest.m_ttl = Plugin.CamoCooldown.Value;
                p.GetSEMan().AddStatusEffect(s_rest, true);
            }
            Plugin.Log.LogInfo("Fox Fang: camouflage over (" + why + "), ready again in " + Plugin.CamoCooldown.Value + " s");
        }

        /// <summary>Every frame, from the plugin: the timer, and every player's look.</summary>
        public static void Tick()
        {
            if (ActiveLocally && (Time.time >= s_until || Player.m_localPlayer == null || Player.m_localPlayer.IsDead()))
                End(Player.m_localPlayer == null || Player.m_localPlayer.IsDead() ? "dead" : "time");
            CamoLook.Tick();
        }
    }

    /// <summary>Creatures forget a player who vanishes, and can't sense one in camouflage beyond RevealDistance.</summary>
    internal static class CamoAI
    {
        private static readonly AccessTools.FieldRef<MonsterAI, Character> s_monsterTarget = AccessTools.FieldRefAccess<MonsterAI, Character>("m_targetCreature");
        private static readonly AccessTools.FieldRef<AnimalAI, Character> s_animalTarget = AccessTools.FieldRefAccess<AnimalAI, Character>("m_target");

        internal static bool Forget(BaseAI ai, Player p)
        {
            bool lost = false;
            if (ai is MonsterAI m && s_monsterTarget(m) == p)
            {
                s_monsterTarget(m) = null;
                lost = true;
            }
            else if (ai is AnimalAI a && s_animalTarget(a) == p)
            {
                s_animalTarget(a) = null;
                lost = true;
            }
            if (lost)
                Traverse.Create(ai).Method("SetAlerted", false).GetValue();
            return lost;
        }

        internal static bool Hidden(Transform me, Character target) =>
            target is Player p && FoxFang.Camouflaged(p) && Vector3.Distance(me.position, p.transform.position) > Plugin.RevealDistance.Value;

        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanSeeTarget),
            new[] { typeof(Transform), typeof(Vector3), typeof(float), typeof(float), typeof(bool), typeof(bool), typeof(Character) })]
        private static class NotSeen
        {
            private static bool Prefix(Transform me, Character target, ref bool __result)
            {
                if (me == null || !Hidden(me, target))
                    return true;
                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.CanHearTarget), new[] { typeof(Transform), typeof(float), typeof(Character) })]
        private static class NotHeard
        {
            private static bool Prefix(Transform me, Character target, ref bool __result)
            {
                if (me == null || !Hidden(me, target))
                    return true;
                __result = false;
                return false;
            }
        }

        /// <summary>The ambush: the first blow from the camouflaged local player hits AmbushMultiplier times as hard.</summary>
        [HarmonyPatch(typeof(Character), nameof(Character.Damage))]
        private static class Ambush
        {
            private static void Prefix(Character __instance, HitData hit)
            {
                if (!FoxFang.ActiveLocally || hit == null || __instance.IsPlayer() || hit.GetAttacker() != Player.m_localPlayer)
                    return;
                hit.m_damage.Modify(Plugin.AmbushMultiplier.Value);
                Plugin.Log.LogDebug("Fox Fang ambush on " + __instance.name + ": x" + Plugin.AmbushMultiplier.Value +
                                    " (" + hit.GetTotalDamage().ToString("F0") + " before resistances)");
                FoxFang.End("ambush");
            }
        }
    }

    /// <summary>
    /// The look of camouflaged players, on every client: all their renderers (body and gear) wear see-through, dim
    /// green copies of their materials (same textures), re-applied twice a second in case the gear changes; the
    /// real materials come back at the end. Leaves swirl and the forest rustles at both ends.
    /// </summary>
    internal static class CamoLook
    {
        private static readonly Dictionary<Player, Dictionary<Renderer, Material[]>> s_saved = new Dictionary<Player, Dictionary<Renderer, Material[]>>();
        private static readonly Dictionary<Material, Material> s_ghost = new Dictionary<Material, Material>();
        private static float s_next;

        public static void Tick()
        {
            if (Time.time < s_next)
                return;
            s_next = Time.time + 0.5f;
            foreach (var p in Player.GetAllPlayers())
            {
                if (p == null)
                    continue;
                bool camo = FoxFang.Camouflaged(p);
                bool shown = s_saved.ContainsKey(p);
                if (camo)
                {
                    if (!shown)
                        Burst(p);
                    Apply(p);
                }
                else if (shown)
                {
                    Restore(p);
                    Burst(p);
                }
            }
            // players who left while camouflaged
            var gone = new List<Player>();
            foreach (var kv in s_saved)
                if (kv.Key == null)
                    gone.Add(kv.Key);
            foreach (var g in gone) s_saved.Remove(g);
        }

        private static Material Ghost(Material src)
        {
            if (src == null)
                return null;
            if (s_ghost.TryGetValue(src, out var m))
                return m;
            m = new Material(AirSlash.Mat()) { name = src.name + "_camo" };
            if (src.HasProperty("_MainTex") && src.mainTexture != null)
                m.mainTexture = src.mainTexture;
            m.color = new Color(0.32f, 0.42f, 0.28f, 0.22f);
            s_ghost[src] = m;
            return m;
        }

        private static void Apply(Player p)
        {
            if (!s_saved.TryGetValue(p, out var saved))
                s_saved[p] = saved = new Dictionary<Renderer, Material[]>();
            foreach (var r in p.GetComponentsInChildren<Renderer>())
            {
                if (r is ParticleSystemRenderer || r is LineRenderer || saved.ContainsKey(r))
                    continue;
                var mats = r.sharedMaterials;
                saved[r] = mats;
                var ghost = new Material[mats.Length];
                for (int i = 0; i < mats.Length; i++) ghost[i] = Ghost(mats[i]);
                r.sharedMaterials = ghost;
            }
        }

        private static void Restore(Player p)
        {
            foreach (var kv in s_saved[p])
                if (kv.Key != null)
                    kv.Key.sharedMaterials = kv.Value;
            s_saved.Remove(p);
        }

        /// <summary>Dead leaves and a breath of mist whirling up around the player, and a rustle.</summary>
        private static void Burst(Player p)
        {
            var at = p.transform.position + Vector3.up * 0.9f;
            Sfx.Play(Sfx.Rustle, at, null);
            var go = new GameObject("knifefox_leaves");
            go.transform.position = at;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f, 1.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.5f, 2.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.16f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.45f, 0.3f, 0.12f, 0.9f), new Color(0.35f, 0.42f, 0.15f, 0.9f));
            main.gravityModifier = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 70) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.6f;
            var orbit = ps.velocityOverLifetime;
            orbit.enabled = true;
            orbit.orbitalY = new ParticleSystem.MinMaxCurve(4f);
            orbit.y = new ParticleSystem.MinMaxCurve(0.8f);
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-4f, 4f);
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = Fx.Leaf;
            ps.Play();
            Object.Destroy(go, 2.5f);
        }
    }
}
