using BanditMilitiasRedux.Core;
using HarmonyLib;
using SandBox.ViewModelCollection.Map.Tracker;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.ViewModelCollection.Map.Tracker;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using static BanditMilitiasRedux.Core.Globals;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia map markers show the militia's random banner | gate: setting:ShowMilitiaMarkers, setting:RandomBanners | Postfix: swap the banner vanilla built for the tracker
[HarmonyPatch(typeof(MapTrackerItemVM), "UpdateProperties")]
internal class MapTrackerItemVMPatch
{
    internal static void Postfix(MapTrackerItemVM __instance, ref BannerImageIdentifierVM ____factionVisualBind)
    {
        // Random Banners off: the marker keeps the clan banner vanilla built, like the map icon.
        if (!ModSettings.RandomBanners || __instance?.TrackedObject is not MobileParty party || !party.IsBanditMilitiaParty())
            return;

        if (!PartyImageMap.TryGetValue(party, out BannerImageIdentifierVM? image))
        {
            Banner? banner = party.GetBanditMilitiaParty()?.Banner;
            if (banner is null)
                return;

            image = new BannerImageIdentifierVM(banner);
            PartyImageMap.Add(party, image);
        }

        ____factionVisualBind = image;
    }
}
