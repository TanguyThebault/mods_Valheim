using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// Inventory icons: the weapon rendered on the diagonal (grip bottom-left, head top-right, blade face to the
    /// camera, a slight turn for depth) so it fills the slot, with a soft halo of its element's colour behind it.
    /// </summary>
    internal static class Icons
    {
        private const int Size = 128;

        /// <summary>head / bladeSide: directions in the item prefab's own space; null: the game's isometric view.</summary>
        public static void Make(CustomItem item, Vector3? head, Vector3? bladeSide, bool flip = false)
        {
            var go = item.ItemPrefab;
            if (!WeaponAuras.ByPrefab.TryGetValue(go.name, out var info))
                return;
            try
            {
                var req = new RenderManager.RenderRequest(go) { Width = Size, Height = Size, UseCache = false };
                req.Rotation = head.HasValue && bladeSide.HasValue ? Diagonal(head.Value, bladeSide.Value) : RenderManager.IsometricRotation;
                if (flip)                                       // half a turn in the picture's plane
                    req.Rotation = Quaternion.AngleAxis(180f, Vector3.forward) * req.Rotation;
                var sprite = RenderManager.Instance.Render(req);
                if (sprite == null)
                    return;
                var haloed = Halo(sprite.texture, WeaponAuras.Main(info.Element));
                item.ItemDrop.m_itemData.m_shared.m_icons = new[] { haloed ?? sprite };
            }
            catch (System.Exception ex)
            {
                Plugin.Log.LogWarning("Icon render failed for " + go.name + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Jotunn's camera sits on +z looking back at the origin (turned 180 degrees about y): screen right is -x,
        /// up is +y. The head goes up-right, the blade's side up-left, its face to the camera, then a 20-degree turn.
        /// </summary>
        private static Quaternion Diagonal(Vector3 head, Vector3 side)
        {
            head.Normalize();
            side = Vector3.ProjectOnPlane(side, head).normalized;
            Vector3 headT = new Vector3(-1f, 1f, 0f).normalized, sideT = new Vector3(1f, 1f, 0f).normalized;
            var src = Quaternion.LookRotation(Vector3.Cross(side, head), head);
            var dst = Quaternion.LookRotation(Vector3.Cross(sideT, headT), headT);
            return Quaternion.AngleAxis(20f, headT) * dst * Quaternion.Inverse(src);
        }

        /// <summary>A soft glow round the silhouette (blurred alpha), the weapon drawn over it.</summary>
        private static Sprite Halo(Texture2D src, Color glow)
        {
            Color32[] px;
            try { px = src.GetPixels32(); }
            catch { return null; }                              // not readable: keep the plain render
            int w = src.width, h = src.height;
            var a = new float[w * h];
            for (int i = 0; i < a.Length; i++) a[i] = px[i].a / 255f;
            var blur = BoxBlur(BoxBlur(a, w, h, 5), w, h, 5);
            var outPx = new Color32[w * h];
            for (int i = 0; i < outPx.Length; i++)
            {
                float ha = Mathf.Clamp01(blur[i] * 2.4f) * 0.55f;
                float sa = px[i].a / 255f;
                float oa = sa + ha * (1f - sa);
                Color c = oa > 0f
                    ? (new Color(px[i].r / 255f, px[i].g / 255f, px[i].b / 255f) * sa + glow * ha * (1f - sa)) / oa
                    : Color.clear;
                c.a = oa;
                outPx[i] = c;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = src.name + "_halo" };
            tex.SetPixels32(outPx);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, w, h), Vector2.one / 2f);
        }

        private static float[] BoxBlur(float[] a, int w, int h, int r)
        {
            var tmp = new float[a.Length];
            var outA = new float[a.Length];
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float s = 0f;
                int n = 0;
                for (int k = -r; k <= r; k++)
                {
                    int xx = x + k;
                    if (xx < 0 || xx >= w) continue;
                    s += a[y * w + xx];
                    n++;
                }
                tmp[y * w + x] = s / n;
            }
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float s = 0f;
                int n = 0;
                for (int k = -r; k <= r; k++)
                {
                    int yy = y + k;
                    if (yy < 0 || yy >= h) continue;
                    s += tmp[yy * w + x];
                    n++;
                }
                outA[y * w + x] = s / n;
            }
            return outA;
        }
    }
}
