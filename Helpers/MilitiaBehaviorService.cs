using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.TwoDimension;
using static BanditMilitias.Globals;
using static BanditMilitias.Helper;

namespace BanditMilitias
{
    internal static class MilitiaBehaviorService
    {
        internal static void BMThink(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty?.Ai is null || mobileParty.Ai.IsDisabled || mobileParty.Ai.DoNotMakeNewDecisions || !mobileParty.IsBM())
                    return;

                if (mobileParty.HasNavalNavigationCapability)
                    return;

                Settlement target;
                switch (mobileParty.DefaultBehavior)
                {
                    case AiBehavior.None:
                    case AiBehavior.Hold:
                        if (mobileParty.TargetSettlement is null)
                        {
                            var locData = Settlement.StartFindingLocatablesAroundPosition(mobileParty.Position.ToVec2(), SettlementFindRange);
                            var candidates = new List<Settlement>();
                            for (var s = Settlement.FindNextLocatable(ref locData); s != null; s = Settlement.FindNextLocatable(ref locData))
                            {
                                if (!s.IsHideout)
                                    candidates.Add(s);
                            }
                            if (candidates.Count > 0)
                                mobileParty.SetMovePatrolAroundSettlement(candidates.GetRandomElement(), MobileParty.NavigationType.Default, false);
                        }
                        break;

                    case AiBehavior.GoToSettlement:
                        if (mobileParty.TargetSettlement?.IsHideout == true)
                        {
                            if (!mobileParty.IsEngaging && mobileParty.Position.ToVec2().DistanceSquared(mobileParty.TargetSettlement.Position.ToVec2()) == 0f)
                                mobileParty.SetMoveModeHold();
                        }
                        break;

                    case AiBehavior.PatrolAroundPoint:
                        var BM = mobileParty.GetBM();
                        if (BM is null || mobileParty.MapFaction is null)
                            return;

                        if (Globals.Settings?.AllowPillaging == true
                            && mobileParty.LeaderHero is not null
                            && mobileParty.Party.EstimatedStrength > MilitiaPartyAveragePower
                            && MBRandom.RandomFloat < (Globals.Settings?.PillagingChance ?? 0) * 0.01f)
                        {
                            var raidingCount = PowerCalculationService.GetCachedBMs().CountQ(m => m?.MobileParty?.ShortTermBehavior is AiBehavior.RaidSettlement);

                            if (raidingCount >= RaidCap)
                                break;

                            try
                            {
                                target = Globals.Villages
                                    .WhereQ(s => s.Village is not { VillageState: Village.VillageStates.BeingRaided or Village.VillageStates.Looted }
                                        && s.Owner is not null
                                        && s.MapFaction?.IsAtWarWith(mobileParty.MapFaction) != false
                                        && s.GetValue() > 0)
                                    .OrderByQ(s => s.GatePosition.ToVec2().DistanceSquared(mobileParty.Position.ToVec2()))
                                    .FirstOrDefault();
                            }
                            catch (Exception ex)
                            {
                                Helper.Trash(mobileParty);
                                return;
                            }

                            if (target is not null && target.OwnerClan is not null && BM.Avoidance is not null)
                            {
                                foreach (var clanHero in target.OwnerClan.Heroes)
                                {
                                    if (clanHero is not null
                                        && BM.Avoidance.TryGetValue(clanHero, out var avoidanceValue)
                                        && MBRandom.RandomFloat * 100f <= avoidanceValue)
                                    {
                                        target = null;
                                        break;
                                    }
                                }

                                if (target is null)
                                    break;
                            }

                            if (target.OwnerClan == Hero.MainHero.Clan)
                                InformationManager.DisplayMessage(new InformationMessage($"{mobileParty.Name} is raiding your village {target.Name} near {target.Town?.Name}!"));

                            mobileParty.SetMoveRaidSettlement(target, MobileParty.NavigationType.Default);
                            mobileParty.Ai.SetDoNotMakeNewDecisions(true);
                        }
                        break;
                }
            }
            catch (Exception ex) { }
        }

        internal static void TryGrowing(MobileParty mobileParty)
        {
            if (Globals.Settings.GrowthPercent > 0
                && MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent
                && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                && mobileParty.MapEvent is null
                && MilitiaPartyFactory.IsAvailableBanditParty(mobileParty)
                && MBRandom.RandomFloat <= Globals.Settings.GrowthChance / 100f)
            {
                var eligibleToGrow = mobileParty.MemberRoster.GetTroopRoster().WhereQ(rosterElement =>
                        rosterElement.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !rosterElement.Character.IsHero
                        && !mobileParty.IsVisible
                        && !rosterElement.Character.Name.ToString().StartsWith("Glorious"))
                    .ToListQ();

                if (!eligibleToGrow.Any())
                    return;

                var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthPercent / 100f;

                var boost = GlobalMilitiaPower > 0 ? CalculatedGlobalPowerLimit / GlobalMilitiaPower : 1;
                growthAmount += Globals.Settings.GlobalPowerPercent / 100f * boost;
                growthAmount = Mathf.Clamp(growthAmount, 1, 50);

                for (var i = 0; i < growthAmount && mobileParty.MemberRoster.TotalManCount + 1 < CalculatedMaxPartySize; i++)
                {
                    if (eligibleToGrow.Count == 0)
                        break;

                    var rosterElement = eligibleToGrow.GetRandomElement();
                    if (rosterElement.Character is null)
                        continue;

                    var troop = rosterElement.Character;
                    if (GlobalMilitiaPower + troop.GetPower() < CalculatedGlobalPowerLimit)
                        mobileParty.MemberRoster.AddToCounts(troop, 1);
                }

                EquipmentPool.AdjustCavalryCount(mobileParty.MemberRoster);
                PowerCalculationService.DoPowerCalculations();
            }
        }

        internal static void AdjustAvoidance(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty?.Position is null)
                    return;

                foreach (var BM in PowerCalculationService.GetCachedBMs(false).WhereQ(bm => bm?.Leader is not null
                    && bm.MobileParty?.Position is not null
                    && bm.MobileParty.Position.ToVec2().DistanceSquared(mobileParty.Position.ToVec2()) < Globals.AdjustRadiusSq))
                {
                    if (BM?.Avoidance is null)
                        continue;

                    var avoidanceKeysCopy = BM.Avoidance.Keys.ToListQ();
                    foreach (var heroKey in avoidanceKeysCopy)
                    {
                        if (heroKey is null || !BM.Avoidance.ContainsKey(heroKey))
                            continue;

                        BM.Avoidance[heroKey] = Math.Max(0, BM.Avoidance[heroKey] - Globals.AvoidanceIncreaseMin);
                    }
                }
            }
            catch (Exception ex) { }
        }

        internal static void SpawnBM()
        {
            if (!Globals.Settings.MilitiaSpawn)
                return;

            try
            {
                var validHideouts = Helper.FindHideoutsAwayFromMainParty();

                if (validHideouts.Count == 0)
                    return;

                var maxIterations = (int)Math.Max(0, (Globals.Settings.GlobalPowerPercent - MilitiaPowerPercent) / 24f);
                maxIterations = Math.Min(maxIterations, SpawnLoopSafetyLimit);

                var landHideouts = validHideouts
                    .WhereQ(s => Helper.IsLandHideout(s))
                    .ToListQ();

                var landMilitiaCountByClan = PowerCalculationService.GetCachedBMs()
                    .WhereQ(bm => bm?.MobileParty?.ActualClan is not null)
                    .GroupBy(bm => bm.MobileParty.ActualClan)
                    .ToDictionary(group => group.Key, group => group.Count());

                for (var i = 0; i < maxIterations; i++)
                {
                    if (MilitiaPowerPercent + 1 > Globals.Settings.GlobalPowerPercent)
                        break;

                    if (MBRandom.RandomInt(0, 101) > Globals.Settings.SpawnChance)
                        continue;

                    var baseSettlement = validHideouts.GetRandomElement();
                    if (baseSettlement is null)
                        continue;

                    var banditClan = Clan.BanditFactions?.FirstOrDefault(c => c.Culture == baseSettlement.Culture)
                        ?? Clan.BanditFactions?.FirstOrDefault()
                        ?? baseSettlement.OwnerClan;

                    if (banditClan?.HasNavalNavigationCapability == true)
                        continue;

                    if (!Globals.Settings.SpawnLandMilitias)
                        continue;

                    if (landHideouts.Count == 0)
                        continue;

                    var settlement = landHideouts.GetRandomElement();

                    var min = Convert.ToInt32(Globals.Settings.MinPartySize);
                    var max = Convert.ToInt32(CalculatedMaxPartySize);

                    if (max < min)
                        max = min;

                    var roster = TroopRoster.CreateDummyTroopRoster();
                    var size = Convert.ToInt32(MBRandom.RandomInt(min, max + 1) / 2f);
                    var foot = MBRandom.RandomInt(40, 61);
                    var range = MBRandom.RandomInt(20, MBRandom.RandomInt(35, 100 - foot) + 1);
                    var horse = 100 - foot - range;

                    if (Globals.BasicCavalry.Count == 0)
                    {
                        foot += horse % 2 == 0 ? horse / 2 : horse / 2 + 1;
                        range += horse / 2;
                        horse = 0;
                    }

                    var formation = new List<int> { foot, range, horse };

                    for (var index = 0; index < formation.Count; index++)
                    {
                        var troopList = index switch
                        {
                            0 => Globals.BasicInfantry,
                            1 => Globals.BasicRanged,
                            2 => Globals.BasicCavalry,
                            _ => null
                        };

                        if (troopList?.Count == 0)
                            continue;

                        for (var c = 0; c < formation[index] * size / 100f; c++)
                        {
                            var troop = troopList?.GetRandomElement();
                            if (troop is not null)
                                roster.AddToCounts(troop, 1);
                        }
                    }

                    if (roster.TotalManCount == 0)
                        continue;

                    int clanCap = Globals.Settings.MaxLandPartiesPerClan;

                    if (clanCap > 0)
                    {
                        landMilitiaCountByClan.TryGetValue(banditClan, out var partiesForClan);

                        if (partiesForClan >= clanCap)
                            continue;
                    }

                    var banditMilitia = MobileParty.CreateParty("Bandit_Militia",
                        new ModBanditMilitiaPartyComponent(settlement, null, banditClan));

                    if (banditMilitia is null)
                        continue;

                    try
                    {
                        MilitiaPartyFactory.InitMilitia(banditMilitia, new[] { roster, TroopRoster.CreateDummyTroopRoster() }, settlement.GatePosition);
                    }
                    catch (Exception ex)
                    {
                        Helper.Trash(banditMilitia);
                        continue;
                    }

                    banditMilitia.DesiredAiNavigationType = MobileParty.NavigationType.Default;

                    PowerCalculationService.DoPowerCalculations();
                    if (banditClan is not null)
                    {
                        landMilitiaCountByClan.TryGetValue(banditClan, out var currentClanCount);
                        landMilitiaCountByClan[banditClan] = currentClanCount + 1;
                    }

                    if (Globals.Settings?.TestingMode == true)
                        TeleportMilitiasNearPlayer(banditMilitia);
                }
            }
            catch (Exception ex) { }
        }

        internal static void TeleportMilitiasNearPlayer(MobileParty banditMilitia)
        {
            try
            {
                MobileParty targetParty = null;

                if (Hero.MainHero?.PartyBelongedTo is not null)
                    targetParty = Hero.MainHero.PartyBelongedTo;

                if (targetParty is null && Hero.MainHero?.PartyBelongedToAsPrisoner?.MobileParty is not null)
                    targetParty = Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;

                if (targetParty?.Position == null)
                    return;

                banditMilitia.Position = targetParty.Position;
            }
            catch (Exception ex) { }
        }
    }
}