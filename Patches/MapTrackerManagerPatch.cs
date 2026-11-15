#if !LOWER_THAN_1_5
// v1.5.7 only: replaces MapTrackerProviderPatch (1.4.x); MapTrackerManager now decides which parties get a tracker.
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia map markers | gate: setting:ShowMilitiaMarkers | Postfix: vanilla never tracks bandit parties
[HarmonyPatch(typeof(MapTrackerManager), "ShouldAutoAddTrackerForParty")]
internal class MapTrackerManagerPatch
{
    internal static void Postfix(MobileParty party, ref bool __result)
    {
        if (!__result && party is not null && MilitiaTrackerManager.ShouldTrack(party))
            __result = true;
    }
}
#endif
