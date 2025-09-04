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
        public const string Version = "0.8.0";

        internal static ManualLogSource Log;
        internal static Harmony H;

        // Local config
        internal static ConfigEntry<bool> CEnabled;
        internal static ConfigEntry<float> CThresholdMultiplier;
        internal static ConfigEntry<float> CIncomingStaggerMultiplier; // optional, default 1.0 = off

        // ServerSync
        internal static ConfigSync ConfigSync;
        internal static SyncedConfigEntry<bool> SS_Enabled;
        internal static SyncedConfigEntry<float> SS_ThresholdMultiplier;
        internal static SyncedConfigEntry<float> SS_IncomingStaggerMultiplier;
        internal static SyncedConfigEntry<bool> SS_LockConfig;

        // Optional: nicer display in BepInEx Config Manager
        [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
        private sealed class ConfigurationManagerAttributes : Attribute
        {
            public string Description;
            public int? Order;
        }

        private void Awake()
        {
            Log = Logger;

            // ServerSync
            ConfigSync = new ConfigSync(ModGUID)
            {
                DisplayName = ModName,
                CurrentVersion = Version,
                MinimumRequiredVersion = Version,
                ModRequired = true
            };

            // Config (local)
            CEnabled = Config.Bind(
                "General", "Enabled", true,
                new ConfigDescription("Enable stagger tuning (player-only).",
                    null, new ConfigurationManagerAttributes { Order = 3, Description = "Server can lock & sync this value." }));

            CThresholdMultiplier = Config.Bind(
                "Tuning", "StaggerThresholdMultiplier", 1.60f,
                new ConfigDescription("Multiplier for player stagger threshold (acts like a bigger stagger bar). 1.0 = vanilla.",
                    new AcceptableValueRange<float>(0.50f, 3.00f),
                    new ConfigurationManagerAttributes { Order = 2, Description = "1.6 ≈ +60% bar; 2.0 ≈ +100% bar." }));

            // Optional cushion (per-hit). Default 1.0 = no change. Leave at 1.0 for best perf.
            CIncomingStaggerMultiplier = Config.Bind(
                "Tuning (Advanced)", "IncomingStaggerMultiplier", 1.00f,
                new ConfigDescription("Scales stagger added to players per hit. 1.0 = vanilla. <1 reduces, >1 increases.",
                    new AcceptableValueRange<float>(0.50f, 1.50f),
                    new ConfigurationManagerAttributes { Order = 1, Description = "Leave at 1.0 for best performance/consistency." }));

            // ServerSync lock toggle
            var lockCfgLocal = Config.Bind("Server Sync", "Lock Configuration", true,
                new ConfigDescription("If on (server-side), configuration is locked and synced to all clients."));
            SS_LockConfig = ConfigSync.AddLockingConfigEntry(lockCfgLocal);

            // Register synced entries
            SS_Enabled = ConfigSync.AddConfigEntry(CEnabled); SS_Enabled.SynchronizedConfig = true;
            SS_ThresholdMultiplier = ConfigSync.AddConfigEntry(CThresholdMultiplier); SS_ThresholdMultiplier.SynchronizedConfig = true;
            SS_IncomingStaggerMultiplier = ConfigSync.AddConfigEntry(CIncomingStaggerMultiplier); SS_IncomingStaggerMultiplier.SynchronizedConfig = true;

            // Patch
            H = new Harmony(ModGUID);
            H.PatchAll();
            // Bind optional per-hit patch only if it will actually change anything
            if (GetIncomingMult() != 1.0f && IsEnabled())
                AddStaggerDamageBinder.BindAndPatch(H);

            Log.LogInfo($"[{ModName}] Loaded {Version} (ServerSync enabled).");
        }

        private void OnDestroy() => H?.UnpatchSelf();

        // Helpers reading possibly-synced values
        internal static bool IsEnabled() => SS_Enabled?.Value ?? CEnabled?.Value ?? true;
        internal static float GetThreshMult()
        {
            var x = SS_ThresholdMultiplier?.Value ?? CThresholdMultiplier?.Value ?? 1.60f;
            return Mathf.Clamp(x, 0.50f, 3.00f);
        }
        internal static float GetIncomingMult()
        {
            var x = SS_IncomingStaggerMultiplier?.Value ?? CIncomingStaggerMultiplier?.Value ?? 1.00f;
            return Mathf.Clamp(x, 0.50f, 1.50f);
        }

        //Stagger Threshold scaling should work in live and PTB
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
                    Log.LogWarning($"[StaggerTuner] GetStaggerTreshold postfix failed: {e}");
                }
            }
        }

        // Optional: reduce incoming stagger per hit (runtime-overload binding; off by default)
        internal static class AddStaggerDamageBinder
        {
            public static void BindAndPatch(Harmony h)
            {
                try
                {
                    var t = typeof(Character);
                    // PTB: (float, Vector3, HitData)
                    var m = AccessTools.Method(t, "AddStaggerDamage",
                        new[] { typeof(float), typeof(Vector3), typeof(HitData) });
                    string postfix = null;

                    if (m != null)
                    {
                        h.Patch(m, prefix: new HarmonyMethod(typeof(AddStaggerDamageBinder), nameof(Prefix3)));
                        Log.LogInfo("[StaggerTuner] Patched Character.AddStaggerDamage(float, Vector3, HitData) for incoming scaling.");
                        return;
                    }

                    // Live: (float, Vector3)
                    m = AccessTools.Method(t, "AddStaggerDamage",
                        new[] { typeof(float), typeof(Vector3) });

                    if (m != null)
                    {
                        h.Patch(m, prefix: new HarmonyMethod(typeof(AddStaggerDamageBinder), nameof(Prefix2)));
                        Log.LogInfo("[StaggerTuner] Patched Character.AddStaggerDamage(float, Vector3) for incoming scaling.");
                        return;
                    }

                    Log.LogWarning("[StaggerTuner] AddStaggerDamage overload not found; skipping incoming scaling.");
                }
                catch (Exception e)
                {
                    Log.LogWarning($"[StaggerTuner] Failed to bind AddStaggerDamage: {e}");
                }
            }

            // PTB
            private static void Prefix3(Character __instance, ref float damage, Vector3 forceDirection, HitData hit)
            {
                TryScaleIncoming(__instance, ref damage);
            }
            // Live
            private static void Prefix2(Character __instance, ref float damage, Vector3 forceDirection)
            {
                TryScaleIncoming(__instance, ref damage);
            }

            private static void TryScaleIncoming(Character ch, ref float damage)
            {
                if (!IsEnabled()) return;
                if (!ch.IsPlayer()) return;

                float m = GetIncomingMult();
                if (Mathf.Approximately(m, 1f)) return; // no-op if default
                damage *= m; // m < 1.0 reduces stagger per hit
            }
        }
    }
}
