using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Gives a cloned creature its own look and voice, so nothing of the base animal shows through:
    /// - Paint: its own textures, from a colour mask painted in the base model's UV layout (tools/paint_masks.py)
    ///   multiplied by the base texture's shading;
    /// - OwnRagdolls: its own ragdoll prefab (painted, scaled with the creature), instead of the base one;
    /// - Voice: its own sound prefabs (cloned vanilla ZSFX with our clips) in place of the base animal's sounds.
    /// </summary>
    internal static class Look
    {
        internal static string PluginDir;
        private static readonly Dictionary<string, Texture2D> s_masks = new Dictionary<string, Texture2D>();
        private static readonly Dictionary<string, Texture2D> s_painted = new Dictionary<string, Texture2D>();

        // ------------------------------------------------------------- textures

        public static Texture2D Mask(string name)
        {
            if (s_masks.TryGetValue(name, out var cached))
                return cached;
            string path = Path.Combine(PluginDir, name + "_colors.png");
            Texture2D tex = null;
            if (File.Exists(path))
            {
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(File.ReadAllBytes(path)))
                    tex = null;
            }
            if (tex == null)
                Plugin.Log.LogWarning("Colour mask missing: " + path);
            s_masks[name] = tex;
            return tex;
        }

        /// <summary>
        /// Starred creatures: the vanilla LevelEffects shift the hue of the main material (a hare with one star
        /// turns purple on our brown coat). Ours keep their coat: no hue shift, no glow, no swapped model, only a
        /// slightly darker and richer fur per star. The size bump stays.
        /// </summary>
        public static void NaturalLevels(GameObject go)
        {
            foreach (var le in go.GetComponentsInChildren<LevelEffects>(true))
            {
                for (int i = 0; i < le.m_levelSetups.Count; i++)
                {
                    var s = le.m_levelSetups[i];
                    Plugin.Log.LogDebug(go.name + " star " + (i + 1) + " was: scale " + s.m_scale + ", hue " + s.m_hue +
                                        ", saturation " + s.m_saturation + ", value " + s.m_value +
                                        (s.m_setEmissiveColor ? ", emissive " + s.m_emissiveColor : "") +
                                        (s.m_enableObject != null ? ", object " + s.m_enableObject.name : ""));
                    s.m_hue = 0f;
                    s.m_saturation = 0.05f * (i + 1);
                    s.m_value = -0.08f * (i + 1);
                    s.m_setEmissiveColor = false;
                    s.m_enableObject = null;
                }
                le.m_baseEnableObject = null;
            }
        }

        /// <summary>Clones every material of go and gives it the mask x shading texture. Falls back to a tint.</summary>
        public static void Paint(GameObject go, Texture2D mask, Color fallbackTint)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>(true))
            {
                if (r is ParticleSystemRenderer)
                    continue;
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                {
                    if (mats[i] == null)
                        continue;
                    var m = new Material(mats[i]) { name = mats[i].name + "_" + go.name };
                    var src = m.HasProperty("_MainTex") ? m.GetTexture("_MainTex") : null;
                    if (mask != null && src != null)
                    {
                        string key = src.GetInstanceID() + "/" + mask.GetInstanceID();
                        if (!s_painted.TryGetValue(key, out var tex))
                            s_painted[key] = tex = Colorize(src, mask);
                        m.SetTexture("_MainTex", tex);
                        if (m.HasProperty("_Color"))
                            m.SetColor("_Color", Color.white);
                    }
                    else if (m.HasProperty("_Color"))
                    {
                        m.SetColor("_Color", m.GetColor("_Color") * fallbackTint);
                    }
                    if (m.HasProperty("_EmissionColor"))
                        m.SetColor("_EmissionColor", Color.black);
                    mats[i] = m;
                }
                r.sharedMaterials = mats;
            }
        }

        private static Texture2D Colorize(Texture src, Texture2D mask)
        {
            int w = mask.width, h = mask.height;
            var shade = ReadThroughGpu(src, w, h).GetPixels();
            var lum = shade.Select(c => 0.3f * c.r + 0.59f * c.g + 0.11f * c.b).OrderBy(v => v).ToArray();
            float lo = lum[(int)(lum.Length * 0.02f)], hi = Mathf.Max(lum[(int)(lum.Length * 0.98f)], lo + 0.01f);
            var col = mask.GetPixels();
            var px = new Color[col.Length];
            for (int i = 0; i < px.Length; i++)
            {
                float l = Mathf.Clamp01((0.3f * shade[i].r + 0.59f * shade[i].g + 0.11f * shade[i].b - lo) / (hi - lo));
                var c = col[i] * (0.75f + 0.5f * l);
                c.a = 1f;
                px[i] = c;
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = src.name + "_painted" };
            tex.SetPixels(px);
            tex.Apply(true);
            return tex;
        }

        /// <summary>Copies any texture (readable or not) through the GPU into a readable one.</summary>
        private static Texture2D ReadThroughGpu(Texture src, int w, int h)
        {
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply(false);
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return tex;
        }

        // -------------------------------------------------------------- ragdolls

        /// <summary>
        /// Replaces each ragdoll in a death EffectList by our own painted copy, made to inherit the creature's
        /// scale (EffectList.Create: m_inheritParentScale copies the dying creature's localScale).
        /// </summary>
        public static void OwnRagdolls(EffectList deathEffects, string prefix, Texture2D mask, Color fallbackTint)
        {
            if (deathEffects?.m_effectPrefabs == null)
                return;
            foreach (var ed in deathEffects.m_effectPrefabs)
            {
                if (ed?.m_prefab == null || ed.m_prefab.GetComponent<Ragdoll>() == null)
                    continue;
                var clone = PrefabManager.Instance.CreateClonedPrefab(prefix + "_ragdoll", ed.m_prefab);
                Paint(clone, mask, fallbackTint);
                PrefabManager.Instance.AddPrefab(new CustomPrefab(clone, true));
                ed.m_prefab = clone;
                ed.m_scale = false;
                ed.m_multiplyParentVisualScale = false;
                ed.m_inheritParentScale = true;
                Plugin.Log.LogInfo("Own ragdoll " + clone.name);
            }
        }

        // ---------------------------------------------------------------- sounds

        public static AudioClip[] LoadClips(string folder, string prefix)
        {
            string dir = Path.Combine(PluginDir, folder);
            if (!Directory.Exists(dir))
            {
                Plugin.Log.LogWarning("No sound folder " + dir);
                return new AudioClip[0];
            }
            return Directory.GetFiles(dir, prefix + "*.wav").OrderBy(p => p).Select(LoadWav).Where(c => c != null).ToArray();
        }

        /// <summary>A sound prefab of our own: a clone of a vanilla ZSFX prefab (mixer, 3D settings) with our clips.</summary>
        public static GameObject MakeSfx(string name, GameObject template, AudioClip[] clips, float minPitch = 0.92f, float maxPitch = 1.08f)
        {
            if (template == null || clips.Length == 0)
                return null;
            var go = PrefabManager.Instance.CreateClonedPrefab(name, template);
            var sfx = go.GetComponent<ZSFX>();
            sfx.m_audioClips = clips;
            sfx.m_minPitch = minPitch;
            sfx.m_maxPitch = maxPitch;
            sfx.m_closedCaptionToken = "";
            sfx.m_secondaryCaptionToken = "";
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            return go;
        }

        /// <summary>First vanilla sound prefab (ZSFX) found in these effect lists, to clone our sounds from.</summary>
        public static GameObject FindSfxTemplate(params EffectList[] lists)
        {
            foreach (var l in lists)
                if (l?.m_effectPrefabs != null)
                    foreach (var ed in l.m_effectPrefabs)
                        if (ed?.m_prefab != null && ed.m_prefab.GetComponent<ZSFX>() != null)
                            return ed.m_prefab;
            return null;
        }

        /// <summary>Swaps every sound in the list for ours (other effects, like blood, stay). Null: just remove sounds.</summary>
        public static EffectList Voice(EffectList list, GameObject sfx)
        {
            var kept = new List<EffectList.EffectData>();
            bool hadSound = false;
            if (list?.m_effectPrefabs != null)
                foreach (var ed in list.m_effectPrefabs)
                {
                    if (ed?.m_prefab != null && ed.m_prefab.GetComponent<ZSFX>() != null)
                        hadSound = true;
                    else if (ed != null)
                        kept.Add(ed);
                }
            if (sfx != null && (hadSound || kept.Count == 0))
                kept.Add(new EffectList.EffectData { m_prefab = sfx, m_enabled = true });
            return new EffectList { m_effectPrefabs = kept.ToArray() };
        }

        /// <summary>Minimal RIFF reader: 16-bit PCM, mono or stereo (stereo is downmixed).</summary>
        private static AudioClip LoadWav(string path)
        {
            try
            {
                var b = File.ReadAllBytes(path);
                int channels = 0, rate = 0, bits = 0, pos = 12;
                while (pos + 8 <= b.Length)
                {
                    string id = Encoding.ASCII.GetString(b, pos, 4);
                    int size = System.BitConverter.ToInt32(b, pos + 4);
                    int body = pos + 8;
                    if (id == "fmt ")
                    {
                        channels = System.BitConverter.ToInt16(b, body + 2);
                        rate = System.BitConverter.ToInt32(b, body + 4);
                        bits = System.BitConverter.ToInt16(b, body + 14);
                    }
                    else if (id == "data" && bits == 16 && channels > 0)
                    {
                        int frames = size / (2 * channels);
                        var data = new float[frames];
                        for (int f = 0; f < frames; f++)
                        {
                            float sum = 0f;
                            for (int c = 0; c < channels; c++)
                                sum += System.BitConverter.ToInt16(b, body + (f * channels + c) * 2) / 32768f;
                            data[f] = sum / channels;
                        }
                        var clip = AudioClip.Create(Path.GetFileNameWithoutExtension(path), frames, 1, rate, false);
                        clip.SetData(data, 0);
                        return clip;
                    }
                    pos = body + size + (size & 1);
                }
                Plugin.Log.LogWarning("Unsupported WAV (need 16-bit PCM): " + path);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning("Failed to read " + path + ": " + e.Message);
            }
            return null;
        }
    }
}
