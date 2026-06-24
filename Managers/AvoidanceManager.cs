using System;
using System.Collections.Generic;
using System.Linq;
using BanditMilitiasRedux.Constructors;
using BanditMilitiasRedux.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using static BanditMilitiasRedux.Globals;

namespace BanditMilitiasRedux.Managers;

internal static class AvoidanceManager
{
    internal static void AdjustAvoidance(MobileParty mobileParty)
    {
        Vec2 currentPartyPosition = mobileParty.Position.ToVec2();
        foreach (BanditMilitiaPartyComponent nearbyMilitiaParty in PowerCalculationManager.GetActiveBanditMilitiaParties()
             .WhereQ(militiaThatIsAround => militiaThatIsAround?.Leader is not null
             && militiaThatIsAround.MobileParty is { IsActive: true }
             && militiaThatIsAround.MobileParty.Position.ToVec2().DistanceSquared(currentPartyPosition) < AvoidanceDecayToOtherBanditHeroesRadiusSq))
        {
            if (nearbyMilitiaParty?.Avoidance is null)
                continue;

            KeyValuePair<Hero, float>[] storedEnemyLordsWithAvoidance = nearbyMilitiaParty.Avoidance.ToArrayQ();
            foreach (KeyValuePair<Hero, float> enemyLordWithAvoidance in storedEnemyLordsWithAvoidance)
            {
                if (enemyLordWithAvoidance.Key is null)
                    continue;
                
                float newAvoidanceValue = enemyLordWithAvoidance.Value - AvoidanceDecreaseAmount;
                nearbyMilitiaParty.Avoidance[enemyLordWithAvoidance.Key] = newAvoidanceValue < 0 ? 0 : newAvoidanceValue;
            }
        }
    }
    
    internal static void AdjustAvoidanceForSurroundingBanditMilitias(MobileParty destroyedMobileParty, PartyBase destroyer)
    {
        var cachedBanditMilitias = PowerCalculationManager.GetActiveBanditMilitiaParties();

        Vec2 destroyedBanditMilitiaPos2D = destroyedMobileParty.Position.ToVec2();

        for (int i = 0, n = cachedBanditMilitias.Count; i < n; i++)
        {
            var cachedBanditMilitia = cachedBanditMilitias[i];
            if (cachedBanditMilitia?.MobileParty is null || !cachedBanditMilitia.MobileParty.IsActive)
                continue;
            if (cachedBanditMilitia.MobileParty.Position.ToVec2().DistanceSquared(destroyedBanditMilitiaPos2D) > AvoidanceEffectToOtherBanditHeroesRadiusSq)
                continue;

            int avoidanceIncreaseValue = AvoidanceManager.AvoidanceIncreaseValue();
            if (cachedBanditMilitia.Avoidance.TryGetValue(destroyer.LeaderHero, out float currentAvoidance))
                cachedBanditMilitia.Avoidance[destroyer.LeaderHero] = currentAvoidance + avoidanceIncreaseValue;
            else
                cachedBanditMilitia.Avoidance.Add(destroyer.LeaderHero, avoidanceIncreaseValue);
        }
    }
    
    internal static void CalculateAverageAvoidance(BanditMilitiaPartyComponent banditMilitiaPartyComponent, Dictionary<Hero, float>  calculatedAvoidance)
    {
        foreach (KeyValuePair<Hero, float> enemyLordWithAvoidance in banditMilitiaPartyComponent.Avoidance)
        {
            if (calculatedAvoidance.TryGetValue(enemyLordWithAvoidance.Key, out float currentAvoidance))
                calculatedAvoidance[enemyLordWithAvoidance.Key] = (currentAvoidance + enemyLordWithAvoidance.Value) * 0.5f;
            else
                calculatedAvoidance[enemyLordWithAvoidance.Key] = enemyLordWithAvoidance.Value;
        }
    }

    internal static void DecreaseAvoidance(List<Hero> loserHeroes, MapEventParty mep)
    {
        var banditMilitiaPartyComponent = mep.Party.MobileParty?.GetBanditMilitiaParty();
            
        if (banditMilitiaPartyComponent?.Avoidance is null)
            return;

        foreach (var loserHero in loserHeroes.Where(loserHero => banditMilitiaPartyComponent.Avoidance.TryGetValue(loserHero, out _)))
            banditMilitiaPartyComponent.Avoidance[loserHero] = Math.Max(0, banditMilitiaPartyComponent.Avoidance[loserHero] - Globals.AvoidanceIncreaseMin);
    }
    
    private static int AvoidanceIncreaseValue() => MBRandom.RandomInt(AvoidanceIncreaseMin, AvoidanceIncreaseMax);
}