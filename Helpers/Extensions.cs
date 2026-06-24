using BanditMilitiasRedux.Constructors;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Helpers
{
    internal static class Extensions
    {
        private static readonly AccessTools.FieldRef<MobileParty, bool> IsCurrentlyUsedByAQuest =
            AccessTools.FieldRefAccess<MobileParty, bool>("_isCurrentlyUsedByAQuest");

        internal static bool IsUsedByAQuest(this MobileParty mobileParty) => IsCurrentlyUsedByAQuest(mobileParty);

        internal static bool IsTooBusyToMerge(this MobileParty mobileParty)
        {
            return mobileParty.ShortTermBehavior is AiBehavior.FleeToPoint or AiBehavior.RaidSettlement;
        }

        internal static bool IsTooBusyToGrowOrSplit(this MobileParty mobileParty)
        {
            return mobileParty.TargetParty is not null
                   || mobileParty.ShortTermTargetParty is not null
                   || mobileParty.ShortTermBehavior is AiBehavior.EngageParty
                       or AiBehavior.FleeToPoint
                       or AiBehavior.RaidSettlement;
        }

        internal static bool IsBanditMilitiaParty(this MobileParty mobileParty) => mobileParty?.PartyComponent is BanditMilitiaPartyComponent;
        internal static bool IsBanditMilitiaHero(this Hero hero) => Globals.AllAliveBanditMilitiaHeroes.Contains(hero);
        internal static bool IsBanditMilitiaCharacterObject(this CharacterObject characterObject) => characterObject?.HeroObject?.IsBanditMilitiaHero() == true;

        internal static BanditMilitiaPartyComponent GetBanditMilitiaParty(this MobileParty mobileParty)
        {
            if (mobileParty.PartyComponent is BanditMilitiaPartyComponent partyComponent)
                return partyComponent;
            return null;
        }
    }
}