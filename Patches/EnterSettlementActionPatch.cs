using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militias never enter settlements (they hold instead) | gate: always | Prefix: skips the vanilla enter
[HarmonyPatch(typeof(EnterSettlementAction), "ApplyForParty")]
internal class EnterSettlementActionPatch
{
    internal static bool Prefix(MobileParty mobileParty, Settlement settlement)
    {
        if (mobileParty is null || !mobileParty.IsBanditMilitiaParty())
            return true;

        mobileParty.SetMoveModeHold();
        return false;
    }
}
