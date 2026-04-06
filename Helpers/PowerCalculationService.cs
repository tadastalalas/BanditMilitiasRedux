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
    /// <summary>
    /// Single-responsibility service that owns all power/size calculations and
    /// the BM party cache. Nothing else should mutate these values directly.
    /// </summary>
    internal static class PowerCalculationService
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<SubModule>();

        // ── Calculated state ─────────────────────────────────────────────────────

        internal static float CalculatedMaxPartySize { get; private set; }
        internal static float CalculatedGlobalPowerLimit { get; private set; }
        internal static float GlobalMilitiaPower { get; private set; }
        internal static float MilitiaPowerPercent { get; private set; }
        internal static float MilitiaPartyAveragePower { get; private set; }

        // ── BM party cache ───────────────────────────────────────────────────────

        private static double _lastCalculated;
        private static double _partyCacheInterval;
        private static List<ModBanditMilitiaPartyComponent> _allBMs = new();

        // ── Variance (kept here because it feeds directly into calculations) ─────

        private static float Variance => MBRandom.RandomFloatRanged(0.925f, 1.075f);

        // ── Public API ───────────────────────────────────────────────────────────

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

        /// <summary>
        /// Returns the cached BM component list, refreshing it when stale or forced.
        /// The returned list is a snapshot — safe to iterate even if parties are
        /// added/removed mid-tick.
        /// </summary>
        internal static List<ModBanditMilitiaPartyComponent> GetCachedBMs(bool forceRefresh = false)
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

        /// <summary>
        /// Recalculates all power and size caps.
        /// <para>
        /// Bug fix: the BM cache and BM-derived values (MilitiaPowerPercent,
        /// MilitiaPartyAveragePower) are always updated even when no non-BM
        /// parties exist, so they never reflect stale data from a previous call.
        /// </para>
        /// </summary>
        internal static void DoPowerCalculations(bool force = false)
        {
            if (!force && _lastCalculated >= CampaignTime.Now.ToHours - 8)
                return;

            // Always refresh the BM cache and BM-derived figures so they are
            // never left as stale values from a previous successful calculation.
            var cachedBMs = GetCachedBMs(true);
            GlobalMilitiaPower = cachedBMs.SumQ(m => m.Party.EstimatedStrength);
            var bmCount = cachedBMs.Count;
            MilitiaPartyAveragePower = bmCount > 0 ? GlobalMilitiaPower / bmCount : 0;

            var parties = MobileParty.All
                .WhereQ(p => p.LeaderHero is not null && !p.IsBM())
                .ToListQ();

            if (parties.Count == 0)
            {
                // No world parties yet (early load). Leave size/limit at their
                // last known values rather than zeroing them, which would make
                // every downstream guard (spawn, grow, split) fire incorrectly.
                Logger.LogWarning("DoPowerCalculations: no non-BM parties found, skipping size/limit recalculation.");
                MilitiaPowerPercent = 0;
                return;
            }

            _lastCalculated = CampaignTime.Now.ToHours;

            var variance = Variance; // sample once — it's random, don't call twice
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