using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using static BanditMilitiasRedux.Core.Globals;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Executing a militia leader costs no Honor | gate: always | Prefix: must skip vanilla's -1000 Honor; OnLordExecuted itself gets inlined so patching it does nothing
[HarmonyPatch(typeof(TraitLevelingHelper), "AddPlayerTraitXPAndLogEntry")]
internal class TraitLevelingHelperPatch
{
    internal static bool Prefix(TraitObject trait, int xpValue)
        => SuppressedExecutionVictim is null || trait != DefaultTraits.Honor || xpValue != Balance.VanillaExecutionHonorPenalty;
}
