#if LOWER_THAN_1_5
// v1.4.7 / v1.4.8 only. v1.5.7 removed DefaultExecutionRelationModel; see ExecutionCampaignBehaviorPatch.
using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Executing a militia leader angers no one | gate: always | Postfix: zeroes the relation change vanilla computed
[HarmonyPatch(typeof(DefaultExecutionRelationModel), nameof(DefaultExecutionRelationModel.GetRelationChangeForExecutingHero))]
internal class DefaultExecutionRelationModelPatch
{
    internal static void Postfix(Hero victim, ref int __result, ref bool showQuickNotification)
    {
        if (victim?.IsBanditMilitiaHero() != true)
            return;
        __result = 0;
        showQuickNotification = false;
    }
}
#endif
