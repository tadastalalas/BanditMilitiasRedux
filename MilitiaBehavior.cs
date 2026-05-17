using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Party.PartyComponents;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.TwoDimension;
using static BanditMilitias.Helper;
using static BanditMilitias.Globals;

namespace BanditMilitias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, village =>
            {
                try
                {
                    if (village?.Settlement?.Party?.MapEvent is null)
                        return;

                    var attackers = village.Settlement.Party.MapEvent.PartiesOnSide(BattleSideEnum.Attacker).ToListQ();
                    PartyBase party = attackers.FirstOrDefaultQ(a => a.Party.IsMobile && a.Party.MobileParty.IsBM())?.Party;

                    if (party is null)
                        return;
                    if (party?.MobileParty is null)
                        return;

                    if (Globals.Settings.ShowRaids && village.Owner?.LeaderHero == Hero.MainHero)
                    {
                        InformationManager.DisplayMessage(new InformationMessage($"{village.Name} is being raided by {party.Name}!"));
                    }
                }
                catch (Exception ex) { }
            });
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, (_, m) =>
            {
                try
                {

                    if (m?.AttackerSide?.Parties is null || !m.AttackerSide.Parties.AnyQ(mep => mep.Party.IsMobile && mep.Party.MobileParty.IsBM()))
                        return;

                    PartyBase party = m.AttackerSide.Parties.FirstOrDefaultQ(mep => mep.Party.IsMobile && mep.Party.MobileParty.IsBM())?.Party;
                    if (party is null)
                        return;

                    if (party.MobileParty?.Ai is not null)
                    {
                        party.MobileParty.Ai.SetDoNotMakeNewDecisions(false);
                        party.MobileParty.SetMoveModeHold();
                    }

                    if (Globals.Settings.ShowRaids)
                    {
                        try
                        {
                            var raidedSettlement = m.MapEventSettlement;
                            var anchorPos = raidedSettlement?.GatePosition.ToVec2() ?? MobileParty.MainParty.Position.ToVec2();
                            Settlement nearestTown = null;
                            var nearestDist = float.MaxValue;
                            foreach (var s in Settlement.All)
                            {
                                if (s?.IsTown != true) continue;
                                var d = s.GatePosition.ToVec2().DistanceSquared(anchorPos);
                                if (d < nearestDist)
                                {
                                    nearestDist = d;
                                    nearestTown = s;
                                }
                            }

                            var townName = nearestTown?.Name?.ToString() ?? "(Unknown Town)";
                            InformationManager.DisplayMessage(
                                new InformationMessage($"{raidedSettlement?.Name} raided!  " +
                                                       $"{party.Name} is fat with loot near {townName}!"));
                        }
                        catch (Exception ex) { }
                    }
                }
                catch (Exception ex) {  }
            });

            CampaignEvents.TickPartialHourlyAiEvent.AddNonSerializedListener(this, TickPartialHourlyAiEvent);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickPartyEvent);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, SpawnBM);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, MobilePartyDestroyed);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, DailyTick);
        }

        private void DailyTick()
        {
            if (Heroes is null)
                return;

            var strayHeroes = Heroes
                .WhereQ(h => h != null
                    && h.PartyBelongedTo is null
                    && h.PartyBelongedToAsPrisoner is null
                    && !h.IsPrisoner)
                .ToArrayQ();

            foreach (Hero hero in strayHeroes)
            {
                try
                {
                    KillCharacterAction.ApplyByRemove(hero);
                }
                catch (Exception) { }
            }

            var imprisonedHeroes = Heroes
                .WhereQ(h => h != null
                && h.IsPrisoner
                && h.CurrentSettlement is not null
                && h.CurrentSettlement.OwnerClan != Clan.PlayerClan)
                .ToArrayQ();

            foreach (Hero hero in imprisonedHeroes)
            {
                try
                {
                    if (MBRandom.RandomFloat < 0.5f)
                        continue;

                    KillCharacterAction.ApplyByRemove(hero);
                }
                catch (Exception) { }
            }
        }

        private static void MobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyer)
        {
            try
            {
                StuckTracker.Remove(mobileParty);

                if (mobileParty?.IsBM() != true || destroyer?.LeaderHero is null)
                    return;

                int AvoidanceIncrease() => MBRandom.RandomInt(AvoidanceIncreaseMin, AvoidanceIncreaseMax);
                var destroyerBM = destroyer.MobileParty?.GetBM();

                if (destroyerBM?.Avoidance != null && mobileParty.LeaderHero != null)
                    destroyerBM?.Avoidance?.Remove(mobileParty.LeaderHero);

                var cachedBMs = PowerCalculationService.GetCachedBMs();
                if (cachedBMs == null)
                    return;

                var destroyedPos2D = mobileParty.Position.ToVec2();
                foreach (var BM in cachedBMs.WhereQ(bm =>
                             bm?.MobileParty != null
                             && bm.MobileParty.Position.ToVec2().DistanceSquared(destroyedPos2D) < AvoidanceEffectRadiusSq))
                {
                    if (BM?.Avoidance is null) continue;

                    var delta = AvoidanceIncrease();
                    if (BM.Avoidance.TryGetValue(destroyer.LeaderHero, out var current))
                        BM.Avoidance[destroyer.LeaderHero] = current + delta;
                    else
                        BM.Avoidance.Add(destroyer.LeaderHero, delta);
                }
            }
            catch (Exception) { }
        }

        private static void TickPartialHourlyAiEvent(MobileParty mobileParty)
        {
            if (mobileParty?.IsActive != true)
                return;

            if (mobileParty.PartyComponent is not (BanditPartyComponent or ModBanditMilitiaPartyComponent))
                return;

            if (mobileParty.MemberRoster.TotalManCount < Globals.Settings.MergeableSize)
                return;

            if (mobileParty.IsUsedByAQuest())
                return;

            if (mobileParty.PartyComponent is BanditPartyComponent)
            {
                if ((mobileParty.CurrentSettlement is not null
                    && mobileParty.Ai is not null && mobileParty.Ai.AiBehaviorInteractable is PartyBase pb1 && pb1.Settlement is { IsHideout: true })
                    || mobileParty.Ai is not null && mobileParty.Ai.AiBehaviorInteractable is PartyBase pb2 && pb2.MobileParty is { IsCaravan: true })
                    return;
            }

            if (mobileParty.MapEvent is not null)
                return;

            if (mobileParty.IsBM()
                && !mobileParty.IsCurrentlyAtSea
                && mobileParty.Ai?.IsDisabled == false
                && mobileParty.Ai?.DoNotMakeNewDecisions == false)
            {
                var currentPos2D = mobileParty.Position.ToVec2();

                if (StuckTracker.TryGetValue(mobileParty, out var stuckState))
                {
                    if (currentPos2D.DistanceSquared(stuckState.LastPos) < Globals.StuckDistanceThresholdSq)
                    {
                        var newCount = stuckState.HourCount + 1;
                        StuckTracker[mobileParty] = (stuckState.LastPos, newCount);

                        if (newCount >= Globals.StuckHourLimit)
                        {
                            StuckTracker.Remove(mobileParty);

                            var escapePool = Hideouts
                                .WhereQ(s => s != null
                                    && s.GatePosition.ToVec2().DistanceSquared(currentPos2D) > Globals.EscapeMinDistanceSq)
                                .ToListQ();

                            if (escapePool.Count == 0)
                                escapePool = Settlement.All
                                    .WhereQ(s => s != null
                                        && !s.IsTown && !s.IsCastle
                                        && s.GatePosition.ToVec2().DistanceSquared(currentPos2D) > Globals.EscapeMinDistanceSq)
                                    .ToListQ();

                            if (escapePool.Count > 0)
                            {
                                var escapeTarget = escapePool.GetRandomElement();
                                mobileParty.SetMoveGoToPoint(escapeTarget.GatePosition, MobileParty.NavigationType.Default);
                                return;
                            }
                        }
                    }
                    else
                    {
                        StuckTracker[mobileParty] = (currentPos2D, 0);
                    }
                }
                else
                {
                    StuckTracker[mobileParty] = (currentPos2D, 0);
                }
            }

            MobileParty mergeTarget = null;
            bool isBM = mobileParty.IsBM();

            if (mobileParty.HasNavalNavigationCapability)
                return;

            if (isBM && !Globals.Settings.SpawnLandMilitias)
            {
                MilitiaBehaviorService.BMThink(mobileParty);
                return;
            }

            try
            {
                if (isBM)
                {
                    if (mobileParty.LeaderHero is null && mobileParty.MemberRoster.TotalHeroes > 0)
                    {
                        var heroRoster = mobileParty.MemberRoster.GetTroopRoster()
                            .WhereQ(t => t.Character?.IsHero == true)
                            .ToListQ();

                        if (heroRoster.Count > 0)
                        {
                            var leader = heroRoster
                            .OrderByQ(t => -(t.Character.HeroObject?.Power ?? 0))
                            .First().Character.HeroObject;

                            if (leader != null)
                            {
                                mobileParty.ChangePartyLeader(leader);
                                mobileParty.SetMoveModeHold();
                            }
                        }
                        return;
                    }
                }

                if (isBM
                    && mobileParty.DefaultBehavior == AiBehavior.EngageParty
                    && mobileParty.TargetParty is not null
                    && !FactionManager.IsAtWarAgainstFaction(mobileParty.MapFaction, mobileParty.TargetParty.MapFaction))
                {
                    if (mobileParty.TargetParty.DefaultBehavior != AiBehavior.EngageParty ||
                        mobileParty.TargetParty.TargetParty != mobileParty)
                    {
                        mobileParty.SetMoveModeHold();
                    }
                }

                if (isBM && mobileParty.Ai?.DoNotMakeNewDecisions == true && mobileParty.DefaultBehavior != AiBehavior.RaidSettlement)
                {
                    mobileParty.Ai.SetDoNotMakeNewDecisions(false);
                    mobileParty.SetMoveModeHold();
                    MilitiaBehaviorService.BMThink(mobileParty);
                    return;
                }

                if (isBM)
                {
                    var locatableSearchData = Settlement.StartFindingLocatablesAroundPosition(mobileParty.Position.ToVec2(), MinDistanceFromHideout);
                    for (Settlement settlement =
                             Settlement.FindNextLocatable(ref locatableSearchData);
                         settlement != null;
                         settlement =
                             Settlement.FindNextLocatable(ref locatableSearchData))
                    {
                        if (!settlement.IsHideout) continue;
                        MilitiaBehaviorService.BMThink(mobileParty);
                        return;
                    }
                }

                if (isBM)
                {
                    var bm = mobileParty.GetBM();
                    if (bm?.LastMergedOrSplitDate != null
                        && CampaignTime.Now < bm.LastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                    {
                        MilitiaBehaviorService.BMThink(mobileParty);
                        return;
                    }
                }

                if (MobileParty.MainParty.ShortTermBehavior == AiBehavior.EngageParty
                    && MobileParty.MainParty.ShortTermTargetParty == mobileParty)
                {
                    if (isBM)
                        MilitiaBehaviorService.BMThink(mobileParty);
                    return;
                }

                List<MobileParty> nearbyBandits = new List<MobileParty>();
                {
                    var locatableSearchData = MobileParty.StartFindingLocatablesAroundPosition(mobileParty.Position.ToVec2(), FindRadius);
                    for (MobileParty party =
                             MobileParty.FindNextLocatable(ref locatableSearchData);
                         party != null;
                         party =
                             MobileParty.FindNextLocatable(ref locatableSearchData))
                    {
                        if (party == mobileParty)
                            continue;

                        if (party.HasNavalNavigationCapability)
                            continue;

                        if (party.IsBandit && party.MapEvent is null &&
                            party.MemberRoster.TotalManCount > Globals.Settings.MergeableSize &&
                            mobileParty.MemberRoster.TotalManCount + party.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize &&
                            MilitiaPartyFactory.IsAvailableBanditParty(party))
                        {
                            nearbyBandits.Add(party);
                        }
                    }
                }

                if (nearbyBandits.Count == 0)
                {
                    MilitiaBehaviorService.BMThink(mobileParty);
                    return;
                }

                int mobilePartyMountedCount = EquipmentPool.NumMountedTroops(mobileParty.MemberRoster);
                var myPos = mobileParty.Position.ToVec2();
                nearbyBandits.Sort((a, b) => a.Position.ToVec2().DistanceSquared(myPos).CompareTo(b.Position.ToVec2().DistanceSquared(myPos)));

                var mountedCountByParty = new Dictionary<MobileParty, int>();

                foreach (var target in nearbyBandits)
                {
                    var militiaTotalCount = mobileParty.MemberRoster.TotalManCount + target.MemberRoster.TotalManCount;
                    if (militiaTotalCount < Globals.Settings.MinPartySize || militiaTotalCount > CalculatedMaxPartySize)
                        continue;

                    if (mobileParty.MapFaction is not null && target.MapFaction?.IsAtWarWith(mobileParty.MapFaction) == true)
                        continue;

                    if (target.IsBM())
                    {
                        var targetBM = target.GetBM();
                        if (targetBM?.LastMergedOrSplitDate != null
                            && CampaignTime.Now < targetBM.LastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings.CooldownHours))
                            continue;
                    }

                    if (!mountedCountByParty.TryGetValue(target, out var targetMountedCount))
                    {
                        targetMountedCount = EquipmentPool.NumMountedTroops(target.MemberRoster);
                        mountedCountByParty[target] = targetMountedCount;
                    }

                    if (mobilePartyMountedCount + targetMountedCount > militiaTotalCount / 2)
                        continue;

                    if (MobileParty.MainParty.ShortTermBehavior == AiBehavior.EngageParty
                        && MobileParty.MainParty.ShortTermTargetParty == target)
                        continue;

                    mergeTarget = target;
                    break;
                }

                if (mergeTarget is null)
                {
                    MilitiaBehaviorService.BMThink(mobileParty);
                    return;
                }

                if (Campaign.Current?.Models?.MapDistanceModel is not null)
                {
                    var distanceSq = mobileParty.Position.ToVec2().DistanceSquared(mergeTarget.Position.ToVec2());
                    if (distanceSq <= Globals.MergeDistanceSq)
                    {
                        MilitiaPartyFactory.TryMergeParties(mobileParty, mergeTarget);
                    }
                    else if (mobileParty.TargetParty != mergeTarget)
                    {
                        mobileParty.SetMoveEngageParty(mergeTarget, MobileParty.NavigationType.Default);
                        mergeTarget.SetMoveEngageParty(mobileParty, MobileParty.NavigationType.Default);
                    }
                }
            }
            catch (Exception)
            {
                try { Trash(mobileParty); } catch { }
                if (mergeTarget is not null)
                {
                    try { Trash(mergeTarget); } catch { }
                }
            }
        }

        private static void DailyTickPartyEvent(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty.IsBM())
                {
                    if ((int)CampaignTime.Now.ToWeeks % CampaignTime.DaysInWeek == 0 && Globals.Settings.AllowPillaging)
                        AdjustAvoidance(mobileParty);

                    TryGrowing(mobileParty);
                    if (MBRandom.RandomFloat <= Globals.Settings.TrainingChance * 0.01f)
                        MilitiaPartyFactory.TrainMilitia(mobileParty);

                    MilitiaPartyFactory.TrySplitParty(mobileParty);
                }
            }
            catch (Exception) { }
        }

        private static void AdjustAvoidance(MobileParty mobileParty)
            => MilitiaBehaviorService.AdjustAvoidance(mobileParty);

        private static void TryGrowing(MobileParty mobileParty)
            => MilitiaBehaviorService.TryGrowing(mobileParty);

        private static void SpawnBM()
            => MilitiaBehaviorService.SpawnBM();

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("Heroes", ref Heroes);
            if (dataStore.IsLoading)
            {
                Heroes ??= new List<Hero>();
                var aliveSet = new HashSet<Hero>(Hero.AllAliveHeroes);
                Globals.Heroes.RemoveAll(hero => hero is null || !aliveSet.Contains(hero));
            }
        }
    }
}