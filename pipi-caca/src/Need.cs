using System.Collections;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace Caca
{
    /// <summary>
    /// The urge. Each meal adds a "pending" amount (bigger foods, more) that digests into the urge a little at a time.
    /// It has no bar: from MinNeed a status effect shows (Urges) and the player can go on command (key or `caca`);
    /// at 100 it happens on its own, as soon as the player stands on the ground. Both values live in the
    /// character's custom data, so they survive a restart.
    /// </summary>
    internal static class Need
    {
        private const string KeyNeed = "caca_need", KeyPending = "caca_pending";
        public static float Value, Pending;
        private static Player s_for;
        private static float s_saveAt;
        public static bool Busy;

        public static void Load(Player p)
        {
            s_for = p;
            Value = Read(p, KeyNeed);
            Pending = Read(p, KeyPending);
        }

        private static float Read(Player p, string key) =>
            p.m_customData.TryGetValue(key, out var s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;

        public static void Save()
        {
            if (s_for == null) return;
            s_for.m_customData[KeyNeed] = Value.ToString("0.###", CultureInfo.InvariantCulture);
            s_for.m_customData[KeyPending] = Pending.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static void AddMeal(ItemDrop.ItemData food)
        {
            float add = Mathf.Clamp(food.m_shared.m_food * Plugin.NeedPerFoodHealth.Value, 4f, 35f);
            Pending += add;
            Save();
            Fart.AddMeal();
        }

        public static void Tick(Player p)
        {
            if (p != s_for) Load(p);
            float dt = Time.deltaTime;
            if (Pending > 0f)
            {
                float d = Mathf.Min(Pending, Plugin.DigestPerMinute.Value / 60f * dt);
                Pending -= d;
                Value = Mathf.Min(100f, Value + d);
            }
            if (Time.time > s_saveAt)
            {
                s_saveAt = Time.time + 2f;
                Save();
            }
            if (Value >= 100f && !Busy && !Pee.Active && CanGo(p))
                Plugin.Instance.StartCoroutine(Go(p, true));
        }

        public static bool CanGo(Player p) =>
            p != null && !p.IsDead() && p.IsOnGround() && !p.IsSwimming() && !p.InWater() && !p.IsAttached()
            && !p.IsTeleporting() && !p.InPlaceMode();

        public static void TryOnCommand(Player p, bool force = false)
        {
            if (Busy || Pee.Active) return;
            if (!force && Value < Plugin.MinNeed.Value)
            {
                p.Message(MessageHud.MessageType.Center, "$msg_caca_notyet");
                return;
            }
            if (!CanGo(p))
            {
                p.Message(MessageHud.MessageType.Center, "$msg_caca_nothere");
                return;
            }
            Plugin.Instance.StartCoroutine(Go(p, false));
        }

        /// <summary>Squat (kneel emote), a fart, the poop lands behind, relief.</summary>
        private static IEnumerator Go(Player p, bool urgent)
        {
            Busy = true;
            if (urgent) p.Message(MessageHud.MessageType.Center, "$msg_caca_urgent");
            p.StartEmote("kneel", false);
            yield return new WaitForSeconds(0.8f);
            if (p == null || p.IsDead()) { Busy = false; yield break; }
            var back = -p.transform.forward;
            var pos = p.transform.position + back * 0.12f + Vector3.up * 0.25f;   // right under the seat
            Plugin.FartSound(pos);
            yield return new WaitForSeconds(0.45f);
            if (Plugin.PoopPrefab != null)
            {
                var go = Object.Instantiate(Plugin.PoopPrefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
                Plugin.Plop(pos);
                var rb = go.GetComponent<Rigidbody>();
                if (rb != null) rb.velocity = back * 0.15f + Vector3.down * 0.5f;
            }
            Value = Mathf.Max(0f, Value - 100f);
            Save();
            Fart.Value = 0f;                                  // that one counted
            yield return new WaitForSeconds(1.4f);
            if (p != null)
            {
                Traverse.Create(p).Method("StopEmote").GetValue();
                p.Message(MessageHud.MessageType.TopLeft, "$msg_caca_relief");
            }
            Busy = false;
        }

        // ---------------------------------------------------------------- debug overlay (off by default)
        private static Texture2D s_white;

        /// <summary>Both urges as thin bars, for tests only ([HUD] ShowBars): in play the status effects tell.</summary>
        public static void DrawDebug()
        {
            var p = Player.m_localPlayer;
            if (p == null || Hud.instance == null || Hud.IsUserHidden() || !Plugin.ShowBars.Value)
                return;
            if (s_white == null)
            {
                s_white = new Texture2D(1, 1);
                s_white.SetPixel(0, 0, Color.white);
                s_white.Apply();
            }
            float scale = Screen.height / 1080f;
            float x = 0.012f * Screen.width, y = 0.70f * Screen.height;
            Bar(x, y, scale, Value, Pending, new Color(0.55f, 0.34f, 0.12f), "caca");
            Bar(x, y + 22f * scale, scale, Pee.Value, 0f, new Color(0.9f, 0.8f, 0.2f), "pipi" + (Pee.Active ? " " + Pee.Power.ToString("0.00") : ""));
            Bar(x, y + 44f * scale, scale, Fart.Value, 0f, new Color(0.6f, 0.65f, 0.35f), "pet");
        }

        private static void Bar(float x, float y, float scale, float value, float pending, Color fill, string label)
        {
            float w = 170f * scale, h = 12f * scale;
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.55f);
            GUI.DrawTexture(new Rect(x - 2, y - 2, w + 4, h + 4), s_white);
            float f = Mathf.Clamp01(value / 100f);
            GUI.color = fill;
            GUI.DrawTexture(new Rect(x, y, w * f, h), s_white);
            float pf = Mathf.Clamp01((value + pending) / 100f) - f;
            if (pf > 0f)
            {
                GUI.color = new Color(fill.r, fill.g, fill.b, 0.35f);
                GUI.DrawTexture(new Rect(x + w * f, y, w * pf, h), s_white);
            }
            GUI.color = Color.white;
            var style = new GUIStyle(GUI.skin.label) { fontSize = Mathf.RoundToInt(12 * scale), fontStyle = FontStyle.Bold };
            GUI.Label(new Rect(x + w + 6 * scale, y - 4 * scale, 200 * scale, 22 * scale), label + " " + Mathf.RoundToInt(value) + "%", style);
            GUI.color = old;
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.EatFood))]
    internal static class EatPatch
    {
        private static void Postfix(Player __instance, ItemDrop.ItemData item, bool __result)
        {
            if (__result && __instance == Player.m_localPlayer && item != null)
                Need.AddMeal(item);
        }
    }
}
