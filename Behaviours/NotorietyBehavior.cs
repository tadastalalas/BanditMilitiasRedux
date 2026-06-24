using System.Collections.Generic;
using BanditMilitiasRedux.Helpers;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BanditMilitiasRedux.Behaviours
{
    public sealed class NotorietyBehavior : CampaignBehaviorBase
    {
        [SaveableField(1)] private Dictionary<Hero, int> _battlesWon = new Dictionary<Hero, int>();
        [SaveableField(2)] private Dictionary<Hero, CampaignTime> _firstSeen = new Dictionary<Hero, CampaignTime>();
        [SaveableField(3)] private Dictionary<Hero, int> _defeats = new Dictionary<Hero, int>();
        [SaveableField(4)] private Dictionary<Hero, Dictionary<Hero, int>> _beatenBy = new Dictionary<Hero, Dictionary<Hero, int>>();

        public static NotorietyBehavior? Instance { get; private set; }
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
            if (loser is null)
                return;
            
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

        public int GetTier(Hero hero) => GetTier(GetBattlesWon(hero), GetFirstSeen(hero));

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
        
        internal static int GetTier(int battlesWon, CampaignTime firstSeen)
        {
            var winTier = CountReached(Globals.NotorietyWinThresholds, battlesWon);
            var daysAlive = (int)(CampaignTime.Now - firstSeen).ToDays;
            var ageTier = CountReached(Globals.NotorietyDayThresholds, daysAlive);
            return winTier > ageTier ? winTier : ageTier;
        }

        private static int CountReached(int[] thresholds, int value)
        {
            var tier = 0;
            for (var i = 0; i < thresholds.Length; i++)
            {
                if (value >= thresholds[i]) tier = i + 1;
                else break;
            }
            return tier;
        }

        internal static string? GetEpithet(int tier)
        {
            if (tier <= 0)
                return null;
            var epithets = Globals.NotorietyEpithets;
            var index = tier - 1;
            return index < epithets.Length ? epithets[index] : epithets[epithets.Length - 1];
        }

        private static Hero GetMostFearedHunter(Hero leader, out float value)
        {
            value = 0f;
            var bm = leader?.PartyBelongedTo?.GetBanditMilitiaParty();
            if (bm?.Avoidance is null) return null;

            Hero best = null;
            var max = 0f;
            foreach (var pair in bm.Avoidance)
            {
                if (pair.Key is not null && pair.Value > max)
                {
                    max = pair.Value;
                    best = pair.Key;
                }
            }
            value = max;
            return best;
        }

        internal static TextObject BuildEncyclopediaText(Hero hero)
        {
            StringHelpers.SetCharacterProperties("LEADER", hero.CharacterObject, null, false);

            var battlesWon = NotorietyBehavior.Instance?.GetBattlesWon(hero) ?? 0;
            var tier = NotorietyBehavior.Instance?.GetTier(hero) ?? 0;
            var epithet = GetEpithet(tier);

            var result = new TextObject(
                "{=BMREncBase}{LEADER.FIRSTNAME} is a brigand chief leading a band of cutthroats that raids under the name of the {CLAN_NAME}.");
            result.SetTextVariable("CLAN_NAME",
                hero.Clan?.Name ?? new TextObject("{=BMREncNoClan}masterless brigands"));

            if (epithet is not null)
            {
                var note = battlesWon > 0
                    ? new TextObject("{=BMREncEpithet1} Across the land {?LEADER.GENDER}she{?}he{\\?} is spoken of as {LEADER.FIRSTNAME} {EPITHET}, victor of {WINS} {?MANY}battles{?}battle{\\?}.")
                    : new TextObject("{=BMREncEpithet2} Across the land {?LEADER.GENDER}she{?}he{\\?} is spoken of as {LEADER.FIRSTNAME} {EPITHET}.");
                note.SetTextVariable("EPITHET", epithet);
                if (battlesWon > 0)
                {
                    note.SetTextVariable("WINS", battlesWon);
                    note.SetTextVariable("MANY", battlesWon > 1 ? 1 : 0);
                }
                result = Concat(result, note);
            }

            var defeats = NotorietyBehavior.Instance?.GetDefeats(hero) ?? 0;
            if (defeats > 0)
            {
                var defeatNote = new TextObject("{=BMREncDefeats} {?LEADER.GENDER}She{?}He{\\?} has been beaten in battle {DEFEATS} {?MANY}times{?}time{\\?}.");
                
                defeatNote.SetTextVariable("DEFEATS", defeats);
                defeatNote.SetTextVariable("MANY", defeats > 1 ? 1 : 0);
                result = Concat(result, defeatNote);
            }

            Hero? feared = null;
            feared = GetMostFearedHunter(hero, out var avoidance);

            if (!(avoidance >= Globals.NotorietyFearMentionThreshold))
                return result;
            
            var fearNote = new TextObject("{=BMREncFear} Above all others, {?LEADER.GENDER}she{?}he{\\?} fears {HUNTER}.");
            
            fearNote.SetTextVariable("HUNTER", feared.Name);
            result = Concat(result, fearNote);

            return result;
        }

        private static TextObject Concat(TextObject a, TextObject b)
        {
            var t = new TextObject("{=!}{A}{B}");
            t.SetTextVariable("A", a);
            t.SetTextVariable("B", b);
            return t;
        }
    }
}