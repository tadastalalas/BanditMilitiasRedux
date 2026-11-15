#if LOWER_THAN_1_5
// v1.4.7 / v1.4.8 only. v1.5.7 removed MapTrackerProvider; see MapTrackerManagerPatch.
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using SandBox.ViewModelCollection.Map.Tracker;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia map markers | gate: always | Postfix: remember the map's tracker provider so militia markers can be refreshed
[HarmonyPatch(typeof(MapTrackerProvider), MethodType.Constructor)]
internal class MapTrackerProviderCtorPatch
{
    internal static void Postfix(MapTrackerProvider __instance)
    {
        if (__instance is not null)
            MilitiaTrackerManager.SetProvider(__instance);
    }
}

// BMR-FEATURE: Militia map markers | gate: setting:ShowMilitiaMarkers | Postfix: vanilla never tracks bandit parties
[HarmonyPatch(typeof(MapTrackerProvider), "CanAddMobileParty")]
internal class MapTrackerProviderCanAddMobilePartyPatch
{
    internal static void Postfix(MobileParty party, ref bool __result)
    {
        if (!__result && party is not null && MilitiaTrackerManager.ShouldTrack(party))
            __result = true;
    }
}
#endif
