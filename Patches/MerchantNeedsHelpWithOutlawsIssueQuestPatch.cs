using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Issues;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militias are excluded from the "merchant needs help with outlaws" quest tick | gate: always | Prefix: skips vanilla for militias
[HarmonyPatch(typeof(MerchantNeedsHelpWithOutlawsIssueQuestBehavior.MerchantNeedsHelpWithOutlawsIssueQuest), "HourlyTickParty")]
internal class MerchantNeedsHelpWithOutlawsIssueQuestPatch
{
    // Hourly tick path: plain checks only.
    internal static bool Prefix(MobileParty mobileParty) => mobileParty is null || !mobileParty.IsBanditMilitiaParty();
}
