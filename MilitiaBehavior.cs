using System;
using System.Collections.Generic;
using System.Linq;
using Helpers;
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
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<MilitiaBehavior>();

        internal const float Increment = 5;
        private const int AdjustRadius = 50;
        private const int SettlementFindRange = 200;
        private const int SpawnLoopSafetyLimit = 100;

        public override void RegisterEvents()
        {
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, village =>
            {
                try
                {
                    if (village?.Settlement?.Party?.MapEvent is null
                        || !village.Settlement.Party.MapEvent.PartiesOnSide(BattleSideEnum.Attacker)
                            .AnyQ(m => m.Party.IsMobile && m.Party.MobileParty.IsBM()))
                        return;

                    var attackers = village.Settlement.Party.MapEvent.PartiesOnSide(BattleSideEnum.Attacker).ToListQ();
                    if (attackers.Count == 0)
                        return;

                    PartyBase party = attackers.FirstOrDefaultQ(a => a.Party.IsMobile && a.Party.MobileParty.IsBM())?.Party;
                    if (party?.MobileParty is null)
                        return;

                    Logger.LogDebug($"{party.Name}({party.MobileParty.StringId} is raiding {village.Name}.");

                    if (Globals.Settings?.ShowRaids == true && village.Owner?.LeaderHero == Hero.MainHero)
                    {
                        InformationManager.DisplayMessage(new InformationMessage($"{village.Name} is being raided by {party.Name}!"));
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in VillageBeingRaided event");
                }
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

                    Logger.LogDebug($"{party.Name}({party.MobileParty.StringId} has done raiding {m.MapEventSettlement?.Name}.");

                    if (party?.MobileParty?.Ai is not null)
                    {
                        party.MobileParty.Ai.SetDoNotMakeNewDecisions(false);
                        party.MobileParty.SetMoveModeHold();
                    }

                    if (Globals.Settings?.ShowRaids == true)
                    {
                        try
                        {
                            // FIX: Find nearest town to the RAIDED SETTLEMENT, not to player
                            var raidedSettlement = m.MapEventSettlement;
                            Settlement nearestTown = null;

                            if (raidedSettlement?.Position != null)
                            {
                                // Find town nearest to the raided village
                                nearestTown = Settlement.All
                                    .WhereQ(s => s?.IsTown == true && s.Position != null)
                                    .OrderByQ(s => s.Position.Distance(raidedSettlement.Position))
                                    .FirstOrDefault();
                            }

                            // Fallback to player's nearest town if above fails
                            if (nearestTown is null)
                            {
                                nearestTown = Settlement.All
                                    .WhereQ(s => s.IsTown)
                                    .OrderByQ(s => s.GatePosition.ToVec2().Distance(MobileParty.MainParty.Position.ToVec2()))
                                    .FirstOrDefault();
                            }

                            var townName = nearestTown?.Name?.ToString() ?? "(Unknown Town)";
                            InformationManager.DisplayMessage(
                                new InformationMessage($"{raidedSettlement?.Name} raided!  " +
                                                       $"{party.Name} is fat with loot near {townName}!"));
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Error displaying raid message: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, "Error in RaidCompletedEvent");
                }
            });

            CampaignEvents.TickPartialHourlyAiEvent.AddNonSerializedListener(this, TickPartialHourlyAiEvent);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, DailyTickPartyEvent);
            CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, SpawnBM);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, MobilePartyDestroyed);
            CampaignEvents.DailyTickEvent.AddNonSerializedListener(this, DailyTick);
            CampaignEvents.AiHourlyTickEvent.AddNonSerializedListener(this, AiHourlyTick);
        }

        /// <summary>
        /// Direct equivalent of <c>PiratesCampaignBehavior.AiHourlyTick</c>.
        /// Injects a naval patrol score so the engine's AI arbitration always
        /// keeps naval BMs at sea, even after merges or save/load cycles.
        /// </summary>
        private static void AiHourlyTick(MobileParty mobileParty, PartyThinkParams p)
        {
            if (mobileParty?.IsActive != true
                || !mobileParty.IsBM()
                || !mobileParty.HasNavalNavigationCapability)
                return;

            var bm = mobileParty.GetBM();
            if (bm == null)
                return;

            // BUG 2 FIX — after a merge the new party spawns at the merge point
            // which may not be classified as "at sea" yet, so IsCurrentlyAtSea is
            // false and NavalPatrolPosition may be stale / a land face.
            // Recompute NavalPatrolPosition from the current position whenever the
            // stored value is zero or the party is not yet moving.
            if (bm.NavalPatrolPosition == CampaignVec2.Zero
                || mobileParty.DefaultBehavior == AiBehavior.Hold
                || mobileParty.DefaultBehavior == AiBehavior.None)
            {
                var freshPos = NavigationHelper.FindPointAroundPosition(
                    mobileParty.Position, MobileParty.NavigationType.Naval, 20f, 0f, true, false);
                bm.NavalPatrolPosition = freshPos;
                mobileParty.SetMovePatrolAroundPoint(freshPos, MobileParty.NavigationType.Naval);
            }

            var patrolPos = bm.NavalPatrolPosition;

            // Identical to vanilla: PatrolAroundPoint + Naval + score 5f
            var behaviorData = new AIBehaviorData(
                patrolPos,
                AiBehavior.PatrolAroundPoint,
                MobileParty.NavigationType.Naval,
                false, false, false);

            var score = new ValueTuple<AIBehaviorData, float>(behaviorData, 5f);
            p.AddBehaviorScore(in score);
        }

        private void DailyTick()
        {
            // Guard: Ensure Heroes collection exists before processing
            if (Heroes is null)
            {
                Logger.LogWarning("Heroes collection is null in DailyTick");
                return;
            }

            // Cache the filtered collection to prevent modification-during-iteration errors
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
                    Logger.LogTrace($"Removing stray hero {hero.Name} from daily cleanup. HeroState: {hero.HeroState}");
                    KillCharacterAction.ApplyByRemove(hero);
                }
                catch (Exception ex)
                {
                    // Log but continue - one failed removal shouldn't stop the entire cleanup
                    Logger.LogError(ex, $"Failed to remove stray hero {hero?.Name}");
                }
            }

            // Cache imprisoned heroes to avoid collection modification issues
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
                    // Random 50% chance to skip this hero
                    if (MBRandom.RandomFloat < 0.5f)
                        continue;

                    Logger.LogTrace($"Removing imprisoned hero {hero.Name} from daily cleanup.");
                    KillCharacterAction.ApplyByRemove(hero);
                }
                catch (Exception ex)
                {
                    // Individual failure shouldn't crash entire cleanup
                    Logger.LogError(ex, $"Failed to remove imprisoned hero {hero?.Name}");
                }
            }
                
            if (Globals.Settings?.MinLogLevel?.SelectedValue <= LogLevel.Debug)
            {
                try
                {
                    var regularBandits = MobileParty.AllBanditParties?.CountQ(p => p != null && !p.IsBM()) ?? 0;
                    var bms = GetCachedBMs().ToListQ();
                    var militias = bms.Count;
                    var leaderless = bms.CountQ(c => c?.MobileParty?.LeaderHero is null);

                    Logger.LogDebug($"Day {CampaignTime.Now.GetDayOfYear} Report: {regularBandits} regular bandits, {militias} bandit militias, {leaderless} leaderless militias.");

                    // should be fixed
                    foreach (Hero hero in Heroes?.WhereQ(hero => hero.BattleEquipment[5].IsEmpty == true).ToArrayQ() ?? Array.Empty<Hero>())
                    {
                        try
                        {
                            var location = hero.PartyBelongedTo?.Name.ToString() ?? hero.CurrentSettlement?.Name.ToString() ?? "Unknown Town";
                            Logger.LogWarning($"Naked hero {hero.Name} at {location}");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Error logging naked hero: {ex.Message}");
                        }
                    }

                    // Check for missing clans
                    foreach (var c in AllBMs.WhereQ(m => m.MobileParty?.IsActive == true && (m.Clan is null || m.MobileParty.ActualClan is null)).ToArrayQ() ?? Array.Empty<ModBanditMilitiaPartyComponent>())
                    {
                        try
                        {
                            Logger.LogWarning($"{c.MobileParty.Name} ({c.MobileParty.StringId}) does not have a clan.");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Error checking clan: {ex.Message}");
                        }
                    }

                    // Check for missing factions
                    foreach (var c in AllBMs?.WhereQ(m => m?.MobileParty?.IsActive == true && m.MobileParty.MapFaction is null).ToArrayQ() ?? Array.Empty<ModBanditMilitiaPartyComponent>())
                    {
                        try
                        {
                            Logger.LogWarning($"{c.MobileParty.Name} ({c.MobileParty.StringId}) does not have a faction.");
                        }
                        catch (Exception ex)
                        {
                            Logger.LogWarning($"Error checking faction: {ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {

                    Logger.LogError(ex, "Error in debug logging section");
                }
            }
        }

        private static void MobilePartyDestroyed(MobileParty mobileParty, PartyBase destroyer)
        {
            try
            {
                StuckTracker.Remove(mobileParty);

                if (mobileParty?.IsBM() != true || destroyer?.LeaderHero is null)
                    return;

                int AvoidanceIncrease() => MBRandom.RandomInt(15, 35);

                // FIX: Guard GetBM() and Avoidance dictionary
                var destroyerBM = destroyer.MobileParty?.GetBM();

                // Guard: Ensure leader hero exists before dictionary operations
                if (destroyerBM?.Avoidance != null && mobileParty.LeaderHero != null)
                {
                    destroyerBM?.Avoidance.Remove(mobileParty.LeaderHero); 
                }

                // Guard: Cache and validate GetCachedBMs result
                var cachedBMs = GetCachedBMs();
                if (cachedBMs == null)
                {
                    Logger.LogWarning("GetCachedBMs returned null in MobilePartyDestroyed");
                    return;
                }

                foreach (var BM in cachedBMs.WhereQ(bm =>
                             bm?.MobileParty != null && bm.MobileParty.Position.ToVec2().Distance(mobileParty.Position.ToVec2()) < AvoidanceEffectRadius))
                {
                    if (BM?.Avoidance is null)
                        continue;

                    if (BM.Avoidance.TryGetValue(destroyer.LeaderHero, out _))
                        BM.Avoidance[destroyer.LeaderHero] += AvoidanceIncrease();
                    else
                        BM.Avoidance.Add(destroyer.LeaderHero, AvoidanceIncrease());
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in MobilePartyDestroyed event");
            }
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

            // they will evacuate hideouts and not chase caravans
            if (mobileParty.PartyComponent is BanditPartyComponent)
            {
                if ((mobileParty.CurrentSettlement is not null
                        && mobileParty.Ai.AiBehaviorInteractable is PartyBase pb1 && pb1.Settlement is { IsHideout: true })
                    || mobileParty.Ai.AiBehaviorInteractable is PartyBase pb2 && pb2.MobileParty is { IsCaravan: true })
                    return;
            }

            if (mobileParty.MapEvent is not null)
                return;

            // --- Stuck detection: runs unconditionally for every BM every tick ---
            // Must be here, NOT inside BMThink, because BMThink is skipped when
            // nearby bandits exist — which is always true when parties are clumping.
            // --- Stuck detection: land-only BMs ---
            // Naval BMs cannot get stuck on land — SetLandNavigationAccess(false) prevents it.
            if (mobileParty.IsBM()
                && !mobileParty.HasNavalNavigationCapability
                && !mobileParty.IsCurrentlyAtSea
                && mobileParty.Ai?.IsDisabled == false
                && mobileParty.Ai?.DoNotMakeNewDecisions == false)
            {
                const float StuckDistanceThreshold = 15f;
                const int StuckHourLimit = 12;
                const float EscapeMinDistance = 30f;

                var currentPos2D = mobileParty.Position.ToVec2();

                if (StuckTracker.TryGetValue(mobileParty, out var stuckState))
                {
                    if (currentPos2D.Distance(stuckState.LastPos) < StuckDistanceThreshold)
                    {
                        var newCount = stuckState.HourCount + 1;
                        StuckTracker[mobileParty] = (stuckState.LastPos, newCount);

                        if (newCount >= StuckHourLimit)
                        {
                            StuckTracker.Remove(mobileParty);

                            // GatePosition is the navmesh entry point — safe for SetMoveGoToPoint.
                            // SetMovePatrolAroundSettlement must NOT be used here: it calls into
                            // GetNavalPatrolBehavior → GetPathDistanceBetweenAIFaces in native code,
                            // which crashes with AccessViolationException when the target settlement
                            // has no valid land AI navmesh face (coastal/island hideouts etc.).
                            var escapePool = Hideouts
                                .WhereQ(s => s != null
                                    && s.GatePosition.ToVec2().Distance(currentPos2D) > EscapeMinDistance)
                                .ToListQ();

                            if (escapePool.Count == 0)
                                escapePool = Settlement.All
                                    .WhereQ(s => s != null
                                        && !s.IsTown && !s.IsCastle
                                        && s.GatePosition.ToVec2().Distance(currentPos2D) > EscapeMinDistance)
                                    .ToListQ();

                            if (escapePool.Count > 0)
                            {
                                var escapeTarget = escapePool.GetRandomElement();
                                Logger.LogDebug($"{mobileParty.Name}({mobileParty.StringId}) stuck {newCount}h — escaping to {escapeTarget.Name}");
                                var navType = mobileParty.HasNavalNavigationCapability
                                    ? MobileParty.NavigationType.Naval
                                    : MobileParty.NavigationType.Default;
                                mobileParty.SetMoveGoToPoint(escapeTarget.GatePosition, navType);
                                return; // don't continue into merge logic this tick
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
            // --- End stuck detection ---

            MobileParty mergeTarget = null;
            bool isBM = mobileParty.IsBM();

            // Respect the SpawnLandMilitias / SpawnNavalMilitias settings.
            // If the party's type is disabled, skip all merge/AI logic entirely.
            if (isBM || mobileParty.HasNavalNavigationCapability)
            {
                bool isNavalParty = mobileParty.HasNavalNavigationCapability;
                if (isNavalParty && !Globals.Settings.SpawnNavalMilitias)
                {
                    BMThink(mobileParty);
                    return;
                }
                if (!isNavalParty && !Globals.Settings.SpawnLandMilitias)
                {
                    BMThink(mobileParty);
                    return;
                }
            }

            try
            {
                if (isBM)
                {
                    // (OG) Let another hero in the party take over the leaderless militia
                    // (OG) The game auto-replaces the leader if there's another hero in the party, just putting this here in case of some oversight
                    if (mobileParty.LeaderHero is null && mobileParty.MemberRoster.TotalHeroes > 0)
                    {
                        // FIX: Check for empty list before calling .First()
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

                // cancel merge if the target has changed its behavior
                if (mobileParty.DefaultBehavior == AiBehavior.EngageParty
                    && mobileParty.TargetParty is not null
                    && !FactionManager.IsAtWarAgainstFaction(mobileParty.MapFaction, mobileParty.TargetParty.MapFaction))
                {
                    if (mobileParty.TargetParty.DefaultBehavior != AiBehavior.EngageParty ||
                        mobileParty.TargetParty.TargetParty != mobileParty)
                    {
                        mobileParty.SetMoveModeHold();
                    }
                }

                // unstuck AI if raid was interrupted
                if (isBM && mobileParty.Ai.DoNotMakeNewDecisions && mobileParty.DefaultBehavior != AiBehavior.RaidSettlement)
                {
                    mobileParty.Ai.SetDoNotMakeNewDecisions(false);
                    mobileParty.SetMoveModeHold();
                    BMThink(mobileParty);
                    return;
                }

                // near any Hideouts?
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
                        BMThink(mobileParty);
                        return;
                    }
                }

                // BM changed too recently?
                if (isBM)
                {
                    // FIX: Guard GetBM() null check and LastMergedOrSplitDate access
                    var bm = mobileParty.GetBM();
                    if (bm?.LastMergedOrSplitDate != null
                        && CampaignTime.Now < bm.LastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings?.CooldownHours ?? 2))
                    {
                        BMThink(mobileParty);
                        return;
                    }
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

                        if (mobileParty.HasNavalNavigationCapability
                            && mobileParty.ActualClan != null
                            && party.ActualClan != mobileParty.ActualClan)
                            continue;

                        if (party.IsBandit && party.MapEvent is null &&
                            mobileParty.MemberRoster.TotalManCount > Globals.Settings.MergeableSize &&
                            mobileParty.MemberRoster.TotalManCount + party.MemberRoster.TotalManCount >= Globals.Settings.MinPartySize &&
                            IsAvailableBanditParty(party))
                        {
                            nearbyBandits.Add(party);
                        }
                    }
                }

                if (nearbyBandits.Count == 0)
                {
                    BMThink(mobileParty);
                    return;
                }

                // compute once outside the loop — mobileParty roster doesn't change mid-loop
                int mobilePartyMountedCount = NumMountedTroops(mobileParty.MemberRoster);

                foreach (var target in nearbyBandits.OrderByQ(m => m.Position.ToVec2().Distance(mobileParty.Position.ToVec2())))
                {
                    var militiaTotalCount = mobileParty.MemberRoster.TotalManCount + target.MemberRoster.TotalManCount;
                    if (militiaTotalCount < Globals.Settings.MinPartySize || militiaTotalCount > CalculatedMaxPartySize)
                        continue;

                    if (mobileParty.MapFaction is not null && target.MapFaction?.IsAtWarWith(mobileParty.MapFaction) == true)
                        continue;

                    if (target.IsBM())
                    {
                        // FIX: Guard GetBM() null check
                        var targetBM = target.GetBM();
                        if (targetBM?.LastMergedOrSplitDate != null
                            && CampaignTime.Now < targetBM.LastMergedOrSplitDate + CampaignTime.Hours(Globals.Settings?.CooldownHours ?? 2))
                            continue;
                    }

                    if (mobilePartyMountedCount + NumMountedTroops(target.MemberRoster) > militiaTotalCount / 2)
                        continue;

                    mergeTarget = target;
                    break;
                }

                if (mergeTarget is null)
                {
                    BMThink(mobileParty);
                    return;
                }

                if (Campaign.Current?.Models?.MapDistanceModel is not null)
                {
                    var distance = mobileParty.Position.ToVec2().Distance(mergeTarget.Position.ToVec2());
                    if (distance <= MergeDistance)
                    {
                        TryMergeParties(mobileParty, mergeTarget);
                    }
                    else if (mobileParty.TargetParty != mergeTarget)
                    {
                        var navType = mobileParty.IsCurrentlyAtSea
                            ? MobileParty.NavigationType.Naval
                            : MobileParty.NavigationType.Default;
                        mobileParty.SetMoveEngageParty(mergeTarget, navType);
                        mergeTarget.SetMoveEngageParty(mobileParty, navType);
                    }
                }
                else
                {
                    Logger.LogWarning("Campaign.Current or MapDistanceModel is null");
                }
            }
            catch (Exception ex)
            {
                // strange memory access violation error when some mods are installed
                Logger.LogError(ex, $"TickPartialHourlyAiEvent crash - removing party {mobileParty?.StringId}");
                try { Trash(mobileParty); } catch { /* swallow */ }
                if (mergeTarget is not null)
                {
                    try { Trash(mergeTarget); } catch { /* swallow */ }
                }
            }
        }

        internal static void BMThink(MobileParty mobileParty)
            => MilitiaBehaviorService.BMThink(mobileParty);

        private static void DailyTickPartyEvent(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty.IsBM())
                {
                    if ((int)CampaignTime.Now.ToWeeks % CampaignTime.DaysInWeek == 0
                        && Globals.Settings.AllowPillaging)
                    {
                        AdjustAvoidance(mobileParty);
                    }

                    TryGrowing(mobileParty);
                    if (MBRandom.RandomFloat <= Globals.Settings.TrainingChance * 0.01f)
                    {
                        TrainMilitia(mobileParty);
                    }

                    TrySplitParty(mobileParty);
                }
            }
            catch (Exception ex)
            {

                Logger.LogError(ex, $"Error in DailyTickPartyEvent for party {mobileParty?.StringId}");
            }
        }

        private static void AdjustAvoidance(MobileParty mobileParty)
            => MilitiaBehaviorService.AdjustAvoidance(mobileParty);

        private static void TryGrowing(MobileParty mobileParty)
            => MilitiaBehaviorService.TryGrowing(mobileParty);

        private static void SpawnBM()
            => MilitiaBehaviorService.SpawnBM();

        internal static void TeleportMilitiasNearPlayer(MobileParty banditMilitia)
            => MilitiaBehaviorService.TeleportMilitiasNearPlayer(banditMilitia);

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("Heroes", ref Heroes);
            if (dataStore.IsLoading)
            {
                Globals.Heroes.RemoveAll(hero => !Hero.AllAliveHeroes.Contains(hero));
            }
        }
    }
}