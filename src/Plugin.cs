using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using Jotunn.Managers;
using Jotunn.Utils;
using UnityEngine;

namespace Wildlife
{
    /// <summary>
    /// Wildlife: new fauna for Valheim and the loot and recipes that come with it. Meadow rabbits, foxes, field
    /// mice, sparrows and owls (plus vanilla crows in the Black Forest), each with its own look, voice and
    /// behaviour; hides, meats, a rug, boots and a cape. Dev tools: ta_show / ta_clear, ta_lab (model and
    /// animation sheets), ta_overlay (creatures through walls).
    /// </summary>
    [BepInPlugin(Guid, "Wildlife", Version)]
    [BepInDependency(Jotunn.Main.ModGuid)]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.Minor)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "lekinox.wildlife";
        public const string Version = "0.21.0";

        internal static BepInEx.Logging.ManualLogSource Log;
        internal static Plugin Instance;
        internal static ConfigEntry<bool> PerfLog;

        private System.DateTime _configStamp;
        private float _nextConfigCheck;
        private float _nextLabPoll;

        private void Awake()
        {
            Log = Logger;
            Instance = this;
            Look.PluginDir = System.IO.Path.GetDirectoryName(Info.Location);
            PerfLog = Config.Bind("Debug", "PerfLog", true, "Log frame rate and this mod's hook timings every 10 s (live).");
            Rabbits.BindConfig(Config);
            Birds.BindConfig(Config);
            Foxes.BindConfig(Config);
            Mice.BindConfig(Config);
            Sea.BindConfig(Config);
            Spawns.BindConfig(Config);
            Interactions.BindConfig(Config);
            _configStamp = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);

            var loc = LocalizationManager.Instance.GetLocalization();
            Rabbits.AddTranslations(loc);
            Birds.AddTranslations(loc);
            Foxes.AddTranslations(loc);
            Mice.AddTranslations(loc);
            Sea.AddTranslations(loc);

            PrefabManager.OnVanillaPrefabsAvailable += RegisterAll;
            new Harmony(Guid).PatchAll();
            CommandManager.Instance.AddConsoleCommand(new ShowCommand());
            CommandManager.Instance.AddConsoleCommand(new ClearCommand());
            CommandManager.Instance.AddConsoleCommand(new LabCommand());
            CommandManager.Instance.AddConsoleCommand(new OverlayCommand());
            CommandManager.Instance.AddConsoleCommand(new SeaCommand());
            Log.LogInfo("Wildlife " + Version + " loaded");
        }

        // Edits to the .cfg apply while the game runs (checked once a second).
        private void Update()
        {
            Perf.Frame();
            long t = Perf.Begin();
            try { CheckConfig(); }
            finally { Perf.End("Plugin.Update (config check)", t); }
            t = Perf.Begin();
            try { Spawns.Tick(); }
            finally { Perf.End("Spawns.Tick", t); }
        }

        private void CheckConfig()
        {
            if (Time.unscaledTime < _nextConfigCheck)
                return;
            _nextConfigCheck = Time.unscaledTime + 1f;
            var stamp = System.IO.File.GetLastWriteTimeUtc(Config.ConfigFilePath);
            if (stamp == _configStamp)
                return;
            _configStamp = stamp;
            Config.Reload();
            Log.LogInfo("Config reloaded");
        }

        // Model lab requests (lab/request.txt), checked once a second, even from the main menu.
        private void LateUpdate()
        {
            if (Time.unscaledTime < _nextLabPoll || Look.PluginDir == null)
                return;
            _nextLabPoll = Time.unscaledTime + 1f;
            Lab.Poll();
        }

        private void RegisterAll()
        {
            PrefabManager.OnVanillaPrefabsAvailable -= RegisterAll;
            Try("Rabbits", Rabbits.Register);
            Try("Foxes", Foxes.Register);
            Try("Mice", Mice.Register);
            Try("Birds", Birds.Register);
            Try("Sea", Sea.Register);
            Try("Fish", Fishes.Register);
        }

        private static void Try(string what, System.Action register)
        {
            try
            {
                register();
            }
            catch (System.Exception e)
            {
                Log.LogError(what + " failed to register: " + e);
            }
        }
    }
}
