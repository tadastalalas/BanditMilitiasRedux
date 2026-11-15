using BanditMilitiasRedux.Core;
using HarmonyLib;
using SandBox.ViewModelCollection.Map.Tracker;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.Tracker;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using static BanditMilitiasRedux.Core.Globals;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia map markers show the militia banner | gate: setting:ShowMilitiaMarkers | Postfix: swap the banner vanilla built for the tracker
[HarmonyPatch(typeof(MapTrackerItemVM), "UpdateProperties")]
internal class MapTrackerItemVMPatch
{
    internal static void Postfix(MapTrackerItemVM __instance, ref BannerImageIdentifierVM ____factionVisualBind)
    {
        if (__instance?.TrackedObject is not MobileParty party || !party.IsBanditMilitiaParty())
            return;

        if (!PartyImageMap.TryGetValue(party, out BannerImageIdentifierVM? image))
        {
            image = new BannerImageIdentifierVM(party.GetBanditMilitiaParty()!.Banner);
            PartyImageMap.Add(party, image);
        }

        ____factionVisualBind = image;
    }
}
