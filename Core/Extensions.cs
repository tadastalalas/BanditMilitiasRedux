using BanditMilitiasRedux.Data;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Core;

// Canonical checks for what belongs to this mod.
internal static class Extensions
{
    private static readonly AccessTools.FieldRef<MobileParty, bool> IsCurrentlyUsedByAQuest =
        AccessTools.FieldRefAccess<MobileParty, bool>("_isCurrentlyUsedByAQuest");

    internal static bool IsUsedByAQuest(this MobileParty mobileParty) => IsCurrentlyUsedByAQuest(mobileParty);

    // A militia ordered to raid keeps RaidSettlement as its default behavior for the whole march.
    internal static bool IsTooBusyToMerge(this MobileParty mobileParty) =>
        mobileParty.DefaultBehavior is AiBehavior.RaidSettlement
        || mobileParty.ShortTermBehavior is AiBehavior.FleeToPoint or AiBehavior.RaidSettlement;

    // In a battle the roster must stay as it is.
    internal static bool IsTooBusyToGrowOrSplit(this MobileParty mobileParty) =>
        mobileParty.MapEvent is not null
        || mobileParty.TargetParty is not null
        || mobileParty.ShortTermTargetParty is not null
        || mobileParty.ShortTermBehavior is AiBehavior.EngageParty
            or AiBehavior.FleeToPoint
            or AiBehavior.RaidSettlement;

    // Merging or splitting removes the old party, and vanilla frees the player when his captor party is removed.
    internal static bool IsHoldingPlayerPrisoner(this MobileParty mobileParty) =>
        Hero.MainHero?.PartyBelongedToAsPrisoner == mobileParty.Party;

    internal static bool IsBanditMilitiaParty(this MobileParty mobileParty) => mobileParty?.PartyComponent is BanditMilitiaPartyComponent;
    internal static bool IsBanditMilitiaHero(this Hero hero) => Globals.AllAliveBanditMilitiaHeroes.Contains(hero);
    internal static bool IsBanditMilitiaCharacterObject(this CharacterObject characterObject) => characterObject?.HeroObject?.IsBanditMilitiaHero() == true;

    internal static BanditMilitiaPartyComponent? GetBanditMilitiaParty(this MobileParty mobileParty)
        => mobileParty.PartyComponent as BanditMilitiaPartyComponent;
}
