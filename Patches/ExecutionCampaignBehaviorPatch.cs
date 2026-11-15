#if !LOWER_THAN_1_5
// v1.5.7 only: replaces DefaultExecutionRelationModelPatch (1.4.x). In 1.5.7 the player executing a hero starts a
// blood feud with his clan (relation penalties with other clans) and costs Honor/Mercy via TraitLevelingHelper.OnBloodFeudStarted.
using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Executing a militia leader angers no one and costs no Honor | gate: always | Prefix: skips the vanilla blood feud start for militia heroes
[HarmonyPatch(typeof(ExecutionCampaignBehavior), "OnPlayerExecutedHero")]
internal class ExecutionCampaignBehaviorPatch
{
    internal static bool Prefix(Hero victim) => victim is null || !victim.IsBanditMilitiaHero();
}
#endif
