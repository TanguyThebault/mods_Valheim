using UnityEngine;

namespace Caca
{
    /// <summary>Small procedural textures for the stream (soft edges: a bare Sprites/Default draws white squares).</summary>
    internal static class PeeArt
    {
        private static Mesh s_quad;

        /// <summary>A 1 x 1 quad lying flat (facing +y), centered.</summary>
        public static Mesh Quad()
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh
            {
                name = "caca_quad",
                vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) },
                uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
            };
            s_quad.RecalculateNormals();
            s_quad.RecalculateBounds();
            return s_quad;
        }

        /// <summary>Across the jet (v): transparent edges, a brighter core and a thin highlight on one side.</summary>
        public static Texture2D JetTexture()
        {
            const int w = 4, h = 32;
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "caca_jet", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < h; y++)
            {
                float v = (y + 0.5f) / h * 2f - 1f;                       // -1 .. 1 across
                float a = Mathf.Clamp01(1f - Mathf.Pow(Mathf.Abs(v), 3f));
                float glint = Mathf.Exp(-Mathf.Pow((v - 0.35f) / 0.12f, 2f));
                var c = Color.Lerp(new Color(0.85f, 0.85f, 0.85f), Color.white, glint);
                c.a = a * (0.75f + 0.25f * glint);
                for (int x = 0; x < w; x++) t.SetPixel(x, y, c);
            }
            t.Apply();
            return t;
        }

        /// <summary>A soft round drop with a highlight.</summary>
        public static Texture2D DropTexture()
        {
            const int n = 32;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "caca_drop", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01((1f - r) * 3f);
                    float hl = Mathf.Exp(-((dx + 0.3f) * (dx + 0.3f) + (dy - 0.3f) * (dy - 0.3f)) / 0.06f);
                    var c = Color.Lerp(new Color(0.85f, 0.85f, 0.85f), Color.white, hl);
                    c.a = a;
                    t.SetPixel(x, y, c);
                }
            t.Apply();
            return t;
        }

        /// <summary>A wet patch: irregular blob, darker and denser in the middle, soft ragged rim.</summary>
        public static Texture2D WetTexture()
        {
            const int n = 64;
            var t = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "caca_wet", wrapMode = TextureWrapMode.Clamp };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float ang = Mathf.Atan2(dy, dx);
                    float rim = 0.78f + 0.08f * Mathf.Sin(ang * 3f + 0.7f) + 0.06f * Mathf.Sin(ang * 7f + 2.1f) + 0.03f * Mathf.Sin(ang * 13f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy) / rim;
                    float a = Mathf.Clamp01((1f - r) * 4f) * (0.75f + 0.25f * Mathf.Clamp01(1f - r));
                    t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            t.Apply();
            return t;
        }
    }
}
