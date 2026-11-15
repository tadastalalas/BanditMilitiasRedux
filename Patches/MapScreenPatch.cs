using BanditMilitiasRedux.Core;
using HarmonyLib;
using SandBox.View.Map;
using static BanditMilitiasRedux.Core.Globals;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: One-time mod state init (hideouts, villages, troops, templates, raid cap) | gate: always | Postfix: the map screen is the first point where the campaign world is ready
[HarmonyPatch(typeof(MapScreen), "OnInitialize")]
internal class MapScreenPatch
{
    internal static void Postfix()
    {
        if (IsGlobalModStateInitialized)
            return;

        Helper.InitializeGlobalModState();
    }
}
