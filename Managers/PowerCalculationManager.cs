using System;
using System.Collections.Generic;
using BanditMilitiasRedux.Constructors;
using BanditMilitiasRedux.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;

namespace BanditMilitiasRedux.Managers
{
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

        private static float Variance => MBRandom.RandomFloatRanged(0.925f, 1.075f);

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

        internal static IReadOnlyList<BanditMilitiaPartyComponent?> GetActiveBanditMilitiaParties(bool forceRefresh = false)
        {
            if (!forceRefresh && !(_lastCacheUpdateHour < CampaignTime.Now.ToHours - 1)) // No more frequent than 1 hour.
                return _allBanditMilitias;
            
            _lastCacheUpdateHour = CampaignTime.Now.ToHours;
            _allBanditMilitias = MobileParty.AllBanditParties.WhereQ(party => party is { IsActive: true } && party.IsBanditMilitiaParty())
                .SelectQ(mobileParty => mobileParty.PartyComponent as BanditMilitiaPartyComponent)
                .WhereQ(partyComponent => partyComponent != null).ToListQ();

            return _allBanditMilitias;
        }

        internal static void DoPowerCalculations(bool force = false)
        {
            if (!force && _lastCalculated >= CampaignTime.Now.ToHours - 20) // Every 20 hours.
                return;
            
            var activeBanditMilitiaParties = GetActiveBanditMilitiaParties(true);
            GlobalMilitiaPower = activeBanditMilitiaParties.SumQ(partyComponent => (int)partyComponent.Party.EstimatedStrength);
            int militiasCount = activeBanditMilitiaParties.Count;
            MilitiaPartyAveragePower = militiasCount > 0 ? GlobalMilitiaPower / militiasCount : 0;
            List<MobileParty> allLordsParties = MobileParty.All.WhereQ(mobileParty => mobileParty.LeaderHero is not null && !mobileParty.IsBanditMilitiaParty()).ToListQ();
            
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
            var medianSize = (float)counts[counts.Length / 2];
            float floor = Globals.Settings.MinPartySize * 2f;
            CalculatedMaxPartySize = Math.Max(floor, Math.Max(medianSize, Math.Max(1, MobileParty.MainParty.MemberRoster.TotalManCount) * variance));
            float nonMilitiaGlobalPower = allLordsParties.SumQ(mobileParty => mobileParty.Party.EstimatedStrength);
            CalculatedGlobalPowerLimit = nonMilitiaGlobalPower * variance;
            MilitiaPowerPercent = nonMilitiaGlobalPower > 0 ? (GlobalMilitiaPower / nonMilitiaGlobalPower) * 100f : 0;
        }
    }
}