#nullable disable
using BepInEx;
using HarmonyLib;
using LifeSupportTracker.UI;

namespace LifeSupportTracker
{
    [BepInPlugin("com.mod.solarexpanse.lifesupporttracker", "LifeSupportTracker", "1.4.2")]
    public class Plugin : BaseUnityPlugin
    {
        internal static SupplyTrackerConfig TrackerConfig;

        private void Awake()
        {
            TrackerConfig = new SupplyTrackerConfig(Config);
            var harmony = new Harmony("com.mod.solarexpanse.lifesupporttracker");
            harmony.PatchAll();
            Patches.PauseScreenEscPatch.Apply(harmony, Logger);
            Logger.LogInfo("LifeSupportTracker loaded");
        }
    }
}
