using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using ServerSync;

namespace VH.StaggerTuner
{
    [BepInPlugin(ModGUID, ModName, Version)]
    public class StaggerTunerPlugin : BaseUnityPlugin
    {
        public const string ModGUID = "vh.staggertuner";
        public const string ModName = "Stagger Tuner";
        public const string Version = "0.7.0";

        internal static ManualLogSource Log;
        internal static Harmony H;

        // Local config entries
        internal static ConfigEntry<bool> CEnabled;
        internal static ConfigEntry<float> CMultiplier;

        // ServerSync
        internal static ConfigSync ConfigSync;
        internal static SyncedConfigEntry<bool> SS_Enabled;
        internal static SyncedConfigEntry<float> SS_Multiplier;
        internal static SyncedConfigEntry<bool> SS_LockConfig;

        // Minimal attributes so entries look nice in BepInEx Config Manager (safe if not installed)
        [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
        private sealed class ConfigurationManagerAttributes : Attribute
        {
            public string Description;
            public int? Order;
        }

        private void Awake()
        {
            Log = Logger;

            // 1) ServerSync bootstrap
            ConfigSync = new ConfigSync(ModGUID)
            {
                DisplayName = ModName,
                CurrentVersion = Version,
                MinimumRequiredVersion = Version,
                ModRequired = true   // require clients to have the mod so behavior matches
            };

            // 2) Config entries (local definitions)
            CEnabled = Config.Bind(
                "General",
                "Enabled",
                true,
                new ConfigDescription(
                    "Enable stagger scaling (affects guard-break frequency, not HP damage).",
                    null,
                    new ConfigurationManagerAttributes { Order = 2, Description = "Server can lock & sync this value." }
                )
            );

            CMultiplier = Config.Bind(
                "General",
                "Multiplier",
                1.60f,
                new ConfigDescription(
                    "Scales how fast the stagger bar fills. Effective bar behaves like 0.40 * Multiplier of max HP.",
                    new AcceptableValueRange<float>(0.50f, 3.00f),
                    new ConfigurationManagerAttributes { Order = 1, Description = "1.50≈60% bar; 1.75≈70%; 2.00≈80%. Server-synced." }
                )
            );

            // 3) ServerSync: add a locking entry visible under a "Server Sync" section
            var lockCfgLocal = Config.Bind(
                "Server Sync",
                "Lock Configuration",
                true,
                new ConfigDescription("If on (server-side), configuration is locked and synced to all clients.")
            );
            SS_LockConfig = ConfigSync.AddLockingConfigEntry(lockCfgLocal); // special lock entry

            // 4) ServerSync: register synchronized entries
            SS_Enabled = ConfigSync.AddConfigEntry(CEnabled);
            SS_Multiplier = ConfigSync.AddConfigEntry(CMultiplier);

            // Mark them as synchronized so the server’s values propagate to clients when locked
            SS_Enabled.SynchronizedConfig = true;
            SS_Multiplier.SynchronizedConfig = true;

            // 5) Patching
            H = new Harmony(ModGUID);
            H.PatchAll();
            Log.LogInfo($"[{ModName}] Loaded {Version} (ServerSync enabled).");
        }

        private void OnDestroy() => H?.UnpatchSelf();

        // Helpers read the possibly-synced (server-enforced) values:
        internal static bool IsEnabled() => SS_Enabled?.Value ?? CEnabled?.Value ?? true;
        internal static float GetDivisor()
        {
            float x = SS_Multiplier?.Value ?? CMultiplier?.Value ?? 1.60f;
            return Mathf.Clamp(x, 0.50f, 3.00f);
        }

        // --- Patch the Character.AddStaggerDamage(float, Vector3, HitData) exactly as in your IL ---
        [HarmonyPatch(typeof(Character))]
        static class Patch_AddStaggerDamage
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(Character), "AddStaggerDamage",
                    new[] { typeof(float), typeof(Vector3), typeof(HitData) });

            // Scale incoming stagger for PLAYERS, so the bar fills slower (equivalent to bigger bar)
            static void Prefix(Character __instance, ref float damage)
            {
                if (!(__instance is Player)) return;
                if (!IsEnabled()) return;

                float div = GetDivisor();
                if (div <= 0.01f) return;

                damage /= div;
            }
        }
    }
}
