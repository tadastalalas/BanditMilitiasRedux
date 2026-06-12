using System.Collections.Generic;
using BanditMilitiasRedux.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.SaveSystem;

namespace BanditMilitiasRedux
{
    public sealed class NotorietyBehavior : CampaignBehaviorBase
    {
        [SaveableField(1)] private Dictionary<Hero, int> _battlesWon = new Dictionary<Hero, int>();
        [SaveableField(2)] private Dictionary<Hero, CampaignTime> _firstSeen = new Dictionary<Hero, CampaignTime>();
        [SaveableField(3)] private Dictionary<Hero, int> _defeats = new Dictionary<Hero, int>();
        [SaveableField(4)] private Dictionary<Hero, Dictionary<Hero, int>> _beatenBy = new Dictionary<Hero, Dictionary<Hero, int>>();

        public static NotorietyBehavior Instance { get; private set; }
        public NotorietyBehavior() { Instance = this; }

        public override void RegisterEvents()
        {
            CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
            CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, _ => Backfill());
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("_battlesWon", ref _battlesWon);
            dataStore.SyncData("_firstSeen", ref _firstSeen);
            dataStore.SyncData("_defeats", ref _defeats);
            dataStore.SyncData("_beatenBy", ref _beatenBy);
        }

        public void EnsureTracked(Hero hero)
        {
            if (hero is null) return;
            if (!_firstSeen.ContainsKey(hero)) _firstSeen[hero] = CampaignTime.Now;
            if (!_battlesWon.ContainsKey(hero)) _battlesWon[hero] = 0;
            if (!_defeats.ContainsKey(hero)) _defeats[hero] = 0;
        }

        public void RecordWin(Hero hero)
        {
            if (hero is null) return;
            EnsureTracked(hero);
            _battlesWon[hero] += 1;
            if (hero.PartyBelongedTo?.LeaderHero == hero)
                hero.PartyBelongedTo.PartyComponent?.ClearCachedName();
        }

        public int GetBattlesWon(Hero hero)
            => hero is not null && _battlesWon.TryGetValue(hero, out var w) ? w : 0;

        public void RecordDefeat(Hero loser, Hero victor)
        {
            if (loser is null) return;
            EnsureTracked(loser);
            _defeats[loser] += 1;

            if (victor is not null && victor != loser)
            {
                if (!_beatenBy.TryGetValue(loser, out var byHero))
                    _beatenBy[loser] = byHero = new Dictionary<Hero, int>();
                byHero[victor] = byHero.TryGetValue(victor, out var c) ? c + 1 : 1;
            }

            if (loser.PartyBelongedTo?.LeaderHero == loser)
                loser.PartyBelongedTo.PartyComponent?.ClearCachedName();
        }

        public int GetDefeats(Hero hero)
            => hero is not null && _defeats.TryGetValue(hero, out var d) ? d : 0;

        public Hero GetMostFearedHunter(Hero hero, out int timesBeaten)
        {
            timesBeaten = 0;
            if (hero is null || !_beatenBy.TryGetValue(hero, out var byHero))
                return null;

            Hero best = null;
            var max = 0;
            foreach (var pair in byHero)
            {
                if (pair.Key is not null && pair.Key.IsAlive && pair.Value > max)
                {
                    max = pair.Value;
                    best = pair.Key;
                }
            }
            timesBeaten = max;
            return best;
        }

        public CampaignTime GetFirstSeen(Hero hero)
            => hero is not null && _firstSeen.TryGetValue(hero, out var t) ? t : CampaignTime.Now;

        public int GetTier(Hero hero)
            => NotorietyService.GetTier(GetBattlesWon(hero), GetFirstSeen(hero));

        private void OnHeroKilled(Hero victim, Hero killer,
            KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
        {
            if (victim is null) return;
            _battlesWon.Remove(victim);
            _firstSeen.Remove(victim);
            _defeats.Remove(victim);
            _beatenBy.Remove(victim);
        }

        private void Backfill()
        {
            foreach (var hero in Hero.AllAliveHeroes)
                if (hero.IsBanditMilitiaHero())
                    EnsureTracked(hero);
        }
    }
}