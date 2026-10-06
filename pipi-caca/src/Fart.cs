using System.Globalization;
using UnityEngine;

namespace Caca
{
    /// <summary>
    /// The fart gauge: hidden, no status effect, no animation. It fills on its own and a little more with each meal;
    /// when full, the character lets one go where they stand (sound for everyone nearby, a faint drifting gas cloud),
    /// and it starts over. Pooping empties it too. Saved in the character's custom data.
    /// </summary>
    internal static class Fart
    {
        private const string Key = "caca_fart";
        public static float Value;
        private static Player s_for;
        private static float s_saveAt;

        private static void Load(Player p)
        {
            s_for = p;
            Value = p.m_customData.TryGetValue(Key, out var s)
                    && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        public static void Save()
        {
            if (s_for != null) s_for.m_customData[Key] = Value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        public static void AddMeal()
        {
            Value = Mathf.Min(100f, Value + Plugin.FartPerMeal.Value);
        }

        public static void Tick(Player p)
        {
            if (p != s_for) Load(p);
            if (!p.IsDead() && !p.InBed())
                Value = Mathf.Min(100f, Value + Plugin.FartFillPerMinute.Value / 60f * Time.deltaTime);
            if (Value >= 100f && !p.IsDead() && !p.InBed() && !p.IsTeleporting() && !Need.Busy)
                Let(p);
            if (Time.time > s_saveAt)
            {
                s_saveAt = Time.time + 2f;
                Save();
            }
        }

        public static void Let(Player p)
        {
            Value = 0f;
            Save();
            var pos = p.transform.position + p.transform.up * 0.9f - p.transform.forward * 0.22f;
            Plugin.GasFart(pos, -p.transform.forward);
            Plugin.Log.LogInfo("Fart");
        }
    }

    /// <summary>A faint, slowly rising and spreading cloud of yellow-green gas behind the player.</summary>
    internal static class GasCloud
    {
        private static Material s_mat;

        public static void Spawn(Vector3 pos, Vector3 back)
        {
            if (!Plugin.FartCloud.Value) return;
            if (s_mat == null)
                s_mat = new Material(Shader.Find("Sprites/Default")) { name = "caca_gas", mainTexture = PuffTexture() };
            var go = new GameObject("caca_gas");
            go.transform.position = pos;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.3f;
            main.loop = false;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.2f, 3.2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.25f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            float a = Plugin.FartCloudOpacity.Value;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.72f, 0.74f, 0.42f, a), new Color(0.62f, 0.6f, 0.36f, a));
            main.gravityModifier = -0.012f;                       // warm gas: it rises, slowly
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 7), new ParticleSystem.Burst(0.12f, 5) });
            var sh = ps.shape;
            sh.shapeType = ParticleSystemShapeType.Sphere;
            sh.radius = 0.08f;
            var vel = ps.velocityOverLifetime;                    // pushed out behind, then drifting
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(back.x * 0.35f);
            vel.y = new ParticleSystem.MinMaxCurve(0.02f);
            vel.z = new ParticleSystem.MinMaxCurve(back.z * 0.35f);
            var size = ps.sizeOverLifetime;                       // and spreading
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2.4f)));
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                      new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.6f, 0.6f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var rot = ps.rotationOverLifetime;
            rot.enabled = true;
            rot.z = new ParticleSystem.MinMaxCurve(-0.4f, 0.4f);
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = s_mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            ps.Play();
            Object.Destroy(go, 4f);
        }

        /// <summary>A soft, slightly lumpy puff.</summary>
        private static Texture2D PuffTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "caca_puff", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float ang = Mathf.Atan2(dy, dx);
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / (0.85f + 0.1f * Mathf.Sin(ang * 5f + 1f) + 0.05f * Mathf.Sin(ang * 9f));
                    float a = Mathf.Exp(-r * r * 3.2f) * Mathf.Clamp01((1f - r) * 2.5f);
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }
    }
}
