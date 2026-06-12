using System.Collections.Generic;
using System.Linq;
using BanditMilitiasRedux.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.SaveSystem;

namespace BanditMilitiasRedux.Behaviours
{
    public sealed class ReusableHeroesBehavior : CampaignBehaviorBase
    {
        public static ReusableHeroesBehavior? Instance { get; private set; }
        public ReusableHeroesBehavior() { Instance = this; }
        
        [SaveableField(1)] private static Dictionary<Clan, List<Hero>> _waitingToBeReusedHeroes = new();
        
        public override void RegisterEvents()
        {
            CampaignEvents.HeroPrisonerReleased.AddNonSerializedListener(this, OnHeroPrisonerReleasedEvent);
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilledEvent);
        }
        
        public Hero? TryGetReusableBanditHero(Clan clan)
        {
            if (!_waitingToBeReusedHeroes.TryGetValue(clan, out List<Hero> heroes))
                _waitingToBeReusedHeroes[clan] = heroes = [];
            
            Hero? reusableHero = heroes.FirstOrDefault();

            if (reusableHero == null)
                return null;
            
            RemoveHeroFromTheWaitingDictionary(reusableHero);
            return reusableHero;
        }

        private static void OnHeroPrisonerReleasedEvent(Hero? prisoner, PartyBase? party, IFaction capturerFaction, EndCaptivityDetail detail, bool showNotification = true)
        {
            if (prisoner == null || !prisoner.IsBanditMilitiaHero())
                return;

            if (detail == EndCaptivityDetail.Death)
                return;

            prisoner.StayingInSettlement = null;
            
            if (prisoner.HeroState != Hero.CharacterStates.Active)
                prisoner.ChangeState(Hero.CharacterStates.Active);

            AddHeroToTheWaitingDictionary(prisoner);
        }

        private static void OnHeroKilledEvent(Hero? victim, Hero? killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim == null || !victim.IsBanditMilitiaHero())
                return;

            RemoveHeroFromTheWaitingDictionary(victim);
        }

        private static void AddHeroToTheWaitingDictionary(Hero hero)
        {
            if (!_waitingToBeReusedHeroes.TryGetValue(hero.Clan, out List<Hero> heroes))
                _waitingToBeReusedHeroes[hero.Clan] = heroes = [];

            if (heroes.Contains(hero))
                return;

            heroes.Add(hero);
        }
        
        private static void RemoveHeroFromTheWaitingDictionary(Hero hero)   
        {
            if (!_waitingToBeReusedHeroes.TryGetValue(hero.Clan, out List<Hero> heroes))
                return;
            
            if (!heroes.Contains(hero))
                return;

            heroes.Remove(hero);
        }
        
        public override void SyncData(IDataStore dataStore)
            => dataStore.SyncData("_waitingToBeReusedHeroes", ref _waitingToBeReusedHeroes);
    }
}