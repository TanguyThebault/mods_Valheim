using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace Caca
{
    /// <summary>
    /// The body while peeing: where the crotch is (from the humanoid hips bone, in the body's frame), and the right
    /// arm brought there by a two-bone IK applied after the animator, so the hand holds the tip whatever the walk
    /// animation does. The weapon in the right hand is hidden meanwhile. Works for every player (remote ones read
    /// their state from the ZDO through their PeeStream).
    /// </summary>
    internal static class Body
    {
        private static readonly AccessTools.FieldRef<Character, Animator> s_animator =
            AccessTools.FieldRefAccess<Character, Animator>("m_animator");
        private static readonly AccessTools.FieldRef<VisEquipment, GameObject> s_rightItem =
            AccessTools.FieldRefAccess<VisEquipment, GameObject>("m_rightItemInstance");

        public static Animator AnimatorOf(Character c) => c != null ? s_animator(c) : null;

        public static GameObject RightItem(VisEquipment ve) => ve != null ? s_rightItem(ve) : null;

        /// <summary>"x y z" -> Vector3 (invariant culture), or the fallback.</summary>
        public static Vector3 Vec(string s, Vector3 fallback)
        {
            if (string.IsNullOrEmpty(s)) return fallback;
            var parts = s.Split(new[] { ' ', ',', ';' }, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) return fallback;
            var v = new float[3];
            for (int i = 0; i < 3; i++)
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i])) return fallback;
            return new Vector3(v[0], v[1], v[2]);
        }

        /// <summary>Point given as (right, up, forward) offsets from the hips, in the body's frame.</summary>
        private static Vector3 FromHips(Character c, Transform hips, Vector3 offset)
        {
            var t = c.transform;
            return hips.position + t.right * offset.x + t.up * offset.y + t.forward * offset.z;
        }

        /// <summary>The tip the stream leaves from. False when the body has no humanoid hips.</summary>
        public static bool Tip(Character c, out Vector3 tip)
        {
            var anim = AnimatorOf(c);
            var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips == null)
            {
                tip = c.transform.position + c.transform.up * 0.95f + c.transform.forward * 0.2f;
                return false;
            }
            tip = FromHips(c, hips, Vec(Plugin.TipOffset.Value, new Vector3(0f, -0.13f, 0.2f)));
            return true;
        }

        /// <summary>Squatting: low, under the hips, a little forward (in the body's frame).</summary>
        public static bool SquatTip(Character c, out Vector3 tip)
        {
            var anim = AnimatorOf(c);
            var hips = anim != null && anim.isHuman ? anim.GetBoneTransform(HumanBodyBones.Hips) : null;
            if (hips == null)
            {
                tip = c.transform.position + c.transform.up * 0.45f + c.transform.forward * 0.08f;
                return false;
            }
            tip = FromHips(c, hips, Vec(Plugin.SquatTipOffset.Value, new Vector3(0f, -0.1f, 0.05f)));
            return true;
        }

        /// <summary>Right hand on the tip: wrist placed by IK, fingers wrapped around, thumb along the stream.</summary>
        public static void HoldTip(Character c, float weight, Vector3 aimDir)
        {
            if (weight <= 0.001f) return;
            var anim = AnimatorOf(c);
            if (anim == null || !anim.isHuman) return;
            var hips = anim.GetBoneTransform(HumanBodyBones.Hips);
            var upper = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
            var lower = anim.GetBoneTransform(HumanBodyBones.RightLowerArm);
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            if (hips == null || upper == null || lower == null || hand == null) return;
            var t = c.transform;

            var wrist = FromHips(c, hips, Vec(Plugin.WristOffset.Value, new Vector3(0.09f, -0.06f, 0.1f)));
            var pole = upper.position + t.right * 0.35f - t.forward * 0.25f - t.up * 0.3f;   // elbow out and back
            var upperRot = upper.rotation;
            var lowerRot = lower.rotation;
            var handRot = hand.rotation;
            SolveTwoBone(upper, lower, hand, wrist, pole);

            // the hand: fingers wrap toward the body's middle and down, thumb along the stream
            var fingers = FingerDir(anim, hand, lower);
            var wantFingers = t.TransformDirection(Vec(Plugin.FingerDir.Value, new Vector3(-0.7f, -0.55f, 0.35f)).normalized);
            hand.rotation = Quaternion.FromToRotation(fingers, wantFingers) * hand.rotation;
            var thumb = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            if (thumb != null)
            {
                var thumbDir = Vector3.ProjectOnPlane(thumb.position - hand.position, wantFingers);
                var wantThumb = Vector3.ProjectOnPlane(aimDir, wantFingers);
                if (thumbDir.sqrMagnitude > 1e-6f && wantThumb.sqrMagnitude > 1e-6f)
                    hand.rotation = Quaternion.FromToRotation(thumbDir, wantThumb) * hand.rotation;
            }
            CurlFingers(anim, weight);

            if (weight < 0.999f)
            {
                upper.rotation = Quaternion.Slerp(upperRot, upper.rotation, weight);
                lower.rotation = Quaternion.Slerp(lowerRot, lower.rotation, weight);
                hand.rotation = Quaternion.Slerp(handRot, hand.rotation, weight);
            }
        }

        /// <summary>The middle of the closed right fist (between the wrist and the knuckles, a bit into the palm).</summary>
        public static bool Fist(Character c, out Vector3 fist)
        {
            fist = Vector3.zero;
            var anim = AnimatorOf(c);
            if (anim == null || !anim.isHuman) return false;
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var mid = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            if (hand == null) return false;
            fist = mid != null ? Vector3.Lerp(hand.position, mid.position, 0.7f) : hand.position;
            return true;
        }

        private static Vector3 FingerDir(Animator anim, Transform hand, Transform lower)
        {
            var mid = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var d = mid != null ? mid.position - hand.position : hand.position - lower.position;
            return d.sqrMagnitude > 1e-8f ? d.normalized : hand.forward;
        }

        /// <summary>Closes the right fingers (a loose fist) by bending each phalanx about the hand's thumb axis.</summary>
        private static readonly HumanBodyBones[] s_fingers =
        {
            HumanBodyBones.RightIndexProximal, HumanBodyBones.RightIndexIntermediate, HumanBodyBones.RightIndexDistal,
            HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightMiddleIntermediate, HumanBodyBones.RightMiddleDistal,
            HumanBodyBones.RightRingProximal, HumanBodyBones.RightRingIntermediate, HumanBodyBones.RightRingDistal,
            HumanBodyBones.RightLittleProximal, HumanBodyBones.RightLittleIntermediate, HumanBodyBones.RightLittleDistal,
        };

        private static void CurlFingers(Animator anim, float weight)
        {
            var hand = anim.GetBoneTransform(HumanBodyBones.RightHand);
            var thumb = anim.GetBoneTransform(HumanBodyBones.RightThumbProximal);
            var mid = anim.GetBoneTransform(HumanBodyBones.RightMiddleProximal);
            var index = anim.GetBoneTransform(HumanBodyBones.RightIndexProximal);
            var little = anim.GetBoneTransform(HumanBodyBones.RightLittleProximal);
            if (hand == null || thumb == null || mid == null || index == null || little == null) return;
            var fingers = (mid.position - hand.position).normalized;
            var across = (index.position - little.position).normalized;       // knuckle line, toward the thumb side
            var palm = Vector3.Cross(fingers, across).normalized;              // one of the two palm normals
            if (Vector3.Dot(palm, thumb.position - hand.position) < 0f) palm = -palm;   // toward the palm (thumb side)
            var axis = Vector3.Cross(fingers, palm).normalized;
            float curl = Plugin.FingerCurl.Value * weight;
            foreach (var b in s_fingers)
            {
                var bone = anim.GetBoneTransform(b);
                if (bone != null) bone.rotation = Quaternion.AngleAxis(curl, axis) * bone.rotation;
            }
        }

        /// <summary>Classic analytic two-bone IK: elbow in the plane of the pole, then the forearm onto the target.</summary>
        private static void SolveTwoBone(Transform a, Transform b, Transform c, Vector3 target, Vector3 pole)
        {
            float lab = Vector3.Distance(a.position, b.position);
            float lbc = Vector3.Distance(b.position, c.position);
            var toT = target - a.position;
            float dist = Mathf.Clamp(toT.magnitude, Mathf.Abs(lab - lbc) + 1e-3f, lab + lbc - 1e-3f);
            var dir = toT.normalized;
            float x = (lab * lab - lbc * lbc + dist * dist) / (2f * dist);
            float h = Mathf.Sqrt(Mathf.Max(0f, lab * lab - x * x));
            var bend = Vector3.ProjectOnPlane(pole - a.position, dir);
            if (bend.sqrMagnitude < 1e-6f) bend = Vector3.ProjectOnPlane(b.position - a.position, dir);
            bend.Normalize();
            var elbow = a.position + dir * x + bend * h;
            a.rotation = Quaternion.FromToRotation(b.position - a.position, elbow - a.position) * a.rotation;
            var wristNow = c.position;
            b.rotation = Quaternion.FromToRotation(wristNow - b.position, a.position + dir * dist - b.position) * b.rotation;
        }
    }

    /// <summary>Lab showcase camera: after the game camera has placed itself.</summary>
    [HarmonyPatch(typeof(GameCamera), "LateUpdate")]
    internal static class LabCameraPatch
    {
        private static void Postfix(GameCamera __instance)
        {
            if (Lab.CamOn) Lab.PlaceCamera(__instance.transform);
        }
    }

    /// <summary>After the animator and Valheim's own late update (head look): bend the arm of peeing players.</summary>
    [HarmonyPatch(typeof(CharacterAnimEvent), nameof(CharacterAnimEvent.CustomLateUpdate))]
    internal static class ArmPatch
    {
        private static readonly AccessTools.FieldRef<CharacterAnimEvent, Character> s_character =
            AccessTools.FieldRefAccess<CharacterAnimEvent, Character>("m_character");

        private static void Postfix(CharacterAnimEvent __instance)
        {
            var c = s_character(__instance);
            if (!(c is Player)) return;
            var stream = PeeStream.Of(c);
            if (stream == null || stream.HandWeight <= 0.001f) return;
            Body.HoldTip(c, stream.HandWeight, stream.Dir);
            if (stream.HandWeight > 0.9f && Body.Fist(c, out var fist))
                stream.SetHandTip(fist + stream.Dir * Plugin.TipFromHand.Value);
        }
    }
}
