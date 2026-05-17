using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;

namespace BanditMilitias
{
    internal static class PowerCalculationService
    {
        internal static float CalculatedMaxPartySize { get; private set; }
        internal static float CalculatedGlobalPowerLimit { get; private set; }
        internal static float GlobalMilitiaPower { get; private set; }
        internal static float MilitiaPowerPercent { get; private set; }
        internal static float MilitiaPartyAveragePower { get; private set; }

        private static double _lastCalculated;
        private static double _partyCacheInterval;
        private static List<ModBanditMilitiaPartyComponent> _allBMs = new();

        private static float Variance => MBRandom.RandomFloatRanged(0.925f, 1.075f);

        internal static void Reset()
        {
            _lastCalculated = 0;
            _partyCacheInterval = 0;
            _allBMs = new List<ModBanditMilitiaPartyComponent>();
            CalculatedMaxPartySize = 0;
            CalculatedGlobalPowerLimit = 0;
            GlobalMilitiaPower = 0;
            MilitiaPowerPercent = 0;
            MilitiaPartyAveragePower = 0;
        }

        internal static IReadOnlyList<ModBanditMilitiaPartyComponent> GetCachedBMs(bool forceRefresh = false)
        {
            if (forceRefresh || _partyCacheInterval < CampaignTime.Now.ToHours - 1)
            {
                _partyCacheInterval = CampaignTime.Now.ToHours;
                _allBMs = MobileParty.AllBanditParties
                    .WhereQ(m => m != null && m.IsActive && m.IsBM())
                    .SelectQ(m => m.PartyComponent as ModBanditMilitiaPartyComponent)
                    .WhereQ(c => c != null)
                    .ToListQ();
            }

            return _allBMs;
        }

        internal static void DoPowerCalculations(bool force = false)
        {
            if (!force && _lastCalculated >= CampaignTime.Now.ToHours - 8)
                return;

            var cachedBMs = GetCachedBMs(true);
            GlobalMilitiaPower = cachedBMs.SumQ(m => m.Party.EstimatedStrength);
            var bmCount = cachedBMs.Count;
            MilitiaPartyAveragePower = bmCount > 0 ? GlobalMilitiaPower / bmCount : 0;

            var parties = MobileParty.All
                .WhereQ(p => p.LeaderHero is not null && !p.IsBM())
                .ToListQ();

            if (parties.Count == 0)
            {
                MilitiaPowerPercent = 0;
                return;
            }

            _lastCalculated = CampaignTime.Now.ToHours;

            var variance = Variance;
            var medianSize = (float)parties
                .OrderBy(p => p.MemberRoster.TotalManCount)
                .ElementAt(parties.Count / 2)
                .MemberRoster.TotalManCount;

            CalculatedMaxPartySize = Math.Max(
                medianSize,
                Math.Max(1, MobileParty.MainParty.MemberRoster.TotalManCount) * variance);

            CalculatedGlobalPowerLimit = parties.SumQ(p => p.Party.EstimatedStrength) * variance;

            MilitiaPowerPercent = CalculatedGlobalPowerLimit > 0
                ? GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100f
                : 0;
        }
    }
}