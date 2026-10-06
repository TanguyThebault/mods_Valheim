using System.Collections.Generic;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// The blast of a Thunder Spear bolt, a sphere around the impact showing the volume it hurts and how the damage
    /// falls off (with the 3D distance): a crackling shell of veined light that swells out to the strike radius and
    /// slowly turns; a white-hot core at the impact that fades outward; arcs shooting from the centre to the shell
    /// (white at the centre, thin and blue at the edge); jagged great circles crawling over the shell; and sparks
    /// thrown from the whole surface when it reaches full size. Local only: every client builds it from the bolt RPC.
    /// </summary>
    internal class StrikeArea : MonoBehaviour
    {
        private const float Lifetime = 1.1f;
        private const float WaveTime = 0.3f;      // seconds for the shell to reach the edge
        private const int Arcs = 10;
        private const int Rings = 3;
        private const int RingPoints = 48;
        private static Texture2D s_veins;

        private Vector3 _centre;
        private float _radius, _age, _nextReshape;
        private Material _mat, _shellMat, _coreMat;
        private Transform _shell, _core;
        private readonly List<LineRenderer> _arcs = new List<LineRenderer>();
        private readonly List<LineRenderer> _rings = new List<LineRenderer>();
        private readonly List<Quaternion> _ringTilt = new List<Quaternion>();
        private ParticleSystem _sparks;
        private bool _sparksFired;

        public static void Spawn(Vector3 point, float radius, Material mat)
        {
            var go = new GameObject("thunderspear_area");
            go.transform.position = point;
            var area = go.AddComponent<StrikeArea>();
            area.Build(point, radius, mat);
            Destroy(go, Lifetime + 0.8f);
        }

        private LineRenderer Line(string name, float width, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = _mat;
            lr.useWorldSpace = true;
            lr.loop = loop;
            lr.startWidth = lr.endWidth = width;
            lr.numCornerVertices = 1;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            return lr;
        }

        private void Build(Vector3 point, float radius, Material mat)
        {
            _centre = point;
            _radius = Mathf.Max(1f, radius);
            _mat = mat;
            _shell = Sphere("shell", Veins(), out _shellMat);
            _core = Sphere("core", null, out _coreMat);
            for (int i = 0; i < Arcs; i++)
            {
                var a = Line("arc", 0.1f, false);
                a.startWidth = 0.18f;
                a.endWidth = 0.03f;
                _arcs.Add(a);
            }
            for (int i = 0; i < Rings; i++)
            {
                _rings.Add(Line("ring", 0.07f, true));
                _ringTilt.Add(Random.rotationUniform);
            }
            _sparks = BuildSparks();
            Reshape(0.2f);
        }

        /// <summary>A texture of thin, branching light veins on a faint haze: the shell's electric skin.</summary>
        private static Texture2D Veins()
        {
            if (s_veins != null)
                return s_veins;
            const int w = 256, h = 128;
            s_veins = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "thunderspear_veins", wrapMode = TextureWrapMode.Repeat };
            var a = new float[w * h];
            var rng = new System.Random(5);
            for (int k = 0; k < 70; k++)
            {
                // random walks: crooked filaments that wrap around the sphere
                float x = rng.Next(w), y = rng.Next(h), ang = (float)(rng.NextDouble() * Mathf.PI * 2);
                int len = 30 + rng.Next(90);
                for (int s = 0; s < len; s++)
                {
                    ang += (float)(rng.NextDouble() - 0.5) * 1.2f;
                    x += Mathf.Cos(ang);
                    y += Mathf.Sin(ang) * 0.6f;
                    int xi = ((int)x % w + w) % w, yi = Mathf.Clamp((int)y, 0, h - 1);
                    a[yi * w + xi] = Mathf.Max(a[yi * w + xi], 1f - s / (float)len * 0.6f);
                }
            }
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++)
                px[i] = new Color(0.75f, 0.87f, 1f, Mathf.Clamp01(0.12f + a[i] * 0.88f));
            s_veins.SetPixels(px);
            s_veins.Apply(true);
            return s_veins;
        }

        private Transform Sphere(string name, Texture2D tex, out Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Destroy(go.GetComponent<Collider>());
            go.name = name;
            go.transform.SetParent(transform, false);
            go.transform.position = _centre;
            go.transform.rotation = Random.rotationUniform;
            go.transform.localScale = Vector3.zero;
            var r = go.GetComponent<MeshRenderer>();
            mat = new Material(_mat);
            if (tex != null) mat.mainTexture = tex;
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return go.transform;
        }

        /// <summary>Sparks thrown out from the whole shell when it reaches full size.</summary>
        private ParticleSystem BuildSparks()
        {
            var go = new GameObject("sparks");
            go.transform.SetParent(transform, false);
            go.transform.position = _centre;
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.duration = 0.15f;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.2f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.78f, 1f), Color.white);
            main.gravityModifier = 0.8f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var em = ps.emission;
            em.rateOverTime = 0f;
            em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Clamp(_radius * _radius * 8f, 80, 380)) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = _radius;
            shape.radiusThickness = 0f;          // only the surface, flying outward
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = Fx.Soft;
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.velocityScale = 0.05f;
            return ps;
        }

        /// <summary>A jagged line between two points (3D kinks, smaller near the ends).</summary>
        private static void Jag(LineRenderer lr, Vector3 a, Vector3 b, int n, float amp)
        {
            lr.positionCount = n + 1;
            for (int i = 0; i <= n; i++)
            {
                float t = (float)i / n;
                var p = Vector3.Lerp(a, b, t);
                if (i > 0 && i < n)
                    p += Random.insideUnitSphere * amp * Mathf.Sin(t * Mathf.PI);
                lr.SetPosition(i, p);
            }
        }

        private void Reshape(float front)
        {
            // arcs from the impact out to the shell, in every direction
            foreach (var arc in _arcs)
            {
                var end = _centre + Random.onUnitSphere * front * Random.Range(0.8f, 1f);
                Jag(arc, _centre, end, Mathf.Max(4, (int)(front * 2f)), 0.35f + front * 0.06f);
            }
            // great circles crawling over the shell, a little jagged
            for (int k = 0; k < _rings.Count; k++)
            {
                _ringTilt[k] = Quaternion.AngleAxis(Random.Range(4f, 10f), Random.onUnitSphere) * _ringTilt[k];
                var lr = _rings[k];
                lr.positionCount = RingPoints;
                for (int i = 0; i < RingPoints; i++)
                {
                    float a = i * Mathf.PI * 2f / RingPoints;
                    var p = _ringTilt[k] * new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * front * (1f + Random.Range(-0.04f, 0.04f));
                    lr.SetPosition(i, _centre + p);
                }
            }
        }

        private void Update()
        {
            _age += Time.deltaTime;
            float t = Mathf.Clamp01(_age / Lifetime);
            float w = Mathf.Clamp01(_age / WaveTime);
            float front = _radius * (1f - (1f - w) * (1f - w));       // ease-out: fast at first
            if (w >= 1f && !_sparksFired)
            {
                _sparksFired = true;
                _sparks.Play();
            }
            if (Time.time >= _nextReshape)
            {
                _nextReshape = Time.time + 0.05f;
                Reshape(Mathf.Max(0.2f, front));
            }
            bool flicker = (int)(_age / 0.04f) % 4 != 3;
            float fade = 1f - t;

            _shell.localScale = Vector3.one * front * 2f;
            _shell.Rotate(Random.onUnitSphere, 90f * Time.deltaTime, Space.World);
            _shellMat.color = new Color(0.7f, 0.85f, 1f, (flicker ? 0.55f : 0.3f) * fade);
            // the core: small, white and strong at the impact (full damage within 1 m), dying fast
            _core.localScale = Vector3.one * Mathf.Min(2f, front * 0.45f) * (1f + 0.15f * Mathf.Sin(_age * 60f));
            _coreMat.color = new Color(1f, 1f, 1f, 0.75f * fade * fade);
            foreach (var arc in _arcs)
            {
                arc.enabled = flicker && t < 0.75f && Random.value > 0.15f;
                arc.startColor = new Color(1f, 1f, 1f, fade);
                arc.endColor = new Color(0.45f, 0.62f, 1f, 0.15f * fade);
            }
            foreach (var ring in _rings)
            {
                ring.enabled = flicker || Random.value > 0.5f;
                ring.startColor = ring.endColor = new Color(0.6f, 0.78f, 1f, 0.8f * fade);
            }
        }

        private void OnDestroy()
        {
            if (_shellMat != null) Destroy(_shellMat);
            if (_coreMat != null) Destroy(_coreMat);
        }
    }
}
