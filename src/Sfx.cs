using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;

namespace LegendaryWeapons
{
    /// <summary>
    /// Our synthesized sounds (sfx/*.wav, made by tools/synth_thunder.py), wrapped in clones of a vanilla ZSFX
    /// prefab so they go through the game's mixer and volume settings.
    /// </summary>
    internal static class Sfx
    {
        public static GameObject Charge;
        public static GameObject Strike;

        public static void Create(params EffectList[] templateSources)
        {
            var template = FindTemplate(templateSources);
            Charge = Make("sfx_thunderspear_charge", template, Wav.Load("thunder_charge"), 0.98f, 1.02f, 2f, 25f);
            Strike = Make("sfx_thunderspear_strike", template, Wav.Load("thunder_strike"), 0.9f, 1.08f, 15f, 220f);
        }

        public static void Play(GameObject prefab, Vector3 pos, Transform parent)
        {
            if (prefab == null)
                return;
            var go = Object.Instantiate(prefab, pos, Quaternion.identity);
            if (parent != null)
                go.transform.SetParent(parent, true);
        }

        private static GameObject FindTemplate(EffectList[] lists)
        {
            foreach (var l in lists)
                if (l?.m_effectPrefabs != null)
                    foreach (var ed in l.m_effectPrefabs)
                        if (ed?.m_prefab != null && ed.m_prefab.GetComponent<ZSFX>() != null)
                            return ed.m_prefab;
            foreach (var n in new[] { "sfx_spear_throw", "sfx_build_hammer_default", "sfx_pickable_pick" })
            {
                var p = PrefabManager.Instance.GetPrefab(n);
                if (p != null && p.GetComponent<ZSFX>() != null)
                    return p;
            }
            return null;
        }

        private static GameObject Make(string name, GameObject template, AudioClip[] clips, float minPitch,
            float maxPitch, float minDistance, float maxDistance)
        {
            if (template == null || clips.Length == 0)
            {
                Plugin.Log.LogWarning(name + ": no " + (template == null ? "sound template" : "clips"));
                return null;
            }
            var go = PrefabManager.Instance.CreateClonedPrefab(name, template);
            var sfx = go.GetComponent<ZSFX>();
            sfx.m_audioClips = clips;
            sfx.m_minPitch = minPitch;
            sfx.m_maxPitch = maxPitch;
            sfx.m_closedCaptionToken = "";
            sfx.m_secondaryCaptionToken = "";
            sfx.m_fadeOutOnAwake = false;
            sfx.m_minDelay = sfx.m_maxDelay = 0f;
            sfx.m_maxConcurrentSources = 0;
            var src = go.GetComponent<AudioSource>();
            if (src != null)
            {
                src.volume = 1f;
                src.loop = false;
                src.rolloffMode = AudioRolloffMode.Linear;
                src.minDistance = minDistance;
                src.maxDistance = maxDistance;
            }
            // Long clips: make sure the instance outlives them.
            var timed = go.GetComponent<TimedDestruction>();
            if (timed != null)
                timed.m_timeout = Mathf.Max(timed.m_timeout, 6f);
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            return go;
        }
    }
}
