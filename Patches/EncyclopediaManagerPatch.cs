using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encyclopedia;

namespace BanditMilitiasRedux.Patches;

// Blocks navigation to bandit-clan encyclopedia pages, which the vanilla
// clan page VM cannot build (it is designed around Clan.NonBanditFactions).
// BMR-FEATURE: Encyclopedia links to bandit clans do nothing | gate: always | Prefix: swallows the click before vanilla opens a page it cannot build
[HarmonyPatch(typeof(EncyclopediaManager), nameof(EncyclopediaManager.GoToLink), new[] { typeof(string), typeof(string) })]
internal class EncyclopediaManagerPatch
{
    internal static bool Prefix(string pageType, string stringID)
    {
        if (stringID is null)
            return true;

        Clan? clan = Campaign.Current?.CampaignObjectManager.Find<Clan>(stringID);
        return clan is null || !clan.IsBanditFaction;
    }
}
