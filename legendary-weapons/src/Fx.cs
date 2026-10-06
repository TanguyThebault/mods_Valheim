using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// Shared particle looks. Sprites/Default without a texture draws particles as hard white squares: billboard
    /// particles use a soft round dot instead, leaves a small leaf shape. Line renderers keep the plain material.
    /// </summary>
    internal static class Fx
    {
        private static Material s_soft, s_leaf, s_plain;

        internal static Material Plain => s_plain ?? (s_plain = new Material(Shader.Find("Sprites/Default")) { name = "lw_plain" });

        internal static Material Soft
        {
            get
            {
                if (s_soft != null)
                    return s_soft;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "lw_softdot", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float r = new Vector2(x - n / 2f + 0.5f, y - n / 2f + 0.5f).magnitude / (n / 2f);
                    float a = Mathf.Clamp01(1f - r);
                    px[y * n + x] = new Color(1f, 1f, 1f, a * a * (3f - 2f * a));       // smooth falloff, bright core
                }
                tex.SetPixels(px);
                tex.Apply(true);
                s_soft = new Material(Plain) { name = "lw_soft", mainTexture = tex };
                return s_soft;
            }
        }

        internal static Material Leaf
        {
            get
            {
                if (s_leaf != null)
                    return s_leaf;
                const int n = 64;
                var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "lw_leaf", wrapMode = TextureWrapMode.Clamp };
                var px = new Color[n * n];
                for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    // a pointed oval along the diagonal, a darker midrib, soft edge
                    float u = (x + y) / (2f * n) * 2f - 1f, v = (x - y) / (float)n * 1.6f;
                    float half = 0.42f * (1f - u * u);
                    float edge = half - Mathf.Abs(v);
                    float a = Mathf.Clamp01(edge * 18f) * (Mathf.Abs(u) < 0.98f ? 1f : 0f);
                    float rib = Mathf.Abs(v) < 0.03f ? 0.65f : 1f;
                    px[y * n + x] = new Color(rib, rib, rib, a);
                }
                tex.SetPixels(px);
                tex.Apply(true);
                s_leaf = new Material(Plain) { name = "lw_leafmat", mainTexture = tex };
                return s_leaf;
            }
        }
    }

    /// <summary>
    /// The charge of a held power, on the weapon hand, kept discreet: a thin stream of motes spiralling in from all around and drawing
    /// in faster as the charge grows (with light trails), a glowing core swelling at the hand, a ring at the feet
    /// closing in to show the progress, and a flash when the power is ready. Themed per weapon.
    /// </summary>
    internal class ChargeFx : MonoBehaviour
    {
        internal enum Theme { Wind, Earth, Leaves, Rune, Ember, Frost }

        private Theme _theme;
        private float _hold, _age;
        private Transform _hand, _feet;
        private ParticleSystem _stream, _core, _ground;
        private LineRenderer _ring;
        private Light _light;
        private Color _a, _b;
        private bool _flashed;

        public static ChargeFx Begin(Player p, Theme theme, float holdTime)
        {
            var vis = p.GetComponent<VisEquipment>();
            var hand = vis != null && vis.m_rightHand != null ? vis.m_rightHand : p.transform;
            var go = new GameObject("legendary_charge");
            var fx = go.AddComponent<ChargeFx>();
            fx.Build(p.transform, hand, theme, holdTime);
            return fx;
        }

        public void Stop()
        {
            foreach (var ps in new[] { _stream, _core, _ground })
                if (ps != null) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (_ring != null) _ring.enabled = false;
            if (_light != null) _light.enabled = false;
            enabled = false;
            Destroy(gameObject, 1.2f);
        }

        private void Build(Transform feet, Transform hand, Theme theme, float hold)
        {
            _theme = theme;
            _hold = Mathf.Max(0.1f, hold);
            _hand = hand;
            _feet = feet;
            switch (theme)
            {
                case Theme.Wind: _a = new Color(0.8f, 0.92f, 1f, 0.6f); _b = new Color(1f, 1f, 1f, 0.95f); break;
                case Theme.Earth: _a = new Color(0.45f, 0.38f, 0.28f, 0.7f); _b = new Color(0.75f, 0.68f, 0.5f, 0.9f); break;
                case Theme.Leaves: _a = new Color(0.5f, 0.32f, 0.12f, 0.95f); _b = new Color(0.4f, 0.5f, 0.18f, 0.95f); break;
                case Theme.Ember: _a = new Color(1f, 0.35f, 0.08f, 0.8f); _b = new Color(1f, 0.7f, 0.3f, 0.95f); break;
                case Theme.Frost: _a = new Color(0.6f, 0.8f, 1f, 0.7f); _b = new Color(0.92f, 0.97f, 1f, 0.95f); break;
                default: _a = new Color(1f, 0.6f, 0.2f, 0.7f); _b = new Color(1f, 0.85f, 0.45f, 0.95f); break;
            }
            _stream = Stream();
            _core = Core();
            _ground = theme == Theme.Earth || theme == Theme.Leaves ? Ground() : null;
            _ring = Ring();
            if (theme != Theme.Leaves)
            {
                var lg = new GameObject("light");
                lg.transform.SetParent(transform, false);
                _light = lg.AddComponent<Light>();
                _light.type = LightType.Point;
                _light.color = theme == Theme.Earth ? new Color(1f, 0.8f, 0.55f) : _b;
                _light.range = 2f;
                _light.intensity = 0f;
            }
            LateUpdate();
        }

        private static Color Faint(Color c, float alpha) => new Color(c.r, c.g, c.b, c.a * alpha);

        private ParticleSystem NewSystem(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = mat;
            return ps;
        }

        /// <summary>Motes born on a sphere round the hand and pulled in on a spiral, leaving light trails.</summary>
        private ParticleSystem Stream()
        {
            bool leaves = _theme == Theme.Leaves;
            var ps = NewSystem("stream", leaves ? Fx.Leaf : Fx.Soft);
            ps.transform.localPosition = Vector3.zero;
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.55f);
            main.startSpeed = 0f;
            main.startSize = leaves ? new ParticleSystem.MinMaxCurve(0.06f, 0.11f) : new ParticleSystem.MinMaxCurve(0.025f, 0.06f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(Faint(_a, 0.6f), Faint(_b, 0.6f));
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = leaves ? 0.9f : 1.0f;
            shape.radiusThickness = 0.1f;
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.radial = new ParticleSystem.MinMaxCurve(-3.2f);
            vel.orbitalY = new ParticleSystem.MinMaxCurve(5f);
            vel.orbitalX = new ParticleSystem.MinMaxCurve(1.5f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.3f), new GradientAlphaKey(0.9f, 0.85f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            if (!leaves)
            {
                var trails = ps.trails;
                trails.enabled = true;
                trails.ratio = 0.6f;
                trails.lifetime = new ParticleSystem.MinMaxCurve(0.15f);
                trails.widthOverTrail = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0f));
                trails.dieWithParticles = true;
                var r = ps.GetComponent<ParticleSystemRenderer>();
                r.trailMaterial = Fx.Plain;
            }
            else
            {
                var rot = ps.rotationOverLifetime;
                rot.enabled = true;
                rot.z = new ParticleSystem.MinMaxCurve(-6f, 6f);
            }
            var em = ps.emission;
            em.rateOverTime = 20f;
            ps.Play();
            return ps;
        }

        /// <summary>A soft glow at the hand, swelling with the charge.</summary>
        private ParticleSystem Core()
        {
            var ps = NewSystem("core", Fx.Soft);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.startLifetime = 0.12f;
            main.startSpeed = 0f;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(_b.r, _b.g, _b.b, 0.2f));
            var em = ps.emission;
            em.rateOverTime = 40f;
            var shape = ps.shape;
            shape.enabled = false;
            ps.Play();
            return ps;
        }

        /// <summary>Earth: grit and dust lifting off the ground round the feet; leaves: dead leaves swirling up the body.</summary>
        private ParticleSystem Ground()
        {
            bool leaves = _theme == Theme.Leaves;
            var ps = NewSystem("ground", leaves ? Fx.Leaf : Fx.Soft);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.3f, 1.2f);
            main.startSize = leaves ? new ParticleSystem.MinMaxCurve(0.06f, 0.11f) : new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = leaves ? new ParticleSystem.MinMaxGradient(_a, _b)
                : new ParticleSystem.MinMaxGradient(new Color(0.55f, 0.48f, 0.38f, 0.25f), new Color(0.7f, 0.62f, 0.5f, 0.4f));
            main.gravityModifier = leaves ? -0.1f : -0.05f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle;
            shape.radius = leaves ? 0.9f : 1.4f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.orbitalY = new ParticleSystem.MinMaxCurve(leaves ? 4f : 1f);
            vel.radial = new ParticleSystem.MinMaxCurve(leaves ? -0.3f : -0.6f);
            vel.y = new ParticleSystem.MinMaxCurve(leaves ? 1.2f : 0.4f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0f, 1f) });
            col.color = g;
            var em = ps.emission;
            em.rateOverTime = 10f;
            ps.Play();
            return ps;
        }

        private LineRenderer Ring()
        {
            var go = new GameObject("ring");
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = Fx.Plain;
            lr.useWorldSpace = true;
            lr.loop = true;
            lr.positionCount = 48;
            lr.widthMultiplier = 0.025f;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private void LateUpdate()
        {
            if (_hand == null || _feet == null)
            {
                Stop();
                return;
            }
            _age += Time.deltaTime;
            float k = Mathf.Clamp01(_age / _hold);
            transform.position = _hand.position;
            if (_stream != null)
            {
                _stream.transform.position = _hand.position;
                var em = _stream.emission;
                em.rateOverTime = Mathf.Lerp(8f, 55f, k * k);
                var vel = _stream.velocityOverLifetime;
                vel.radial = new ParticleSystem.MinMaxCurve(Mathf.Lerp(-2.5f, -5f, k));
            }
            if (_core != null)
            {
                _core.transform.position = _hand.position;
                var main = _core.main;
                main.startSize = 0.06f + 0.2f * k * (1f + 0.1f * Mathf.Sin(_age * 40f));
            }
            if (_ground != null)
            {
                _ground.transform.position = _feet.position + Vector3.up * 0.05f;
                var em = _ground.emission;
                em.rateOverTime = Mathf.Lerp(3f, 22f, k);
            }
            if (_light != null)
            {
                _light.transform.position = _hand.position;
                _light.intensity = 0.5f * k * (0.85f + 0.15f * Mathf.Sin(_age * 30f));
            }
            // the ring at the feet closes in as the charge fills, and brightens
            float radius = Mathf.Lerp(1.2f, 0.4f, k);
            var c = new Color(_b.r, _b.g, _b.b, 0.05f + 0.3f * k);
            _ring.startColor = _ring.endColor = c;
            for (int i = 0; i < _ring.positionCount; i++)
            {
                float a = i * Mathf.PI * 2f / _ring.positionCount + _age * 1.5f;
                _ring.SetPosition(i, _feet.position + new Vector3(Mathf.Cos(a) * radius, 0.06f, Mathf.Sin(a) * radius));
            }
            if (k >= 1f && !_flashed)
            {
                _flashed = true;
                Flash();
            }
        }

        /// <summary>Ready: a short burst outward from the hand.</summary>
        private void Flash()
        {
            var ps = NewSystem("flash", _theme == Theme.Leaves ? Fx.Leaf : Fx.Soft);
            ps.transform.position = _hand.position;
            var main = ps.main;
            main.loop = false;
            main.duration = 0.1f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.08f);
            main.startColor = new ParticleSystem.MinMaxGradient(Faint(_a, 0.7f), Faint(_b, 0.7f));
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, 14) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.1f;
            ps.Play();
        }
    }
}
