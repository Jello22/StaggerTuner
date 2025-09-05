using System;
using System.IO;
using System.Timers;
using System.Collections;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace VH.StaggerTuner
{
    [BepInPlugin(ModGUID, ModName, Version)]
    public class StaggerTunerPlugin : BaseUnityPlugin
    {
        public const string ModGUID = "vh.staggertuner";
        public const string ModName = "Stagger Tuner";
        public const string Version = "0.8.3";

        internal static ManualLogSource Log;
        internal static Harmony H;

        // Local config
        // Z is a bird murderer!!
        internal static ConfigEntry<bool> CEnabled;
        internal static ConfigEntry<float> CThresholdMultiplier;

        // ServerSync
        internal static ConfigSync ConfigSync;
        internal static SyncedConfigEntry<bool> SS_Enabled;
        internal static SyncedConfigEntry<float> SS_ThresholdMultiplier;
        internal static SyncedConfigEntry<bool> SS_LockConfig;

        // Config UI niceties 
        [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
        private sealed class ConfigurationManagerAttributes : Attribute
        {
            public string Description;
            public int? Order;
        }

        // Hot-reload watcher (server-side)
        private FileSystemWatcher _cfgWatcher;
        private Timer _debounce;
        private string _cfgPath;

        private void Awake()
        {
            Log = Logger;

            // ServerSync bootstrap
            ConfigSync = new ConfigSync(ModGUID)
            {
                DisplayName = ModName,
                CurrentVersion = Version,
                MinimumRequiredVersion = Version,
                ModRequired = true
            };

            // Config
            CEnabled = Config.Bind(
                "General", "Enabled", true,
                new ConfigDescription(
                    "Enable stagger tuning (player-only).",
                    null,
                    new ConfigurationManagerAttributes { Order = 2, Description = "Server can lock & sync this value." }
                )
            );

            CThresholdMultiplier = Config.Bind(
                "Tuning", "StaggerThresholdMultiplier", 1.60f,
                new ConfigDescription(
                    "Multiplier for player stagger threshold (acts like a bigger stagger bar). 1.0 = vanilla.",
                    new AcceptableValueRange<float>(0.50f, 3.00f),
                    new ConfigurationManagerAttributes { Order = 1, Description = "1.6 ≈ +60% bar; 2.0 ≈ +100% bar." }
                )
            );

            // Server lock toggle - server auth values
            var lockCfgLocal = Config.Bind(
                "Server Sync", "Lock Configuration", true,
                new ConfigDescription("If on (server-side), configuration is locked and synced to all clients.")
            );
            SS_LockConfig = ConfigSync.AddLockingConfigEntry(lockCfgLocal);

            // Register synchronized entries
            SS_Enabled = ConfigSync.AddConfigEntry(CEnabled); SS_Enabled.SynchronizedConfig = true;
            SS_ThresholdMultiplier = ConfigSync.AddConfigEntry(CThresholdMultiplier); SS_ThresholdMultiplier.SynchronizedConfig = true;

            // Harmony patching
            H = new Harmony(ModGUID);
            H.PatchAll();

            // Start server-side config watcher 
            StartFileWatcher();

            Log.LogInfo("[" + ModName + "] Loaded " + Version + " (threshold-only; ServerSync enabled).");
        }

        private void OnDestroy()
        {
            try { if (H != null) H.UnpatchSelf(); } catch (Exception) { /* ignore */ }
            try
            {
                if (_cfgWatcher != null)
                {
                    _cfgWatcher.EnableRaisingEvents = false;
                    _cfgWatcher.Dispose();
                }
                if (_debounce != null) _debounce.Dispose();
            }
            catch (Exception) { /* ignore */ }
        }

        // Helper accessors 
        internal static bool IsEnabled()
        {
            if (SS_Enabled != null) return SS_Enabled.Value;
            if (CEnabled != null) return CEnabled.Value;
            return true;
        }

        internal static float GetThreshMult()
        {
            float x = 1.60f;
            if (SS_ThresholdMultiplier != null) x = SS_ThresholdMultiplier.Value;
            else if (CThresholdMultiplier != null) x = CThresholdMultiplier.Value;

            if (x < 0.50f) x = 0.50f;
            else if (x > 3.00f) x = 3.00f;
            return x;
        }

        // Primary patch: scale player stagger threshold (works on live + PTB) 
        [HarmonyPatch(typeof(Character), "GetStaggerTreshold")]
        private static class Patch_GetStaggerTreshold_Postfix
        {
            private static void Postfix(Character __instance, ref float __result)
            {
                try
                {
                    if (!IsEnabled()) return;
                    if (!__instance.IsPlayer()) return;
                    __result *= GetThreshMult();
                }
                catch (Exception e)
                {
                    Log.LogWarning("[StaggerTuner] GetStaggerTreshold postfix failed: " + e);
                }
            }
        }

       
        // File watcher - server
    
        private void StartFileWatcher()
        {
            try
            {
                _cfgPath = Config.ConfigFilePath;
                if (string.IsNullOrEmpty(_cfgPath) || !File.Exists(_cfgPath))
                {
                    Log.LogWarning("[StaggerTuner] Config file not found for watcher; hot-reload disabled.");
                    return;
                }

                string dir = Path.GetDirectoryName(_cfgPath);
                string file = Path.GetFileName(_cfgPath);
                if (string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(file))
                {
                    Log.LogWarning("[StaggerTuner] Invalid config path; hot-reload disabled.");
                    return;
                }

                _debounce = new Timer(600.0);
                _debounce.AutoReset = false;
                _debounce.Elapsed += (s, e) => UnityMainThread(SoftReloadAndResync);

                _cfgWatcher = new FileSystemWatcher(dir, file);
                _cfgWatcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.CreationTime;
                _cfgWatcher.IncludeSubdirectories = false;

                FileSystemEventHandler kick = (s, e) =>
                {
                    try { if (_debounce != null) _debounce.Start(); } catch (Exception) { }
                };
                RenamedEventHandler kickRename = (s, e) =>
                {
                    try { if (_debounce != null) _debounce.Start(); } catch (Exception) { }
                };

                _cfgWatcher.Changed += kick;
                _cfgWatcher.Created += kick;
                _cfgWatcher.Renamed += kickRename;

                _cfgWatcher.EnableRaisingEvents = true;

                Log.LogInfo("[StaggerTuner] Config file watcher armed for hot-reload.");
            }
            catch (Exception e)
            {
                Log.LogWarning("[StaggerTuner] File watcher init failed: " + e);
            }
        }

        private void SoftReloadAndResync()
        {
            try
            {
                // Server only; avoid loops during remote applies
                if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
                if (ConfigSync == null) return;

                // Skip if we're not the authority, or if ServerSync is currently applying values
                if (!ConfigSync.IsSourceOfTruth) return;
                if (ConfigSync.ProcessingServerUpdate) return;

                Log.LogInfo("[StaggerTuner] Detected server config change; reloading (no version bump).");

                // Loads new values from file; raises SettingChanged; ServerSync will propagate to clients
                Config.Reload();

                Log.LogInfo("[StaggerTuner] Reloaded config and signaled ServerSync to propagate values.");
            }
            catch (Exception e)
            {
                Log.LogWarning("[StaggerTuner] Soft reload failed: " + e);
            }
        }

        // Run an action on main thread next frame
        private void UnityMainThread(Action action)
        {
            StartCoroutine(CoNextFrame(action));
        }

        private IEnumerator CoNextFrame(Action action)
        {
            yield return null;
            try { if (action != null) action(); } catch (Exception e) { Log.LogWarning("[StaggerTuner] Hot-reload error: " + e); }
        }
    }
}
