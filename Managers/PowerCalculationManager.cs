using System;
using System.Collections.Generic;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Data;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;

namespace BanditMilitiasRedux.Managers;

// BMR-FEATURE: Power calculation, caps militia size and total militia strength | gate: setting:GlobalPowerPercent
internal static class PowerCalculationManager
{
    internal static float CalculatedMaxPartySize { get; private set; }
    internal static float CalculatedGlobalPowerLimit { get; private set; }
    internal static int GlobalMilitiaPower { get; private set; }
    internal static float MilitiaPowerPercent { get; private set; }
    internal static int MilitiaPartyAveragePower { get; private set; }

    private static double _lastCalculated;
    private static double _lastCacheUpdateHour;
    private static List<BanditMilitiaPartyComponent?> _allBanditMilitias = [];
    private static readonly List<MobileParty> AllLordsPartiesBuffer = [];

    private static float Variance => MBRandom.RandomFloatRanged(Balance.PowerVarianceMin, Balance.PowerVarianceMax);

    internal static void Reset()
    {
        _allBanditMilitias = [];
        _lastCalculated = 0;
        _lastCacheUpdateHour = 0;
        CalculatedMaxPartySize = 0;
        CalculatedGlobalPowerLimit = 0;
        GlobalMilitiaPower = 0;
        MilitiaPowerPercent = 0;
        MilitiaPartyAveragePower = 0;
    }

    // Hot path (AI ticks): returns the cached list; rebuilt at most once per in-game hour unless forced.
    internal static IReadOnlyList<BanditMilitiaPartyComponent?> GetActiveBanditMilitiaParties(bool forceRefresh = false)
    {
        if (!forceRefresh && !(_lastCacheUpdateHour < CampaignTime.Now.ToHours - 1))
            return _allBanditMilitias;

        _lastCacheUpdateHour = CampaignTime.Now.ToHours;
        MBReadOnlyList<MobileParty> banditParties = MobileParty.AllBanditParties;
        List<BanditMilitiaPartyComponent?> militias = new(banditParties.Count);
        for (int i = 0; i < banditParties.Count; i++)
        {
            MobileParty party = banditParties[i];
            if (party is { IsActive: true } && party.PartyComponent is BanditMilitiaPartyComponent component)
                militias.Add(component);
        }
        _allBanditMilitias = militias;

        return _allBanditMilitias;
    }

    internal static void DoPowerCalculations(bool force = false)
    {
        if (!force && _lastCalculated >= CampaignTime.Now.ToHours - Balance.PowerRecalcIntervalHours)
            return;

        IReadOnlyList<BanditMilitiaPartyComponent?> activeBanditMilitiaParties = GetActiveBanditMilitiaParties(true);
        int militiaPower = 0;
        for (int i = 0; i < activeBanditMilitiaParties.Count; i++)
            militiaPower += (int)activeBanditMilitiaParties[i]!.Party.EstimatedStrength;
        GlobalMilitiaPower = militiaPower;
        int militiasCount = activeBanditMilitiaParties.Count;
        MilitiaPartyAveragePower = militiasCount > 0 ? GlobalMilitiaPower / militiasCount : 0;

        List<MobileParty> allLordsParties = AllLordsPartiesBuffer;
        allLordsParties.Clear();
        MBReadOnlyList<MobileParty> allParties = MobileParty.All;
        for (int i = 0; i < allParties.Count; i++)
        {
            MobileParty mobileParty = allParties[i];
            if (mobileParty.LeaderHero is not null && !mobileParty.IsBanditMilitiaParty())
                allLordsParties.Add(mobileParty);
        }

        _lastCalculated = CampaignTime.Now.ToHours;
        if (allLordsParties.Count == 0)
        {
            MilitiaPowerPercent = 0;
            return;
        }
        float variance = Variance;
        int[] counts = new int[allLordsParties.Count];
        for (int i = 0; i < allLordsParties.Count; i++)
            counts[i] = allLordsParties[i].MemberRoster.TotalManCount;

        Array.Sort(counts);
        float medianSize = counts[counts.Length / Balance.MedianDivisor];
        float floor = ModSettings.MinPartySize * Balance.MaxPartySizeFloorMultiplier;
        CalculatedMaxPartySize = Math.Max(floor, Math.Max(medianSize, Math.Max(1, MobileParty.MainParty.MemberRoster.TotalManCount) * variance));
        float nonMilitiaGlobalPower = 0f;
        for (int i = 0; i < allLordsParties.Count; i++)
            nonMilitiaGlobalPower += allLordsParties[i].Party.EstimatedStrength;
        CalculatedGlobalPowerLimit = nonMilitiaGlobalPower * variance;
        MilitiaPowerPercent = nonMilitiaGlobalPower > 0 ? (GlobalMilitiaPower / nonMilitiaGlobalPower) * Balance.PercentDivisor : 0;
        allLordsParties.Clear();
    }
}