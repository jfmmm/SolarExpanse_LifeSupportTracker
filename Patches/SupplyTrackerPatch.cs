using BepInEx.Logging;
using HarmonyLib;
using LifeSupportTracker.UI;
using Manager;

namespace LifeSupportTracker.Patches
{
    [HarmonyPatch(typeof(NotificationManager), "Awake")]
    internal static class SupplyTrackerPatch
    {
        internal static ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("LifeSupportTracker");

        [HarmonyPostfix]
        static void Postfix(NotificationManager __instance)
        {
            Log.LogInfo("[LifeSupportTracker] NotificationManager.Awake postfix — injecting");
            SupplyTrackerInjector.Inject(__instance, Log, Plugin.TrackerConfig);
        }
    }
}
