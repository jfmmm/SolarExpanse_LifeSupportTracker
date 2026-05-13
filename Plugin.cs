#nullable disable
using BepInEx;
using HarmonyLib;
using LifeSupportTracker.UI;

namespace LifeSupportTracker
{
    [BepInPlugin("com.mod.solarexpanse.lifesupporttracker", "LifeSupportTracker", "1.0.0")]
    public class Plugin : BaseUnityPlugin
    {
        internal static SupplyTrackerConfig TrackerConfig;

        private void Awake()
        {
            TrackerConfig = new SupplyTrackerConfig(Config);
            new Harmony("com.mod.solarexpanse.lifesupporttracker").PatchAll();
            Logger.LogInfo("LifeSupportTracker loaded");
        }
    }
}
