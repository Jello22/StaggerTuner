using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;

namespace VH.StaggerTuner
{
    [BepInPlugin(ModGUID, ModName, Version)]
    public class StaggerTunerPlugin : BaseUnityPlugin
    {
        public const string ModGUID = "vh.staggertuner";
        public const string ModName = "Stagger Tuner";
        public const string Version = "1.0.0";

        internal static ManualLogSource Log = null!;
        internal static Harmony H = null!;

        // Config
        internal static ConfigEntry<float> CThresholdMultiplier = null!;

        // ServerSync
        internal static ConfigSync ConfigSync = null!;

        // Configuration Manager niceties
        [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
        private sealed class ConfigurationManagerAttributes : Attribute
        {
            public string? Description;
            public int? Order;
        }

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

            // Tuning
            CThresholdMultiplier = BindConfig(
                "Tuning",
                "StaggerThresholdMultiplier",
                1.60f,
                new ConfigDescription(
                    "Multiplier for player stagger threshold (acts like a bigger stagger bar). 1.0 = vanilla.",
                    new AcceptableValueRange<float>(0.50f, 3.00f),
                    new ConfigurationManagerAttributes
                    {
                        Order = 1,
                        Description = "1.0 = Vanilla; 1.6 ≈ +60% bar; 2.0 ≈ +100% bar. [Synced with Server]"
                    }
                )
            );

            // ServerSync lock
            ConfigEntry<bool> lockConfig = base.Config.Bind(
                "General",
                "Lock Configuration",
                true,
                new ConfigDescription(
                    "If enabled, configuration is locked and can only be changed by server administrators. [Synced with Server]"
                )
            );

            ConfigSync.AddLockingConfigEntry(lockConfig);

            // Harmony
            H = new Harmony(ModGUID);
            H.PatchAll();

            Log.LogInfo($"[{ModName}] Loaded {Version} (threshold-only; ServerSync enabled).");
        }

        /// <summary>
        /// Bind a BepInEx config entry and register it with ServerSync.
        /// StaggerTuner gameplay settings are synchronized by default.
        /// </summary>
        private ConfigEntry<T> BindConfig<T>(
            string group,
            string name,
            T value,
            ConfigDescription description,
            bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry =
                base.Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry =
                ConfigSync.AddConfigEntry(configEntry);

            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        [HarmonyPatch(typeof(Character), nameof(Character.GetStaggerTreshold))]
        private static class Patch_GetStaggerTreshold_Postfix
        {
            private static void Postfix(Character __instance, ref float __result)
            {
                try
                {
                    
                    if (!__instance.IsPlayer())
                        return;

                    __result *= CThresholdMultiplier.Value;
                }
                catch (Exception e)
                {
                    Log.LogWarning(
                        $"[{ModName}] GetStaggerTreshold postfix failed: {e}"
                    );
                }
            }
        }
    }
}