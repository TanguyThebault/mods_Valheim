using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Configs;
using Jotunn.Entities;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace Caca
{
    /// <summary>
    /// Caca: eating fills a hidden urge, little by little. From half-way a status effect shows (like the cold) and
    /// you can go on command (K, or `caca` in the console); when it is full it happens on its own. A fart, a poop
    /// lands behind you: an elongated, poop-brown stone you can pick up and throw, which bursts into a brown splash.
    /// Pipi: a second urge that fills on its own; from half-way, L; when full, it starts by itself. The player keeps
    /// walking while a simulated stream, aimed with the mouse, empties the urge in a few seconds (Pee, PeeStream).
    /// Pets: a hidden third gauge; when full the character farts on their own, sound and a faint cloud only (Fart).
    /// Everything is in the config file (BepInEx/config/lekinox.pipicacamod.cfg), reloaded live.
    /// </summary>
    [BepInPlugin(Guid, ModName, Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "lekinox.pipicacamod";
        public const string ModName = "Pipi + Caca Mod";
        public const string Version = "0.5.0";
        public const string ItemName = "Caca";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static Plugin Instance;
        internal static ConfigEntry<KeyCode> Key;
        internal static ConfigEntry<float> MinNeed;
        internal static ConfigEntry<float> DigestPerMinute;
        internal static ConfigEntry<float> NeedPerFoodHealth;
        internal static ConfigEntry<bool> ShowBars, LabEnabled;
        internal static ConfigEntry<KeyCode> PeeKey;
        internal static ConfigEntry<float> PeeMinNeed, PeeFillPerMinute, PeeDrainSeconds, PeeMinSpeed, PeeMaxSpeed;
        internal static ConfigEntry<float> PeeAimLift, PeeVolume, FingerCurl, CacaVolume;
        internal static ConfigEntry<float> TipFromHand, UrgentAt;
        internal static ConfigEntry<bool> EnablePoop, EnablePee, EnableFarts, FartCloud, WetPatches;
        internal static ConfigEntry<float> FartFillPerMinute, FartPerMeal, FartCloudOpacity;
        internal static ConfigEntry<PeePose> Pose;
        internal static ConfigEntry<float> SquatPitch, SquatSpeed;
        internal static ConfigEntry<string> SquatTipOffset;
        internal static ConfigEntry<string> TipOffset, WristOffset, FingerDir, HeldPos, HeldRot;
        internal static GameObject PoopPrefab;
        internal static AudioClip PeeGroundClip, PeeWaterClip;
        internal static UnityEngine.Audio.AudioMixerGroup SfxMixer;
        internal static GameObject ProjectilePrefab;
        private static GameObject s_fartSfx, s_plopSfx, s_splatSfx;
        private System.DateTime _configStamp;
        private float _nextConfigCheck;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            EnablePoop = Config.Bind("General", "EnablePoop", true, "The poop urge (status effect, K, forced at 100 %).");
            EnablePee = Config.Bind("General", "EnablePee", true, "The pee urge (status effect, L, forced at 100 %).");
            EnableFarts = Config.Bind("General", "EnableFarts", true, "The hidden fart gauge (farts on its own when full).");
            UrgentAt = Config.Bind("General", "UrgentAt", 85f, "Urge (%) from which the status effects turn urgent and flash.");
            Key = Config.Bind("Controls", "Key", KeyCode.K, "Key to poop on command (once the urge is past MinNeed).");
            PeeKey = Config.Bind("Controls", "PeeKey", KeyCode.L, "Key to pee on command (once the urge is past PeeMinNeed); press again to stop early.");
            MinNeed = Config.Bind("Urge", "MinNeed", 50f, "Urge (%) from which the status effect shows and you can go on command. At 100 % it happens on its own.");
            DigestPerMinute = Config.Bind("Urge", "DigestPerMinute", 6f, "How fast eaten food turns into urge (% per minute).");
            NeedPerFoodHealth = Config.Bind("Urge", "NeedPerFoodHealth", 0.35f,
                "Urge added per point of the food's health value (a 30-health food adds about 10 %, clamped to 4-35 %).");
            ShowBars = Config.Bind("Debug", "ShowBars", false, "Show the three gauges as bars (in play, the status effects tell).");
            LabEnabled = Config.Bind("Debug", "Lab", false, "Test hook: run the lines of plugins/Caca/lab/request.txt (console commands, camera renders).");
            PeeMinNeed = Config.Bind("Pee", "MinNeed", 50f, "Urge (%) from which the status effect shows and you can pee on command. At 100 % it starts on its own.");
            PeeFillPerMinute = Config.Bind("Pee", "FillPerMinute", 1.5f, "How fast the pee urge fills on its own (% per minute; 1.5 = full in about 1 h 07).");
            PeeDrainSeconds = Config.Bind("Pee", "DrainSeconds", 6f, "Seconds to go from 100 % to 0 % while peeing.");
            PeeMinSpeed = Config.Bind("Pee", "MinSpeed", 1.2f, "Stream speed (m/s) when almost empty.");
            PeeMaxSpeed = Config.Bind("Pee", "MaxSpeed", 6.5f, "Stream speed (m/s) at full urge.");
            PeeAimLift = Config.Bind("Pee", "AimLift", 12f, "Degrees added to the camera pitch (looking straight ahead gives an arc).");
            PeeVolume = Config.Bind("Pee", "Volume", 0.2f, "Stream sound volume (0-1).");
            Pose = Config.Bind("Pee", "Pose", PeePose.Auto, "Auto: standing for the male body, squatting for the female body. Standing / Squatting: always that one.");
            SquatPitch = Config.Bind("Pee", "SquatPitch", -62f, "Squatting: the stream's angle below the horizontal (degrees, negative = down).");
            SquatSpeed = Config.Bind("Pee", "SquatSpeed", 0.45f, "Squatting: stream speed compared to standing (0-1).");
            CacaVolume = Config.Bind("Urge", "Volume", 0.4f, "Volume of the poop and fart sounds: farts, plop, splat (0-1).");
            CacaVolume.SettingChanged += (s, e) => ApplyVolume();
            WetPatches = Config.Bind("Pee", "WetPatches", true, "Darker wet patches where the stream lands (they dry in a minute).");
            FartFillPerMinute = Config.Bind("Farts", "FillPerMinute", 3.03f, "How fast the fart gauge fills on its own (% per minute; 3.03 = every 33 minutes).");
            FartPerMeal = Config.Bind("Farts", "PerMeal", 8f, "Fart gauge (%) added by each meal.");
            FartCloud = Config.Bind("Farts", "Cloud", true, "A faint cloud of gas comes with each fart.");
            FartCloudOpacity = Config.Bind("Farts", "CloudOpacity", 0.16f, "How visible the gas cloud is (0-1).");
            TipOffset = Config.Bind("Tuning", "TipOffset", "0 -0.06 0.2", "Fallback only (no humanoid hand): where the stream leaves, right, up, forward (m) from the hips bone.");
            SquatTipOffset = Config.Bind("Tuning", "SquatTipOffset", "0 -0.1 0.05", "Squatting: where the stream leaves, right, up, forward (m) from the hips bone.");
            TipFromHand = Config.Bind("Tuning", "TipFromHand", 0.06f, "The stream leaves the right fist, this far (m) out along the aim.");
            WristOffset = Config.Bind("Tuning", "WristOffset", "0.09 -0.06 0.1", "Right wrist while peeing: right, up, forward (m) from the hips bone.");
            FingerDir = Config.Bind("Tuning", "FingerDir", "-0.7 -0.55 0.35", "Right fingers direction while peeing (right, up, forward).");
            FingerCurl = Config.Bind("Tuning", "FingerCurl", 55f, "How much the right fingers close while peeing (degrees per joint).");
            HeldPos = Config.Bind("Tuning", "HeldPos", "0 0 0.03", "Poop in the hand: position (m) in the hand's attach frame (z = along the grip).");
            HeldRot = Config.Bind("Tuning", "HeldRot", "0 0 0", "Poop in the hand: rotation (degrees) in the hand's attach frame.");
            _configStamp = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);

            AddLocalization();
            PrefabManager.OnVanillaPrefabsAvailable += Create;
            CommandManager.Instance.AddConsoleCommand(new CacaCommand());
            CommandManager.Instance.AddConsoleCommand(new NeedCommand());
            CommandManager.Instance.AddConsoleCommand(new ThrowCommand());
            CommandManager.Instance.AddConsoleCommand(new PeeCommand());
            CommandManager.Instance.AddConsoleCommand(new PeeNeedCommand());
            CommandManager.Instance.AddConsoleCommand(new HoldCommand());
            CommandManager.Instance.AddConsoleCommand(new FartCommand());
            CommandManager.Instance.AddConsoleCommand(new FartNeedCommand());
            new Harmony(Guid).PatchAll();
            Log.LogInfo(ModName + " " + Version + " loaded");
        }

        private static void AddLocalization()
        {
            var loc = LocalizationManager.Instance.GetLocalization();
            loc.AddTranslation("English", new Dictionary<string, string>
            {
                { "item_caca", "Poop" },
                { "item_caca_desc", "An elongated stone, still warm. You can pick it up... and throw it (it bursts on impact)." },
                { "hud_caca", "Poop" },
                { "msg_caca_notyet", "Not yet" },
                { "msg_caca_nothere", "Not here!" },
                { "msg_caca_urgent", "It's urgent!" },
                { "msg_caca_relief", "Ahhh, much better." },
                { "msg_pipi_notyet", "No need to pee" },
                { "msg_pipi_urgent", "Can't hold it any longer!" },
                { "msg_pipi_relief", "Phew." },
                { "se_caca", "Need to poop" },
                { "se_caca_urgent", "Need to poop, NOW" },
                { "se_caca_tip", "Find a quiet spot and squat." },
                { "se_pipi", "Need to pee" },
                { "se_pipi_urgent", "Bursting to pee" },
                { "se_pipi_tip", "Aim with the mouse, you can keep walking." },
            });
            loc.AddTranslation("French", new Dictionary<string, string>
            {
                { "item_caca", "Caca" },
                { "item_caca_desc", "Une pierre allongée, encore tiède. Se ramasse... et se lance (elle éclate à l'impact)." },
                { "hud_caca", "Caca" },
                { "msg_caca_notyet", "Pas encore envie" },
                { "msg_caca_nothere", "Pas ici !" },
                { "msg_caca_urgent", "Ça presse !" },
                { "msg_caca_relief", "Ahhh, ça va mieux." },
                { "msg_pipi_notyet", "Pas envie de faire pipi" },
                { "msg_pipi_urgent", "Je peux plus me retenir !" },
                { "msg_pipi_relief", "Ouf." },
                { "se_caca", "Envie de caca" },
                { "se_caca_urgent", "Envie de caca pressante" },
                { "se_caca_tip", "Trouve un coin tranquille et accroupis-toi." },
                { "se_pipi", "Envie de pipi" },
                { "se_pipi_urgent", "Envie de pipi pressante" },
                { "se_pipi_tip", "Vise avec la souris, tu peux continuer à marcher." },
            });
        }

        private void Update()
        {
            if (Time.unscaledTime > _nextConfigCheck)
            {
                _nextConfigCheck = Time.unscaledTime + 1f;
                var stamp = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
                if (stamp != _configStamp)
                {
                    _configStamp = stamp;
                    Config.Reload();
                }
            }
            var p = Player.m_localPlayer;
            if (p == null)
                return;
            RegisterRpc();
            if (EnablePoop.Value) Need.Tick(p);
            if (EnablePee.Value || Pee.Active) Pee.Tick(p);
            if (EnableFarts.Value) Fart.Tick(p);
            Urges.Tick(p);
            Hold.Apply(p);
            if (TypingFree())
            {
                if (EnablePoop.Value && Input.GetKeyDown(Key.Value)) Need.TryOnCommand(p);
                if (EnablePee.Value && Input.GetKeyDown(PeeKey.Value)) Pee.TryOnCommand(p);
            }
        }

        private void LateUpdate()
        {
            if (LabEnabled.Value) Lab.Poll();
        }

        private void OnGUI()
        {
            Need.DrawDebug();
        }

        private static bool TypingFree() =>
            !(Chat.instance != null && Chat.instance.HasFocus()) && !Console.IsVisible() && !Menu.IsVisible()
            && !InventoryGui.IsVisible() && !TextInput.IsVisible() && !Minimap.IsOpen() && !StoreGui.IsVisible();

        // ---------------------------------------------------------------- the fart and the plop, heard by everyone
        private static ZRoutedRpc s_rpcFor;          // a new ZRoutedRpc comes with each session

        private static void RegisterRpc()
        {
            if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == s_rpcFor) return;
            ZRoutedRpc.instance.Register<int, Vector3>("Caca_Sfx", (sender, which, pos) => PlayHere(which, pos));
            s_rpcFor = ZRoutedRpc.instance;
        }

        private static void PlayHere(int which, Vector3 pos)
        {
            var sfx = which == 1 ? s_plopSfx : s_fartSfx;
            if (sfx != null) Object.Instantiate(sfx, pos, Quaternion.identity);
            if (which >= 2)                                  // a gauge fart: the cloud drifts back (direction packed in `which`)
            {
                float yaw = (which - 2) * 2f;
                GasCloud.Spawn(pos, Quaternion.Euler(0f, yaw, 0f) * Vector3.forward);
            }
        }

        private static void Play(int which, Vector3 pos)
        {
            if (ZRoutedRpc.instance != null && ZRoutedRpc.instance == s_rpcFor)
                ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, "Caca_Sfx", which, pos);
            else
                PlayHere(which, pos);
        }

        public static void FartSound(Vector3 pos) => Play(0, pos);

        public static void Plop(Vector3 pos) => Play(1, pos);

        /// <summary>A fart with its gas cloud; `back` is where the cloud drifts (sent as a yaw in 2-degree steps).</summary>
        public static void GasFart(Vector3 pos, Vector3 back)
        {
            float yaw = Mathf.Repeat(Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg, 360f);
            Play(2 + Mathf.RoundToInt(yaw / 2f) % 180, pos);
        }

        /// <summary>[Urge] Volume, live: the sound prefabs' ZSFX pick their volume when spawned.</summary>
        private static void ApplyVolume()
        {
            foreach (var go in new[] { s_fartSfx, s_plopSfx, s_splatSfx })
            {
                var z = go != null ? go.GetComponent<ZSFX>() : null;
                if (z != null) z.m_minVol = z.m_maxVol = CacaVolume.Value;
            }
        }

        // ---------------------------------------------------------------- prefabs
        private void Create()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= Create;
            try
            {
                var bomb = PrefabManager.Instance.GetPrefab("BombOoze");
                if (bomb == null)
                {
                    Log.LogError("BombOoze not found: no poop item");
                    return;
                }
                var mesh = PoopArt.Mesh();
                var tex = PoopArt.Texture();
                var flat = PoopArt.FlatNormal();
                var sprite = Icons.Load("caca.png") ?? PoopArt.Icon();

                var item = PrefabManager.Instance.CreateClonedPrefab(ItemName, bomb);
                var drop = item.GetComponent<ItemDrop>();
                var shared = drop.m_itemData.m_shared;
                var oldProjectile = shared.m_attack.m_attackProjectile;
                var template = FindSfxTemplate(oldProjectile);

                // sounds
                s_fartSfx = MakeSfx("sfx_caca_fart", template, Wav.Load("fart"), 0.94f, 1.08f);
                s_plopSfx = MakeSfx("sfx_caca_plop", template, Wav.Load("plop"), 0.92f, 1.08f);
                var src0 = template != null ? template.GetComponentInChildren<AudioSource>(true) : null;
                SfxMixer = src0 != null ? src0.outputAudioMixerGroup : null;
                PeeGroundClip = Wav.Load("pee_ground").FirstOrDefault();
                PeeWaterClip = Wav.Load("pee_water").FirstOrDefault();
                var splatSfx = MakeSfx("sfx_caca_splat", template, Wav.Load("splat"), 0.9f, 1.1f);
                s_splatSfx = splatSfx;
                var splash = MakeSplash(oldProjectile);

                // the item
                shared.m_name = "$item_caca";
                shared.m_description = "$item_caca_desc";
                shared.m_icons = new[] { sprite };
                shared.m_maxStackSize = 20;
                shared.m_weight = 0.4f;
                shared.m_value = 0;
                shared.m_teleportable = true;
                shared.m_attackStatusEffect = null;
                drop.m_autoPickup = false;              // it lands right next to you: pick it up on purpose (E)
                Reskin(item, mesh, tex, flat);
                Hold.FixPrefab(item);

                // the throw: same arc as the ooze bomb, but it just bursts: no poison cloud, no damage
                if (oldProjectile != null)
                {
                    var proj = PrefabManager.Instance.CreateClonedPrefab("caca_projectile", oldProjectile);
                    var pr = proj.GetComponent<Projectile>();
                    if (pr != null)
                    {
                        pr.m_spawnOnHit = null;
                        pr.m_damage = new HitData.DamageTypes();
                        pr.m_aoe = 0f;
                        var fx = new List<EffectList.EffectData>();
                        if (splash != null) fx.Add(new EffectList.EffectData { m_prefab = splash, m_enabled = true });
                        if (splatSfx != null) fx.Add(new EffectList.EffectData { m_prefab = splatSfx, m_enabled = true });
                        pr.m_hitEffects = new EffectList { m_effectPrefabs = fx.ToArray() };
                        pr.m_hitWaterEffects = new EffectList { m_effectPrefabs = fx.ToArray() };
                    }
                    Reskin(proj, mesh, tex, flat);
                    PrefabManager.Instance.AddPrefab(new CustomPrefab(proj, true));
                    shared.m_attack.m_attackProjectile = proj;
                    shared.m_secondaryAttack.m_attackProjectile = proj;
                    ProjectilePrefab = proj;
                }
                ItemManager.Instance.AddItem(new CustomItem(item, false));
                Urges.Create();
                PoopPrefab = item;
                Log.LogInfo("Poop item ready (projectile " + (oldProjectile != null ? oldProjectile.name : "none") + ")");
            }
            catch (System.Exception e)
            {
                Log.LogError("Caca setup failed: " + e);
            }
        }

        /// <summary>Every mesh of the clone becomes the poop (scale reset), with a poop-brown material.</summary>
        private static void Reskin(GameObject go, Mesh mesh, Texture2D tex, Texture2D flat)
        {
            foreach (var smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                smr.enabled = false;
            foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
            {
                var mr = mf.GetComponent<MeshRenderer>();
                if (mr == null) continue;
                mf.sharedMesh = mesh;
                mf.transform.localScale = Vector3.one * (1f / Mathf.Max(0.001f, mf.transform.parent != null ? mf.transform.parent.lossyScale.x : 1f));
                mf.transform.localRotation = Quaternion.identity;
                var src = mr.sharedMaterial;
                var m = src != null ? new Material(src) : new Material(Shader.Find("Standard"));
                m.name = "caca_mat";
                if (m.HasProperty("_MainTex")) m.SetTexture("_MainTex", tex);
                if (m.HasProperty("_Color")) m.SetColor("_Color", Color.white);
                if (m.HasProperty("_BumpMap")) m.SetTexture("_BumpMap", flat);
                if (m.HasProperty("_MetallicGlossMap")) m.SetTexture("_MetallicGlossMap", null);
                if (m.HasProperty("_UseGlossmap")) m.SetFloat("_UseGlossmap", 0f);
                m.DisableKeyword("_USEGLOSSMAP_ON");
                if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.55f);     // a little wet
                if (m.HasProperty("_Metallic")) m.SetFloat("_Metallic", 0f);
                if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", Color.black);
                m.DisableKeyword("_EMISSION");
                mr.sharedMaterials = new[] { m };
            }
            foreach (var ps in go.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = ps.main;                                 // the bomb's green trail: poop brown instead
                main.startColor = new ParticleSystem.MinMaxGradient(PoopArt.DarkBrown, PoopArt.Brown);
            }
            foreach (var l in go.GetComponentsInChildren<Light>(true))
                l.enabled = false;
        }

        /// <summary>The burst: brown chunks flying out and falling, plus a short brown puff.</summary>
        private static GameObject MakeSplash(GameObject projectile)
        {
            Material mat = null;
            if (projectile != null)
                foreach (var r in projectile.GetComponentsInChildren<ParticleSystemRenderer>(true))
                    if (r.sharedMaterial != null) { mat = r.sharedMaterial; break; }
            var go = PrefabManager.Instance.CreateEmptyPrefab("vfx_caca_splat", false);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            Object.DestroyImmediate(go.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(go.GetComponent<MeshFilter>());
            void Burst(string name, int count, float speedMin, float speedMax, float sizeMin, float sizeMax, float life, float gravity, float alpha)
            {
                var child = new GameObject(name);
                child.transform.SetParent(go.transform, false);
                var ps = child.AddComponent<ParticleSystem>();
                var main = ps.main;
                main.duration = 0.2f;
                main.loop = false;
                main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
                main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
                main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
                var c0 = PoopArt.DarkBrown; c0.a = alpha;
                var c1 = PoopArt.Brown; c1.a = alpha;
                main.startColor = new ParticleSystem.MinMaxGradient(c0, c1);
                main.gravityModifier = gravity;
                main.simulationSpace = ParticleSystemSimulationSpace.World;
                main.playOnAwake = true;
                var em = ps.emission;
                em.rateOverTime = 0f;
                em.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });
                var sh = ps.shape;
                sh.shapeType = ParticleSystemShapeType.Hemisphere;
                sh.radius = 0.12f;
                var col = ps.colorOverLifetime;
                col.enabled = true;
                var g = new Gradient();
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                          new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
                col.color = g;
                var r = child.GetComponent<ParticleSystemRenderer>();
                if (mat != null) r.sharedMaterial = mat;
            }
            Burst("chunks", 45, 2.5f, 6f, 0.04f, 0.11f, 1.3f, 1.6f, 1f);
            Burst("spray", 30, 1f, 3.5f, 0.02f, 0.05f, 0.9f, 0.9f, 1f);
            Burst("puff", 6, 0.3f, 0.9f, 0.35f, 0.7f, 0.9f, -0.05f, 0.45f);
            go.AddComponent<TimedDestruction>().m_timeout = 3f;
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, false));
            return go;
        }

        private static GameObject FindSfxTemplate(GameObject projectile)
        {
            var lists = new List<EffectList>();
            var pr = projectile != null ? projectile.GetComponent<Projectile>() : null;
            if (pr != null) { lists.Add(pr.m_hitEffects); lists.Add(pr.m_hitWaterEffects); }
            if (pr != null && pr.m_spawnOnHit != null)
            {
                var aoe = pr.m_spawnOnHit.GetComponent<Aoe>();
                if (aoe != null) lists.Add(aoe.m_hitEffects);
            }
            foreach (var l in lists)
                if (l?.m_effectPrefabs != null)
                    foreach (var ed in l.m_effectPrefabs)
                        if (ed?.m_prefab != null && ed.m_prefab.GetComponent<ZSFX>() != null)
                            return ed.m_prefab;
            foreach (var n in new[] { "sfx_eat", "sfx_build_hammer_default", "sfx_pickable_pick" })
            {
                var p = PrefabManager.Instance.GetPrefab(n);
                if (p != null && p.GetComponent<ZSFX>() != null) return p;
            }
            return null;
        }

        private static GameObject MakeSfx(string name, GameObject template, AudioClip[] clips, float minPitch, float maxPitch)
        {
            if (template == null || clips.Length == 0)
            {
                Log.LogWarning(name + ": no " + (template == null ? "sound template" : "clips"));
                return null;
            }
            var go = PrefabManager.Instance.CreateClonedPrefab(name, template);
            var sfx = go.GetComponent<ZSFX>();
            sfx.m_audioClips = clips;
            sfx.m_minPitch = minPitch;
            sfx.m_maxPitch = maxPitch;
            sfx.m_minVol = sfx.m_maxVol = CacaVolume.Value;
            sfx.m_closedCaptionToken = "";
            sfx.m_secondaryCaptionToken = "";
            var src = go.GetComponent<AudioSource>();
            if (src != null) { src.volume = 1f; src.maxDistance = Mathf.Max(src.maxDistance, 30f); }
            PrefabManager.Instance.AddPrefab(new CustomPrefab(go, true));
            return go;
        }
    }

    internal class CacaCommand : ConsoleCommand
    {
        public override string Name => "caca";
        public override string Help => "caca [force] - go now (force: whatever the urge)";

        public override void Run(string[] args)
        {
            var p = Player.m_localPlayer;
            if (p != null) Need.TryOnCommand(p, args.Length > 0 && args[0] == "force");
        }
    }

    /// <summary>Test: throws a poop from the player's eyes, exactly like the item's attack, without the controls.</summary>
    internal class ThrowCommand : ConsoleCommand
    {
        public override string Name => "caca_throw";
        public override string Help => "caca_throw [speed=14] - throw a poop forward (tests)";

        public static float Arg(string[] args, float fallback) =>
            args.Length > 0 && float.TryParse(args[0], System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fallback;

        public override void Run(string[] args)
        {
            var p = Player.m_localPlayer;
            if (p == null || Plugin.ProjectilePrefab == null) return;
            float speed = Arg(args, 14f);
            var dir = (p.transform.forward + Vector3.up * 0.35f).normalized;
            var pos = p.GetEyePoint() + p.transform.forward * 0.6f;
            var go = Object.Instantiate(Plugin.ProjectilePrefab, pos, Quaternion.LookRotation(dir));
            var item = Plugin.PoopPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
            go.GetComponent<Projectile>()?.Setup(p, dir * speed, 10f, new HitData(), item, null);
        }
    }

    internal class PeeCommand : ConsoleCommand
    {
        public override string Name => "pipi";
        public override string Help => "pipi [force] - pee now (force: whatever the urge); again to stop";

        public override void Run(string[] args)
        {
            var p = Player.m_localPlayer;
            if (p != null) Pee.TryOnCommand(p, args.Length > 0 && args[0] == "force");
        }
    }

    internal class PeeNeedCommand : ConsoleCommand
    {
        public override string Name => "pipi_need";
        public override string Help => "pipi_need <0-100> - set the pee urge (tests)";

        public override void Run(string[] args)
        {
            if (Player.m_localPlayer == null || args.Length < 1) return;
            Pee.Value = Mathf.Clamp(ThrowCommand.Arg(args, Pee.Value), 0f, 100f);
            Pee.Save();
            Console.instance.Print("pee urge " + Pee.Value);
        }
    }

    internal class FartCommand : ConsoleCommand
    {
        public override string Name => "pet";
        public override string Help => "pet - fart now (pet <0-100> also sets the gauge, like pet_need)";

        public override void Run(string[] args)
        {
            var p = Player.m_localPlayer;
            if (p == null) return;
            if (args.Length == 0) Fart.Let(p);
            else Fart.Value = Mathf.Clamp(ThrowCommand.Arg(args, Fart.Value), 0f, 100f);
        }
    }

    internal class FartNeedCommand : ConsoleCommand
    {
        public override string Name => "pet_need";
        public override string Help => "pet_need <0-100> - set the fart gauge (tests)";

        public override void Run(string[] args)
        {
            if (Player.m_localPlayer == null || args.Length < 1) return;
            Fart.Value = Mathf.Clamp(ThrowCommand.Arg(args, Fart.Value), 0f, 100f);
            Fart.Save();
            Console.instance.Print("fart gauge " + Fart.Value);
        }
    }

    internal class NeedCommand : ConsoleCommand
    {
        public override string Name => "caca_need";
        public override string Help => "caca_need <0-100> - set the urge (tests)";

        public override void Run(string[] args)
        {
            if (Player.m_localPlayer == null || args.Length < 1) return;
            Need.Value = Mathf.Clamp(ThrowCommand.Arg(args, Need.Value), 0f, 100f);
            Need.Pending = 0f;
            Need.Save();
            Console.instance.Print("urge " + Need.Value);
        }
    }
}
