using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CampaignBehaviors.AiBehaviors;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Vanilla land-bandit patrol AI is skipped for militias (they patrol near home) | gate: always | Prefix: skips vanilla for militias
[HarmonyPatch(typeof(AiLandBanditPatrollingBehavior), "AiHourlyTick")]
internal class AiLandBanditPatrollingBehaviorPatch
{
    internal static bool Prefix(MobileParty mobileParty) => mobileParty is null || !mobileParty.IsBanditMilitiaParty();
}
