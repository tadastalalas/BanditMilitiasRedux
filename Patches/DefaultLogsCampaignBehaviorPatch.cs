using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia hero "taken prisoner" log messages hidden | gate: setting:RemovePrisonerMessages | Prefix: skips the vanilla log entry
[HarmonyPatch(typeof(DefaultLogsCampaignBehavior), "OnPrisonerTaken")]
internal class DefaultLogsCampaignBehaviorOnPrisonerTakenPatch
{
    internal static bool Prefix(PartyBase party, Hero hero)
        => !ModSettings.RemovePrisonerMessages || party == PartyBase.MainParty || hero?.IsBanditMilitiaHero() != true;
}

// BMR-FEATURE: Militia hero "released" log messages hidden | gate: setting:RemovePrisonerMessages | Prefix: skips the vanilla log entry
[HarmonyPatch(typeof(DefaultLogsCampaignBehavior), "OnHeroPrisonerReleased")]
internal class DefaultLogsCampaignBehaviorOnHeroPrisonerReleasedPatch
{
    internal static bool Prefix(PartyBase party, Hero hero)
        => !ModSettings.RemovePrisonerMessages || party == PartyBase.MainParty || hero?.IsBanditMilitiaHero() != true;
}
