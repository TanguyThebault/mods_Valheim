using System.Linq;
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
        public static GameObject Slash;
        public static GameObject Quake;
        public static GameObject Rustle;
        public static GameObject Reel, FishFall, FishImpact, Strain, PortalOpen, PortalClose;

        public static void Create(params EffectList[] templateSources)
        {
            var template = FindTemplate(templateSources);
            Charge = Make("sfx_thunderspear_charge", template, Wav.Load("thunder_charge"), 0.98f, 1.02f, 2f, 25f);
            Strike = Make("sfx_thunderspear_strike", template, Wav.Load("thunder_strike"), 0.9f, 1.08f, 15f, 220f);
        }

        /// <summary>The Wind Blade's air slash (sfx/wind_slash_*.wav, tools/synth_wind.py).</summary>
        public static void CreateSlash(params EffectList[] templateSources)
        {
            Slash = Make("sfx_windblade_slash", FindTemplate(templateSources), Wav.Load("wind_slash"), 0.92f, 1.08f, 4f, 45f);
        }

        /// <summary>The Earthbreaker's quake (sfx/quake_*.wav, tools/synth_earth.py).</summary>
        public static void CreateQuake(params EffectList[] templateSources)
        {
            Quake = Make("sfx_earthbreaker_quake", FindTemplate(templateSources), Wav.Load("quake"), 0.92f, 1.04f, 10f, 110f);
        }

        /// <summary>The Fox Fang's rustle of leaves (sfx/rustle_*.wav, tools/synth_earth.py).</summary>
        public static void CreateRustle(params EffectList[] templateSources)
        {
            Rustle = Make("sfx_knifefox_rustle", FindTemplate(templateSources), Wav.Load("rustle"), 0.9f, 1.1f, 2f, 20f);
        }

        /// <summary>The Skyfisher's Rod: reel, falling fish, impact (sfx/reel_*, fishfall_*, fishimpact_*, tools/synth_fish.py).</summary>
        public static void CreateFishing(params EffectList[] templateSources)
        {
            var t = FindTemplate(templateSources);
            Reel = Make("sfx_rodsky_reel", t, Wav.Load("reel"), 0.95f, 1.05f, 2f, 25f);
            FishFall = Make("sfx_rodsky_fall", t, Wav.Load("fishfall"), 0.95f, 1.05f, 30f, 220f);
            FishImpact = Make("sfx_rodsky_impact", t, Wav.Load("fishimpact"), 0.9f, 1.05f, 20f, 200f);
            Strain = Make("sfx_rodsky_strain", t, Wav.Load("strain"), 0.95f, 1.05f, 3f, 30f);
            PortalOpen = Make("sfx_rodsky_portal_open", t, Wav.Load("portal_open"), 0.97f, 1.03f, 40f, 400f);
            PortalClose = Make("sfx_rodsky_portal_close", t, Wav.Load("portal_close"), 0.97f, 1.03f, 40f, 400f);
        }

        /// <summary>The Fog Horn: the call, a plain blow, spirits rising and fading (sfx/horn_*, ghost_*, tools/synth_horn.py).</summary>
        public static void CreateHorn(params EffectList[] templateSources)
        {
            var t = FindTemplate(templateSources);
            FogHorn.CallSfx = Make("sfx_hornfog_call", t, Wav.Load("horn_call"), 0.98f, 1.02f, 40f, 600f);
            FogHorn.ShortSfx = Make("sfx_hornfog_blow", t, Wav.Load("horn_short"), 0.97f, 1.03f, 25f, 350f);
            FogHorn.RiseSfx = Make("sfx_hornfog_rise", t, Wav.Load("ghost_rise"), 0.95f, 1.05f, 3f, 40f);
            FogHorn.FadeSfx = Make("sfx_hornfog_fade", t, Wav.Load("ghost_fade"), 0.95f, 1.05f, 3f, 30f);
            FogHorn.ReleaseSfx = Make("sfx_hornfog_release", t, Wav.Load("horn_release"), 1f, 1f, 25f, 350f);
            FogHorn.HoldClip = Wav.Load("horn_hold").FirstOrDefault();
        }

        /// <summary>Surtrbrand: driven in, the dome rising, fading, pulled out (sfx/surtr_*, tools/synth_ember.py).</summary>
        public static void CreateSurtr(params EffectList[] templateSources)
        {
            var t = FindTemplate(templateSources);
            Surtrbrand.PlantSfx = Make("sfx_swordsurtr_plant", t, Wav.Load("surtr_plant"), 0.95f, 1.05f, 6f, 60f);
            Surtrbrand.DomeSfx = Make("sfx_swordsurtr_dome", t, Wav.Load("surtr_dome"), 0.97f, 1.03f, 10f, 90f);
            Surtrbrand.DomeEndSfx = Make("sfx_swordsurtr_fade", t, Wav.Load("surtr_fade"), 0.97f, 1.03f, 8f, 70f);
            Surtrbrand.PickupSfx = Make("sfx_swordsurtr_pull", t, Wav.Load("surtr_pull"), 0.95f, 1.05f, 4f, 40f);
            Surtrbrand.BlastSfx = Make("sfx_swordsurtr_blast", t, Wav.Load("surtr_blast"), 0.96f, 1.04f, 10f, 90f);
            Surtrbrand.CampClip = Wav.Load("surtr_camp").FirstOrDefault();
            var src = t != null ? t.GetComponent<AudioSource>() : null;
            Surtrbrand.Mixer = src != null ? src.outputAudioMixerGroup : null;
        }

        /// <summary>Ymir's Bite: the frost rising in the blood, and its end (sfx/ymir_*, tools/synth_frost.py).</summary>
        public static void CreateYmir(params EffectList[] templateSources)
        {
            var t = FindTemplate(templateSources);
            YmirBite.CastSfx = Make("sfx_atgeirymir_cast", t, Wav.Load("ymir_cast"), 0.96f, 1.04f, 4f, 40f);
            YmirBite.EndSfx = Make("sfx_atgeirymir_end", t, Wav.Load("ymir_end"), 0.96f, 1.04f, 2f, 20f);
            YmirBite.GiantSfx = Make("sfx_atgeirymir_giant", t, Wav.Load("ymir_giant"), 0.97f, 1.03f, 8f, 80f);
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
