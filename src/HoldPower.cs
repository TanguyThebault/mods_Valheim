using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// Powers on a held secondary attack (local player), for every legendary weapon except the Thunder Spear (which
    /// has its own controls). The weapon's own secondary attack is never replaced:
    /// - a press released before HoldTime is the weapon's base secondary attack, on release;
    /// - held HoldTime (the charge shows on the hand), the power goes: either a power attack of its own (an
    ///   animation, swapped in only while the attack starts, see PowerAttackSwap), whose blow fires the power, or
    ///   (no power attack) the power fires at once.
    /// - CanStart can refuse (cooldown): the press is then the base secondary attack straight away.
    /// </summary>
    internal static class HoldPower
    {
        internal class Spec
        {
            public string Token;                                   // the weapon's $item_ name
            public System.Func<float> HoldTime;
            public Attack PowerAttack;                             // null: the power fires at once
            public System.Action<Player, ItemDrop.ItemData> Fire;  // on the power attack's blow (or at once); may be null
            public ChargeFx.Theme Theme = ChargeFx.Theme.Wind;
            public System.Func<Player, bool> CanStart;
            // optional cooldown after the power, shown as a status icon with its timer
            public System.Func<float> Cooldown;
            public string RestName, RestTooltip;
            public Sprite Icon;
            public bool NoAmmo;                        // the power attack needs none of the weapon's ammo (bait)
            internal SE_Stats Rest;
            internal float ReadyAt;
        }

        /// <summary>Test (lw_reset): every held power ready again.</summary>
        internal static void ResetAll()
        {
            foreach (var spec in s_specs.Values) spec.ReadyAt = 0f;
        }

        internal static bool Ready(Spec spec) => spec.Cooldown == null || Time.time >= spec.ReadyAt;

        /// <summary>The power went: start its cooldown, then fire it.</summary>
        internal static void Fire(Spec spec, Player p, ItemDrop.ItemData weapon)
        {
            if (spec.Cooldown != null)
            {
                spec.ReadyAt = Time.time + spec.Cooldown();
                if (spec.Rest != null)
                {
                    spec.Rest.m_ttl = spec.Cooldown();
                    p.GetSEMan().AddStatusEffect(spec.Rest, true);
                }
            }
            spec.Fire?.Invoke(p, weapon);
        }

        private static readonly Dictionary<string, Spec> s_specs = new Dictionary<string, Spec>();
        private static readonly AccessTools.FieldRef<Humanoid, bool> s_secondary =
            AccessTools.FieldRefAccess<Humanoid, bool>("m_currentAttackIsSecondary");

        /// <summary>Asked for by the controls: the next secondary attack started is this power's attack.</summary>
        internal static Spec Requested;
        internal static float RequestedAt;
        /// <summary>The power whose attack is under way: its blow fires the power.</summary>
        internal static Spec Active;
        internal static float ActiveSince;

        /// <summary>Seconds of holding before the charge effect shows.</summary>
        internal const float ChargeDelay = 0.18f;

        public static void Register(Spec spec)
        {
            if (spec.Cooldown != null && spec.RestName != null)
            {
                spec.Rest = ScriptableObject.CreateInstance<SE_Stats>();
                spec.Rest.name = "SE_LW_Rest_" + spec.Token.TrimStart('$');
                spec.Rest.m_name = spec.RestName;
                spec.Rest.m_tooltip = spec.RestTooltip;
                spec.Rest.m_icon = spec.Icon;
                spec.Rest.m_ttl = spec.Cooldown();
                Jotunn.Managers.ItemManager.Instance.AddStatusEffect(new Jotunn.Entities.CustomStatusEffect(spec.Rest, false));
            }
            s_specs[spec.Token] = spec;
        }

        internal static bool IsSecondary(Humanoid h) => s_secondary(h);

        internal static Spec For(Humanoid h)
        {
            var w = h != null ? h.GetCurrentWeapon() : null;
            return w != null && s_specs.TryGetValue(w.m_shared.m_name, out var s) ? s : null;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class HoldPowerControls
    {
        private static float s_holdStart = -1f;
        private static bool s_passThrough, s_waitRelease;
        private static ChargeFx s_charge;

        private static void Prefix(Player __instance, ref bool secondaryAttack, ref bool secondaryAttackHold)
        {
            if (__instance != Player.m_localPlayer)
                return;
            if (HoldPower.Requested != null && Time.time - HoldPower.RequestedAt > 0.3f)
                HoldPower.Requested = null;          // the power attack could not start (staggered, out of stamina...)
            var spec = HoldPower.For(__instance);
            bool held = secondaryAttack || secondaryAttackHold;
            if (spec == null)
            {
                Reset();
                return;
            }
            if (s_waitRelease)
            {
                // the power went: no base attack (nor repeated attacks from holding) until the button is let go
                if (!held) s_waitRelease = false;
                secondaryAttack = secondaryAttackHold = false;
                if (HoldPower.Requested != null && Time.time - HoldPower.RequestedAt < 0.05f)
                {
                    secondaryAttack = true;          // ...except the power attack itself, this frame
                    StartDirect(__instance);
                }
                return;
            }
            if (s_passThrough)
            {
                // refused (cooldown): the weapon's own secondary attack, untouched, until the button is let go
                if (!held) s_passThrough = false;
                return;
            }
            if (!held)
            {
                bool shortPress = s_holdStart >= 0f;
                Reset();
                if (shortPress)
                {
                    secondaryAttack = true;          // released before the charge: the base secondary attack
                    secondaryAttackHold = false;
                }
                return;
            }
            if (s_holdStart < 0f)
            {
                if (!HoldPower.Ready(spec) || (spec.CanStart != null && !spec.CanStart(__instance)))
                {
                    s_passThrough = true;
                    return;
                }
                s_holdStart = Time.time;
            }
            // the charge shows only once the press is clearly a hold (a quick click shows nothing)
            if (s_charge == null && Time.time - s_holdStart >= HoldPower.ChargeDelay)
                s_charge = ChargeFx.Begin(__instance, spec.Theme, Mathf.Max(0.05f, spec.HoldTime() - HoldPower.ChargeDelay));
            if (Time.time - s_holdStart >= spec.HoldTime())
            {
                Reset();
                s_waitRelease = true;
                if (spec.PowerAttack != null)
                {
                    HoldPower.Requested = spec;
                    HoldPower.RequestedAt = Time.time;
                    secondaryAttack = true;          // starts the power attack (PowerAttackSwap)
                    StartDirect(__instance);
                }
                else
                {
                    HoldPower.Fire(spec, __instance, __instance.GetCurrentWeapon());   // no attack at all (camouflage)
                    secondaryAttack = false;
                }
                secondaryAttackHold = false;
                return;
            }
            secondaryAttack = secondaryAttackHold = false;
        }

        /// <summary>
        /// Weapons whose primary attack is drawn like a bow (the fishing rod) never get their secondary attack from
        /// the input (Player.PlayerAttackInput takes the bow-draw path instead): the power attack is started here.
        /// </summary>
        private static void StartDirect(Player p)
        {
            var w = p.GetCurrentWeapon();
            if (w != null && w.m_shared.m_attack.m_bowDraw && HoldPower.Requested != null)
                p.StartAttack(null, true);
        }

        private static void Reset()
        {
            s_holdStart = -1f;
            if (s_charge != null)
            {
                s_charge.Stop();
                s_charge = null;
            }
        }
    }

    /// <summary>
    /// The power attack replaces the weapon's secondary attack only for the instant the attack starts (StartAttack
    /// clones it), so the item keeps its own secondary attack for every other press.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.StartAttack))]
    internal static class PowerAttackSwap
    {
        private static string s_ammo;

        private static void Prefix(Humanoid __instance, bool secondaryAttack, out Attack __state)
        {
            __state = null;
            var spec = HoldPower.Requested;
            if (spec == null || __instance != Player.m_localPlayer || !secondaryAttack || HoldPower.For(__instance) != spec)
                return;
            var shared = __instance.GetCurrentWeapon().m_shared;
            __state = shared.m_secondaryAttack;
            shared.m_secondaryAttack = spec.PowerAttack;
            s_ammo = shared.m_ammoType;
            if (spec.NoAmmo)
                shared.m_ammoType = "";               // no bait needed to start the power attack
        }

        private static void Postfix(Humanoid __instance, bool __result, Attack __state)
        {
            if (__state == null)
                return;
            var shared = __instance.GetCurrentWeapon().m_shared;
            shared.m_secondaryAttack = __state;
            shared.m_ammoType = s_ammo;
            if (__result)
            {
                HoldPower.Active = HoldPower.Requested;
                HoldPower.ActiveSince = Time.time;
            }
            HoldPower.Requested = null;
        }
    }

    /// <summary>
    /// A power attack (type None) does nothing of its own at the blow: no weapon swing effect left hanging at the
    /// end of the animation (seen as a little wind-blade on the sword), no wear; the power does it all.
    /// </summary>
    [HarmonyPatch(typeof(Attack), "DoNonAttack")]
    internal static class PowerAttackNoEffects
    {
        private static readonly AccessTools.FieldRef<Attack, Humanoid> s_character = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");

        private static bool Prefix(Attack __instance) =>
            !(HoldPower.Active != null && s_character(__instance) == Player.m_localPlayer);
    }

    /// <summary>A power attack that needs no ammo doesn't use any at its blow either (the rod's bait).</summary>
    [HarmonyPatch(typeof(Attack), nameof(Attack.OnAttackTrigger))]
    internal static class PowerAttackNoAmmo
    {
        private static readonly AccessTools.FieldRef<Attack, Humanoid> s_character = AccessTools.FieldRefAccess<Attack, Humanoid>("m_character");
        private static readonly AccessTools.FieldRef<Attack, ItemDrop.ItemData> s_weapon = AccessTools.FieldRefAccess<Attack, ItemDrop.ItemData>("m_weapon");

        private static void Prefix(Attack __instance, out string __state)
        {
            __state = null;
            var spec = HoldPower.Active;
            if (spec == null || !spec.NoAmmo || s_character(__instance) != Player.m_localPlayer)
                return;
            var w = s_weapon(__instance);
            if (w == null)
                return;
            __state = w.m_shared.m_ammoType;
            w.m_shared.m_ammoType = "";
        }

        private static void Postfix(Attack __instance, string __state)
        {
            if (__state != null)
                s_weapon(__instance).m_shared.m_ammoType = __state;
        }
    }

    /// <summary>The power fires when its attack's blow lands (OnAttackTrigger), on the local player.</summary>
    [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.OnAttackTrigger))]
    internal static class HoldPowerTrigger
    {
        private static void Postfix(Humanoid __instance)
        {
            var spec = HoldPower.Active;
            if (spec == null || !(__instance is Player p) || p != Player.m_localPlayer)
                return;
            HoldPower.Active = null;
            if (Time.time - HoldPower.ActiveSince < 4f && HoldPower.IsSecondary(p) && HoldPower.For(p) == spec)
                HoldPower.Fire(spec, p, p.GetCurrentWeapon());
        }
    }
}
