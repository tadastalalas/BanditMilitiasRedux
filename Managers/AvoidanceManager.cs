using System;
using System.Collections.Generic;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Data;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BanditMilitiasRedux.Managers;

// BMR-FEATURE: Avoidance, militias learn to avoid heroes who destroy militias and forget it weekly | gate: always
internal static class AvoidanceManager
{
    private static readonly List<Hero> AvoidanceKeysBuffer = [];

    // Daily tick path: reused key buffer, no LINQ.
    internal static void AdjustAvoidance(MobileParty mobileParty)
    {
        Dictionary<Hero, float>? avoidance = mobileParty.GetBanditMilitiaParty()?.Avoidance;
        if (avoidance is null || avoidance.Count == 0)
            return;

        List<Hero> enemyLords = AvoidanceKeysBuffer;
        enemyLords.Clear();
        enemyLords.AddRange(avoidance.Keys);
        for (int i = 0; i < enemyLords.Count; i++)
        {
            Hero enemyLord = enemyLords[i];
            float newAvoidanceValue = avoidance[enemyLord] - Balance.AvoidanceDecreaseAmount;
            avoidance[enemyLord] = newAvoidanceValue < 0 ? 0 : newAvoidanceValue;
        }
        enemyLords.Clear();
    }

    internal static void AdjustAvoidanceForSurroundingBanditMilitias(MobileParty destroyedMobileParty, PartyBase destroyer)
    {
        IReadOnlyList<BanditMilitiaPartyComponent?> cachedBanditMilitias = PowerCalculationManager.GetActiveBanditMilitiaParties();

        Vec2 destroyedBanditMilitiaPos2D = destroyedMobileParty.Position.ToVec2();

        for (int i = 0, n = cachedBanditMilitias.Count; i < n; i++)
        {
            BanditMilitiaPartyComponent? cachedBanditMilitia = cachedBanditMilitias[i];
            if (cachedBanditMilitia?.MobileParty is null || !cachedBanditMilitia.MobileParty.IsActive)
                continue;
            if (cachedBanditMilitia.MobileParty.Position.ToVec2().DistanceSquared(destroyedBanditMilitiaPos2D) > Balance.AvoidanceEffectToOtherBanditHeroesRadiusSq)
                continue;

            int avoidanceIncreaseValue = AvoidanceIncreaseValue();
            if (cachedBanditMilitia.Avoidance.TryGetValue(destroyer.LeaderHero, out float currentAvoidance))
                cachedBanditMilitia.Avoidance[destroyer.LeaderHero] = currentAvoidance + avoidanceIncreaseValue;
            else
                cachedBanditMilitia.Avoidance.Add(destroyer.LeaderHero, avoidanceIncreaseValue);
        }
    }

    internal static void CalculateAverageAvoidance(BanditMilitiaPartyComponent banditMilitiaPartyComponent, Dictionary<Hero, float> calculatedAvoidance)
    {
        foreach (KeyValuePair<Hero, float> enemyLordWithAvoidance in banditMilitiaPartyComponent.Avoidance)
        {
            if (calculatedAvoidance.TryGetValue(enemyLordWithAvoidance.Key, out float currentAvoidance))
                calculatedAvoidance[enemyLordWithAvoidance.Key] = (currentAvoidance + enemyLordWithAvoidance.Value) * Balance.AvoidanceMergeAverageFactor;
            else
                calculatedAvoidance[enemyLordWithAvoidance.Key] = enemyLordWithAvoidance.Value;
        }
    }

    internal static void DecreaseAvoidance(List<Hero?> loserHeroes, MapEventParty mapEventParty)
    {
        BanditMilitiaPartyComponent? banditMilitiaPartyComponent = mapEventParty.Party.MobileParty?.GetBanditMilitiaParty();

        if (banditMilitiaPartyComponent?.Avoidance is null)
            return;

        Dictionary<Hero, float> avoidance = banditMilitiaPartyComponent.Avoidance;
        for (int i = 0; i < loserHeroes.Count; i++)
        {
            Hero? loserHero = loserHeroes[i];
            if (loserHero is null || !avoidance.TryGetValue(loserHero, out float currentAvoidance))
                continue;
            avoidance[loserHero] = Math.Max(0, currentAvoidance - Balance.AvoidanceIncreaseMin);
        }
    }

    private static int AvoidanceIncreaseValue() => MBRandom.RandomInt(Balance.AvoidanceIncreaseMin, Balance.AvoidanceIncreaseMax);
}
