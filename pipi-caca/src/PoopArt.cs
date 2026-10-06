using UnityEngine;

namespace Caca
{
    /// <summary>
    /// The poop, made in code (no model file): an elongated, slightly bent and lumpy stone, poop brown, with a
    /// matching mottled texture and an inventory icon drawn from the same shape.
    /// </summary>
    internal static class PoopArt
    {
        public static readonly Color Brown = new Color(0.36f, 0.22f, 0.10f);
        public static readonly Color DarkBrown = new Color(0.20f, 0.12f, 0.05f);

        private static float Noise(Vector3 p, float seed)
        {
            return Mathf.Sin(p.x * 13.1f + seed) * 0.5f + Mathf.Sin(p.y * 17.3f - seed * 1.7f) * 0.3f
                   + Mathf.Sin(p.z * 9.7f + p.x * 5.1f + seed * 0.6f) * 0.4f + Mathf.Sin(p.z * 31f + p.y * 23f) * 0.12f;
        }

        /// <summary>About 0.2 m long (z), 0.08 m wide, 0.07 m tall, resting on y = 0 at its middle.</summary>
        public static Mesh Mesh()
        {
            const int rings = 24, segs = 18;
            var verts = new Vector3[(rings + 1) * (segs + 1)];
            var uvs = new Vector2[verts.Length];
            int k = 0;
            for (int r = 0; r <= rings; r++)
            {
                float v = (float)r / rings;                      // 0 .. 1 along the length
                float lat = Mathf.Lerp(-Mathf.PI / 2, Mathf.PI / 2, v);
                for (int s = 0; s <= segs; s++)
                {
                    float u = (float)s / segs;
                    float lon = u * Mathf.PI * 2f;
                    var p = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon), Mathf.Cos(lat) * Mathf.Sin(lon), Mathf.Sin(lat));
                    // elongated, one end thicker than the other, lumpy, pinched in three segments
                    float taper = 1f - 0.35f * v;
                    float constrict = 1f - 0.3f * Mathf.Pow(0.5f + 0.5f * Mathf.Cos(v * Mathf.PI * 6f), 3f);
                    float lump = (1f + 0.1f * Noise(p * 1.3f, 2.1f)) * constrict;
                    var q = new Vector3(p.x * 0.042f * taper * lump, p.y * 0.036f * taper * lump, p.z * 0.1f * (1f + 0.05f * Noise(p, 5f)));
                    q.y = Mathf.Max(q.y, -0.026f);                // a flatter belly where it lies
                    q.y += 0.012f * (1f - (q.z / 0.1f) * (q.z / 0.1f)); // slight banana bend
                    q.y += 0.018f * Mathf.Pow(Mathf.Max(0f, v - 0.7f) / 0.3f, 2f);   // the tip curls up
                    q.x += 0.01f * Mathf.Sin(q.z * 25f);           // and a little twist
                    verts[k] = q;
                    uvs[k] = new Vector2(u, v);
                    k++;
                }
            }
            var tris = new int[rings * segs * 6];
            int t = 0;
            for (int r = 0; r < rings; r++)
                for (int s = 0; s < segs; s++)
                {
                    int a = r * (segs + 1) + s, b = a + segs + 1;
                    tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                    tris[t++] = a + 1; tris[t++] = b + 1; tris[t++] = b;
                }
            var mesh = new Mesh { name = "caca_mesh", vertices = verts, uv = uvs, triangles = tris };
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        public static Texture2D Texture()
        {
            const int n = 64;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, true) { name = "caca_tex", wrapMode = TextureWrapMode.Repeat };
            var rng = new System.Random(7);
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float m = 0.5f + 0.5f * Mathf.Sin(x * 0.4f + Mathf.Sin(y * 0.3f) * 2f) * Mathf.Cos(y * 0.25f);
                    float speck = (float)rng.NextDouble();
                    var c = Color.Lerp(DarkBrown, Brown, 0.55f + 0.45f * m);
                    if (speck > 0.93f) c = Color.Lerp(c, new Color(0.5f, 0.38f, 0.16f), 0.6f);   // undigested bits
                    px[y * n + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        public static Texture2D FlatNormal()
        {
            var t = new Texture2D(4, 4, TextureFormat.RGBA32, false, true) { name = "caca_flat_normal" };
            var px = new Color[16];
            for (int i = 0; i < 16; i++) px[i] = new Color(0.5f, 0.5f, 1f, 0.5f);
            t.SetPixels(px);
            t.Apply(false, true);
            return t;
        }

        /// <summary>Inventory icon: the poop seen from the side, diagonal, with a highlight and a soft outline.</summary>
        public static Sprite Icon()
        {
            const int n = 128;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "caca_icon" };
            var px = new Color[n * n];
            var axis = new Vector2(1f, 0.55f).normalized;
            var perp = new Vector2(-axis.y, axis.x);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2(x - n / 2f, y - n / 2f);
                    float a = Vector2.Dot(p, axis) / 52f;      // along
                    float b = Vector2.Dot(p, perp) / 22f;      // across
                    b -= 0.25f * (1f - a * a);                 // the bend
                    float lump = 1f + 0.1f * Mathf.Sin(a * 9f) + 0.06f * Mathf.Sin(a * 23f);
                    float width = Mathf.Sqrt(Mathf.Max(0f, 1f - a * a)) * (1.05f - 0.2f * a) * lump;
                    float d = Mathf.Abs(b) - width;             // < 0 inside
                    Color c = new Color(0, 0, 0, 0);
                    if (d < 0.18f)
                    {
                        if (d >= 0f) c = new Color(0.1f, 0.06f, 0.02f, 1f - d / 0.18f);   // outline
                        else
                        {
                            float shade = Mathf.Clamp01(0.55f + 0.5f * (b / Mathf.Max(width, 0.01f)));
                            c = Color.Lerp(DarkBrown, Brown * 1.25f, shade);
                            float hl = Mathf.Exp(-Mathf.Pow((b / Mathf.Max(width, 0.01f) - 0.55f) / 0.18f, 2f)) * Mathf.Clamp01(1f - Mathf.Abs(a + 0.15f) * 1.6f);
                            c = Color.Lerp(c, new Color(0.75f, 0.55f, 0.3f), hl * 0.6f);   // a glossy highlight
                            c.a = 1f;
                        }
                    }
                    px[y * n + x] = c;
                }
            tex.SetPixels(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, n, n), new Vector2(0.5f, 0.5f), 100f);
        }
    }
}
