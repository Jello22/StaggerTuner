// Refs: BepInEx, 0Harmony, assembly_valheim, UnityEngine.CoreModule
using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace VH.StaggerTuner
{
    [BepInPlugin("vh.staggertuner", "Stagger Tuner", "0.6.0")]
    public class StaggerTunerPlugin : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        internal static Harmony H;

        internal static ConfigEntry<bool> CEnabled;
        internal static ConfigEntry<float> CMultiplier;

        // Minimal attributes so entries look good in BepInEx Config Manager (safe if not installed)
        [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
        private sealed class ConfigurationManagerAttributes : Attribute
        {
            public bool? Browsable;
            public string Category;
            public string Description;
            public bool? IsAdvanced;
            public int? Order;
            public bool? ShowRangeAsPercent;
            public string DispName;
        }

        private void Awake()
        {
            Log = Logger;

            CEnabled = Config.Bind(
                "General",
                "Enabled",
                true,
                new ConfigDescription(
                    "Enable stagger scaling (affects guard-break frequency, not HP damage).",
                    null,
                    new ConfigurationManagerAttributes { Order = 1, Description = "Toggle patch on/off at runtime." }
                )
            );

            CMultiplier = Config.Bind(
                "General",
                "Multiplier",
                1.60f,
                new ConfigDescription(
                    "Multiplier applied to how fast the stagger bar fills. Effective bar behaves like 0.40 * Multiplier of max HP.",
                    new AcceptableValueRange<float>(0.50f, 3.00f),
                    new ConfigurationManagerAttributes { Order = 0, Description = "1.50≈60% bar; 1.75≈70%; 2.00≈80%." }
                )
            );

            H = new Harmony("vh.staggertuner");
            H.PatchAll();
            Log.LogInfo("[StaggerTuner] Loaded.");
        }

        private void OnDestroy() => H?.UnpatchSelf();

        internal static bool IsEnabled() => CEnabled?.Value ?? true;
        internal static float GetDivisor() => Mathf.Clamp(CMultiplier?.Value ?? 1.60f, 0.50f, 3.00f);

        // --- Patch the Character.AddStaggerDamage(float, Vector3, HitData) ---
        [HarmonyPatch(typeof(Character))]
        static class Patch_AddStaggerDamage
        {
            static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(Character), "AddStaggerDamage",
                    new[] { typeof(float), typeof(Vector3), typeof(HitData) });

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
