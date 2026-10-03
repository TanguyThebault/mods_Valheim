using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ThrowingAxe
{
    /// <summary>
    /// Lightweight self-profiler: every 10 s logs the frame rate, the worst frame, and the time spent in each
    /// of this mod's hooks. Turn off with [Debug] PerfLog = false.
    /// </summary>
    internal static class Perf
    {
        private static readonly Dictionary<string, double> s_ms = new Dictionary<string, double>();
        private static readonly Dictionary<string, int> s_calls = new Dictionary<string, int>();
        private static readonly Stopwatch s_clock = Stopwatch.StartNew();
        private static float s_windowStart;
        private static int s_frames;
        private static float s_worst;

        public static long Begin()
        {
            return s_clock.ElapsedTicks;
        }

        public static void End(string key, long start)
        {
            double ms = (s_clock.ElapsedTicks - start) * 1000.0 / Stopwatch.Frequency;
            s_ms.TryGetValue(key, out var t);
            s_ms[key] = t + ms;
            s_calls.TryGetValue(key, out var n);
            s_calls[key] = n + 1;
        }

        /// <summary>Called once per frame from the plugin.</summary>
        public static void Frame()
        {
            if (!Plugin.PerfLog.Value)
                return;
            s_frames++;
            s_worst = Mathf.Max(s_worst, Time.unscaledDeltaTime);
            float elapsed = Time.unscaledTime - s_windowStart;
            if (elapsed < 10f)
                return;
            var sb = new StringBuilder();
            sb.Append("Perf: " + (s_frames / elapsed).ToString("F0") + " fps, worst frame " + (s_worst * 1000f).ToString("F0") +
                      " ms, scene " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name +
                      ", objects " + Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None).Length);
            double total = s_ms.Values.Sum();
            sb.Append(", mod hooks " + (total / s_frames).ToString("F3") + " ms/frame");
            foreach (var kv in s_ms.OrderByDescending(kv => kv.Value))
                sb.Append("\n  " + kv.Key + ": " + (kv.Value / s_frames).ToString("F3") + " ms/frame, " + (s_calls[kv.Key] / elapsed).ToString("F0") + " calls/s");
            Plugin.Log.LogInfo(sb.ToString());
            s_ms.Clear();
            s_calls.Clear();
            s_frames = 0;
            s_worst = 0f;
            s_windowStart = Time.unscaledTime;
        }
    }
}
