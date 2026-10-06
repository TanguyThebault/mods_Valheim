using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace Caca
{
    /// <summary>How the character pees: Auto picks from the body model (male: standing, female: squatting).</summary>
    public enum PeePose { Auto, Standing, Squatting }

    /// <summary>
    /// The pee urge: it fills on its own, slowly. From PeeMinNeed the player can go on command (PeeKey, or `pipi`);
    /// at 100 % it starts on its own. Peeing does not stop the player: they walk (no running, jumping, dodging or
    /// attacking), the body turns with the camera, and the stream follows the mouse. The urge drains at a fixed
    /// rate (100 % in PeeDrainSeconds); the stream's strength follows what is left, so it starts strong and ends in
    /// dribbles. The state goes into the player's ZDO so everyone sees the stream and the hand (PeeStream, Body).
    /// </summary>
    internal static class Pee
    {
        public const string ZdoPower = "caca_pee", ZdoHold = "caca_pee_hold", ZdoAim = "caca_pee_aim", ZdoSquat = "caca_pee_squat";
        private static readonly AccessTools.FieldRef<Player, bool> s_crouch = AccessTools.FieldRefAccess<Player, bool>("m_crouchToggled");
        private const string KeyNeed = "caca_pee_need";
        private const float Prepare = 0.45f;          // hand to the tip before the stream starts

        public static float Value;
        public static bool Active, Squat;
        private static bool s_crouchBefore;
        public static float Power;                    // 0..1, what the stream shows right now
        public static Vector3 AimLocal = Vector3.forward;
        private static float s_since;
        private static bool s_walkBefore;
        private static Player s_for;
        private static float s_saveAt;

        public static void Load(Player p)
        {
            s_for = p;
            Value = p.m_customData.TryGetValue(KeyNeed, out var s)
                    && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
            Active = false;
            Power = 0f;
        }

        public static void Save()
        {
            if (s_for != null)
                s_for.m_customData[KeyNeed] = Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static void Tick(Player p)
        {
            if (p != s_for) Load(p);
            float dt = Time.deltaTime;
            if (!Active)
            {
                if (!p.InBed() && !p.IsDead())
                    Value = Mathf.Min(100f, Value + Plugin.PeeFillPerMinute.Value / 60f * dt);
                if (Value >= 100f && !Need.Busy && CanGo(p))
                    Start(p, true);
            }
            else
            {
                s_since += dt;
                if (!CanKeepGoing(p))
                {
                    Stop(p, false);
                }
                else
                {
                    p.SetWalk(true);
                    if (Squat) s_crouch(p) = true;          // squatting: she can still shuffle along, slowly
                    AimLocal = Squat ? SquatAim(p) : Aim(p);
                    if (s_since >= Prepare)
                    {
                        Value = Mathf.Max(0f, Value - 100f / Mathf.Max(0.5f, Plugin.PeeDrainSeconds.Value) * dt);
                        float ramp = Mathf.Clamp01((s_since - Prepare) / 0.25f);
                        Power = Strength(Value) * ramp;
                        if (Value <= 0f) Stop(p, true);
                    }
                }
            }
            if (Time.time > s_saveAt)
            {
                s_saveAt = Time.time + 2f;
                Save();
            }
            Publish(p);
        }

        /// <summary>What's left drives the strength: full urge, a strong arc; the last percents, dribbles.</summary>
        public static float Strength(float value) => Mathf.Pow(Mathf.Clamp01(value / 100f), 0.55f);

        public static bool CanGo(Player p) =>
            p != null && !p.IsDead() && !p.IsSwimming() && !p.IsAttached() && !p.InBed() && !p.IsTeleporting()
            && !p.InCutscene() && !p.InEmote();

        private static bool CanKeepGoing(Player p) =>
            p != null && !p.IsDead() && !p.IsSwimming() && !p.IsAttached() && !p.InBed() && !p.IsTeleporting()
            && !p.InCutscene();

        public static void TryOnCommand(Player p, bool force = false)
        {
            if (Active)
            {
                Stop(p, false);                      // the same key stops early: what's left stays
                return;
            }
            if (Need.Busy) return;
            if (!force && Value < Plugin.PeeMinNeed.Value)
            {
                p.Message(MessageHud.MessageType.Center, "$msg_pipi_notyet");
                return;
            }
            if (!CanGo(p))
            {
                p.Message(MessageHud.MessageType.Center, "$msg_caca_nothere");
                return;
            }
            if (force && Value < 30f) Value = 30f;
            Start(p, false);
        }

        private static void Start(Player p, bool urgent)
        {
            Active = true;
            s_since = 0f;
            Power = 0f;
            s_walkBefore = p.GetWalk();
            Squat = IsSquatter(p);
            s_crouchBefore = s_crouch(p);
            AimLocal = Squat ? SquatAim(p) : Aim(p);
            if (urgent) p.Message(MessageHud.MessageType.Center, "$msg_pipi_urgent");
            Plugin.Log.LogInfo("Pee start at " + Value.ToString("0") + " % (" + (urgent ? "forced" : "on command") + (Squat ? ", squatting" : "") + ")");
        }

        public static void Stop(Player p, bool emptied)
        {
            if (!Active) return;
            Active = false;
            Power = 0f;
            s_aimPublished = Vector3.down;
            if (p != null)
            {
                p.SetWalk(s_walkBefore);
                if (Squat) s_crouch(p) = s_crouchBefore;
                if (emptied) p.Message(MessageHud.MessageType.TopLeft, "$msg_pipi_relief");
            }
            Save();
            Plugin.Log.LogInfo("Pee stop at " + Value.ToString("0") + " %");
        }

        /// <summary>Squatting or standing, from [Pee] Pose (Auto: the female body squats).</summary>
        public static bool IsSquatter(Player p)
        {
            switch (Plugin.Pose.Value)
            {
                case PeePose.Standing: return false;
                case PeePose.Squatting: return true;
                default:
                    var ve = p.GetComponent<VisEquipment>();
                    return ve != null && ve.GetModelIndex() == 1;
            }
        }

        /// <summary>Squatting: down toward the ground, a little forward; the mouse only nudges it sideways.</summary>
        private static Vector3 SquatAim(Player p)
        {
            var local = Quaternion.Inverse(p.transform.rotation) * p.GetLookDir();
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -15f, 15f);
            return Quaternion.Euler(-Plugin.SquatPitch.Value, yaw, 0f) * Vector3.forward;
        }

        /// <summary>The camera's look, in the body's frame, kept in a cone in front (the body turns with the camera).</summary>
        private static Vector3 Aim(Player p)
        {
            if (Lab.AimOn) return Lab.Aim;
            var look = p.GetLookDir();
            var local = Quaternion.Inverse(p.transform.rotation) * look;
            float yaw = Mathf.Clamp(Mathf.Atan2(local.x, local.z) * Mathf.Rad2Deg, -35f, 35f);
            float pitch = Mathf.Asin(Mathf.Clamp(local.y, -1f, 1f)) * Mathf.Rad2Deg + Plugin.PeeAimLift.Value;
            pitch = Mathf.Clamp(pitch, -70f, 45f);
            return Quaternion.Euler(-pitch, yaw, 0f) * Vector3.forward;
        }

        private static float s_published = -1f;
        private static bool s_holdPublished;
        private static Vector3 s_aimPublished = Vector3.down;     // never a real aim (pitch is clamped at -70)

        private static void Publish(Player p)
        {
            var nview = p.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            var zdo = nview.GetZDO();
            bool hold = Active;
            if (Mathf.Abs(Power - s_published) > 0.01f || hold != s_holdPublished)
            {
                zdo.Set(ZdoPower, Power);
                zdo.Set(ZdoHold, hold ? 1f : 0f);
                zdo.Set(ZdoSquat, Squat ? 1f : 0f);
                s_published = Power;
                s_holdPublished = hold;
            }
            if (hold && Vector3.Angle(AimLocal, s_aimPublished) > 1f)   // only when it moves: no ZDO churn
            {
                zdo.Set(ZdoAim, AimLocal);
                s_aimPublished = AimLocal;
            }
        }
    }

    /// <summary>While peeing: walk only, no jump / dodge / crouch / attacks / block / autorun.</summary>
    [HarmonyPatch(typeof(Player), nameof(Player.SetControls))]
    internal static class PeeControlsPatch
    {
        private static void Prefix(Player __instance, ref bool attack, ref bool attackHold, ref bool secondaryAttack,
            ref bool secondaryAttackHold, ref bool block, ref bool blockHold, ref bool jump, ref bool crouch, ref bool run,
            ref bool autoRun, ref bool dodge)
        {
            if (!Pee.Active || __instance != Player.m_localPlayer) return;
            attack = attackHold = secondaryAttack = secondaryAttackHold = false;
            block = blockHold = jump = crouch = run = autoRun = dodge = false;
        }
    }

    /// <summary>While peeing the body faces where the camera looks (so the stream can be aimed with the mouse).</summary>
    [HarmonyPatch(typeof(Player), "AlwaysRotateCamera")]
    internal static class PeeRotatePatch
    {
        private static void Postfix(Player __instance, ref bool __result)
        {
            if (Pee.Active && __instance == Player.m_localPlayer) __result = true;
        }
    }

    /// <summary>Every player gets a PeeStream (it stays idle until that player pees).</summary>
    [HarmonyPatch(typeof(Player), "Awake")]
    internal static class PlayerAwakePatch
    {
        private static void Postfix(Player __instance)
        {
            if (__instance.GetComponent<PeeStream>() == null) __instance.gameObject.AddComponent<PeeStream>();
        }
    }
}
