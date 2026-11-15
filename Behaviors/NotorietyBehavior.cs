using System.Collections.Generic;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Data;
using Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.Localization;
using TaleWorlds.SaveSystem;

namespace BanditMilitiasRedux.Behaviors;

// BMR-FEATURE: Notoriety, militia leaders gain epithets from wins and age; encyclopedia text | gate: always
public sealed class NotorietyBehavior : CampaignBehaviorBase
{
    // Released SyncData keys (kept for save compatibility).
    private const string BattlesWonKey = "_battlesWon";
    private const string FirstSeenKey = "_firstSeen";
    private const string DefeatsKey = "_defeats";
    private const string BeatenByKey = "_beatenBy";
    private const string LeaderTag = "LEADER";

    [SaveableField(1)] private Dictionary<Hero, int> _battlesWon = new();
    [SaveableField(2)] private Dictionary<Hero, CampaignTime> _firstSeen = new();
    [SaveableField(3)] private Dictionary<Hero, int> _defeats = new();
    [SaveableField(4)] private Dictionary<Hero, Dictionary<Hero, int>> _beatenBy = new();

    public static NotorietyBehavior? Instance { get; private set; }
    public NotorietyBehavior() { Instance = this; }

    public override void RegisterEvents()
    {
        CampaignEvents.HeroKilledEvent.AddNonSerializedListener(this, OnHeroKilled);
        CampaignEvents.OnGameLoadedEvent.AddNonSerializedListener(this, _ => Backfill());
    }

    // Keys: "_battlesWon", "_firstSeen", "_defeats", "_beatenBy".
    public override void SyncData(IDataStore dataStore)
    {
        dataStore.SyncData(BattlesWonKey, ref _battlesWon);
        dataStore.SyncData(FirstSeenKey, ref _firstSeen);
        dataStore.SyncData(DefeatsKey, ref _defeats);
        dataStore.SyncData(BeatenByKey, ref _beatenBy);
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
        => hero is not null && _battlesWon.TryGetValue(hero, out int wins) ? wins : 0;

    public void RecordDefeat(Hero loser, Hero? victor)
    {
        if (loser is null)
            return;

        EnsureTracked(loser);
        _defeats[loser] += 1;

        if (victor is not null && victor != loser)
        {
            if (!_beatenBy.TryGetValue(loser, out Dictionary<Hero, int>? byHero))
                _beatenBy[loser] = byHero = new Dictionary<Hero, int>();
            byHero[victor] = byHero.TryGetValue(victor, out int count) ? count + 1 : 1;
        }

        if (loser.PartyBelongedTo?.LeaderHero == loser)
            loser.PartyBelongedTo.PartyComponent?.ClearCachedName();
    }

    public int GetDefeats(Hero hero)
        => hero is not null && _defeats.TryGetValue(hero, out int defeats) ? defeats : 0;

    public Hero? GetMostFearedHunter(Hero hero, out int timesBeaten)
    {
        timesBeaten = 0;
        if (hero is null || !_beatenBy.TryGetValue(hero, out Dictionary<Hero, int>? byHero))
            return null;

        Hero? best = null;
        int max = 0;
        foreach (KeyValuePair<Hero, int> pair in byHero)
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
        => hero is not null && _firstSeen.TryGetValue(hero, out CampaignTime firstSeen) ? firstSeen : CampaignTime.Now;

    public int GetTier(Hero hero) => GetTier(GetBattlesWon(hero), GetFirstSeen(hero));

    private void OnHeroKilled(Hero victim, Hero killer, KillCharacterAction.KillCharacterActionDetail detail, bool showNotification)
    {
        if (victim is null) return;
        _battlesWon.Remove(victim);
        _firstSeen.Remove(victim);
        _defeats.Remove(victim);
        _beatenBy.Remove(victim);
    }

    private void Backfill()
    {
        foreach (Hero hero in Hero.AllAliveHeroes)
            if (hero.IsBanditMilitiaHero())
                EnsureTracked(hero);
    }

    internal static int GetTier(int battlesWon, CampaignTime firstSeen)
    {
        int winTier = CountReached(Balance.NotorietyWinThresholds, battlesWon);
        int daysAlive = (int)(CampaignTime.Now - firstSeen).ToDays;
        int ageTier = CountReached(Balance.NotorietyDayThresholds, daysAlive);
        return winTier > ageTier ? winTier : ageTier;
    }

    private static int CountReached(int[] thresholds, int value)
    {
        int tier = 0;
        for (int i = 0; i < thresholds.Length; i++)
        {
            if (value >= thresholds[i]) tier = i + 1;
            else break;
        }
        return tier;
    }

    internal static TextObject? GetEpithet(int tier)
    {
        if (tier <= 0)
            return null;
        TextObject[] epithets = Texts.NotorietyEpithets;
        int index = tier - 1;
        return index < epithets.Length ? epithets[index] : epithets[epithets.Length - 1];
    }

    private static Hero? GetMostFearedHunter(Hero leader, out float value)
    {
        value = 0f;
        BanditMilitiaPartyComponent? militia = leader?.PartyBelongedTo?.GetBanditMilitiaParty();
        if (militia?.Avoidance is null) return null;

        Hero? best = null;
        float max = 0f;
        foreach (KeyValuePair<Hero, float> pair in militia.Avoidance)
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
        StringHelpers.SetCharacterProperties(LeaderTag, hero.CharacterObject, null, false);

        int battlesWon = Instance?.GetBattlesWon(hero) ?? 0;
        int tier = Instance?.GetTier(hero) ?? 0;
        TextObject? epithet = GetEpithet(tier);

        TextObject result = Texts.EncyclopediaBase(hero.Clan?.Name ?? Texts.EncyclopediaNoClan);

        if (epithet is not null)
        {
            TextObject note = battlesWon > 0
                ? Texts.EncyclopediaEpithetWithWins(epithet, battlesWon, battlesWon > 1 ? 1 : 0)
                : Texts.EncyclopediaEpithet(epithet);
            result = Texts.Concat(result, note);
        }

        int defeats = Instance?.GetDefeats(hero) ?? 0;
        if (defeats > 0)
            result = Texts.Concat(result, Texts.EncyclopediaDefeats(defeats, defeats > 1 ? 1 : 0));

        Hero? feared = GetMostFearedHunter(hero, out float avoidance);

        if (!(avoidance >= Balance.NotorietyFearMentionThreshold) || feared is null)
            return result;

        return Texts.Concat(result, Texts.EncyclopediaFear(feared.Name));
    }
}
