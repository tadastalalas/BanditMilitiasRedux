using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militias may always upgrade troops | gate: always | Postfix: overrides the vanilla answer for militias
[HarmonyPatch(typeof(DefaultPartyTroopUpgradeModel), "CanPartyUpgradeTroopToTarget")]
internal class DefaultPartyTroopUpgradeModelPatch
{
    // Model override path: plain checks only.
    internal static void Postfix(PartyBase upgradingParty, ref bool __result)
    {
        if (upgradingParty is null)
            return;

        if (upgradingParty.MobileParty.IsBanditMilitiaParty() && upgradingParty.IsMobile)
            __result = true;
    }
}
