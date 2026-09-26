using BanditMilitiasRedux.Helpers;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Localization;
using static BanditMilitiasRedux.Globals;

namespace BanditMilitiasRedux.Behaviours
{
    public sealed class HeroDefectionBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
            => CampaignEvents.OnHeroChangedClanEvent.AddNonSerializedListener(this, OnHeroChangedClan);

        public override void SyncData(IDataStore dataStore) { }

        private static void OnHeroChangedClan(Hero? hero, Clan? oldClan)
        {
            if (hero is null || !hero.IsBanditMilitiaHero() || hero.Clan?.IsBanditFaction == true)
                return;

            AllAliveBanditMilitiaHeroes.Remove(hero);
            ReusableHeroesBehavior.RemoveHeroFromTheWaitingDictionary(hero, oldClan);

            // Only fill the gap if the recruiting mod left him a Bandit; never override Lord etc.
            if (hero.Occupation == Occupation.Bandit)
                hero.SetNewOccupation(Occupation.Wanderer);

            hero.EncyclopediaText = BuildDefectionText(hero, oldClan);
        }

        private static TextObject BuildDefectionText(Hero hero, Clan? oldClan)
        {
            TextObject text = new("{=BMREncDefected}{LEADER.FIRSTNAME} once led a band of cutthroats that raided under the name of the {OLD_CLAN}. {?LEADER.GENDER}She{?}He{\\?} has since turned {?LEADER.GENDER}her{?}his{\\?} back on that life and now sells {?LEADER.GENDER}her{?}his{\\?} sword to whoever will pay for it.");
            StringHelpers.SetCharacterProperties("LEADER", hero.CharacterObject, text, false);
            text.SetTextVariable("OLD_CLAN", oldClan?.Name ?? new TextObject("{=BMREncNoClan}masterless brigands"));
            return text;
        }
    }
}