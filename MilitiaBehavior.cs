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

// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<MilitiaBehavior>();

        internal const float Increment = 5;
        private const float EffectRadius = 100;
        private const int AdjustRadius = 50;
        private const int settlementFindRange = 200;
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

                    PartyBase party = attackers.First().Party;
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

                    PartyBase party = m.AttackerSide.Parties.First().Party;
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
                    var militias = GetCachedBMs()?.CountQ() ?? 0;
                    var leaderless = AllBMs?.CountQ(c => c?.MobileParty?.LeaderHero is null) ?? 0;

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

                foreach (var BM in GetCachedBMs().WhereQ(bm =>
                             bm?.MobileParty != null && bm.MobileParty.Position.ToVec2().Distance(mobileParty.Position.ToVec2()) < EffectRadius))
                {
                    if (BM?.Avoidance is null)
                        continue;
                    if (destroyer.LeaderHero is null)
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
                if (mobileParty.PartyComponent is BanditPartyComponent)
                {
                    if ((mobileParty.CurrentSettlement is not null
                         && mobileParty.Ai.AiBehaviorInteractable is PartyBase pb1 && pb1.Settlement is { IsHideout: true })
                        || mobileParty.Ai.AiBehaviorInteractable is PartyBase pb2 && pb2.MobileParty is { IsCaravan: true })
                        return;
                }
            }

            if (mobileParty.MapEvent is not null)
                return;

            MobileParty mergeTarget = null;
            try
            {
                if (mobileParty.IsBM())
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
                if (mobileParty.IsBM() && mobileParty.Ai.DoNotMakeNewDecisions && mobileParty.DefaultBehavior != AiBehavior.RaidSettlement)
                {
                    mobileParty.Ai.SetDoNotMakeNewDecisions(false);
                    mobileParty.SetMoveModeHold();
                    BMThink(mobileParty);
                    return;
                }

                // near any Hideouts?
                if (mobileParty.IsBM())
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
                if (mobileParty.IsBM())
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
                        if (party == mobileParty) continue;
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

                    if (NumMountedTroops(mobileParty.MemberRoster) + NumMountedTroops(target.MemberRoster) > militiaTotalCount / 2)
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
                    if (distance > MergeDistance && mobileParty.TargetParty != mergeTarget)
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
                    Trash(mergeTarget);
                }
            }
        }

        internal static void BMThink(MobileParty mobileParty)
        {
            try
            {

                if (mobileParty?.Ai is null || mobileParty.Ai.IsDisabled || mobileParty.Ai.DoNotMakeNewDecisions || !mobileParty.IsBM())
                    return;

                Settlement target;
                switch (mobileParty.DefaultBehavior)
                {
                    case AiBehavior.None:
                    case AiBehavior.Hold:
                        if (mobileParty.TargetSettlement is null)
                        {
                            var navType = mobileParty.IsCurrentlyAtSea
                                ? MobileParty.NavigationType.Naval
                                : MobileParty.NavigationType.Default;

                            var validSettlements = Settlement.All
                                .WhereQ(s => s != null && s.Position.ToVec2().Distance(mobileParty.Position.ToVec2()) < settlementFindRange)
                                .ToListQ();

                            if (validSettlements.Count > 0)
                            {
                                target = validSettlements.GetRandomElement();
                                bool isTargetingPort = mobileParty.IsCurrentlyAtSea;
                                mobileParty.SetMovePatrolAroundSettlement(target, navType, isTargetingPort);
                            }
                        }
                        break;

                    case AiBehavior.GoToSettlement:
                        // Sometimes they might be stuck in a hideout
                        if (mobileParty.TargetSettlement?.IsHideout == true)
                        {
                            // strange memory access violation error when some mods are installed
                            if (!mobileParty.IsEngaging && mobileParty.Position.ToVec2().Distance(mobileParty.TargetSettlement.Position.ToVec2()) == 0f)
                            {
                                mobileParty.SetMoveModeHold();
                            }
                        }
                        break;
                    case AiBehavior.PatrolAroundPoint:
                        var BM = mobileParty.GetBM();
                        if (BM is null || mobileParty.MapFaction is null)
                            return;

                        // Sea parties do not raid — they cannot reach inland villages
                        if (mobileParty.IsCurrentlyAtSea)
                            break;

                        // PILLAGE!
                        if (Globals.Settings?.AllowPillaging == true
                            && mobileParty.LeaderHero is not null
                            && mobileParty.Party.EstimatedStrength > MilitiaPartyAveragePower
                            && MBRandom.RandomFloat < (Globals.Settings?.PillagingChance ?? 0) * 0.01f
                            && GetCachedBMs().CountQ(m => m.MobileParty.ShortTermBehavior is AiBehavior.RaidSettlement) < RaidCap)
                        {
                            var raidingCount = GetCachedBMs().CountQ(m => m?.MobileParty?.ShortTermBehavior is AiBehavior.RaidSettlement);

                            if (raidingCount >= RaidCap)
                            {
                                Logger.LogTrace($"{mobileParty.Name} cannot raid - raid cap ({RaidCap}) already reached ({raidingCount} active raids)");
                                break;
                            }

                            try
                            {
                                target = Settlement.All
                                    .WhereQ(s => s.IsVillage
                                        && s.Village is not { VillageState: Village.VillageStates.BeingRaided or Village.VillageStates.Looted }
                                        && s.Owner is not null
                                        && s.MapFaction?.IsAtWarWith(mobileParty.MapFaction) != false
                                        && s.GetValue() > 0)
                                    .OrderByQ(s => s.GatePosition.ToVec2().Distance(mobileParty.Position.ToVec2()))
                                    .FirstOrDefault();
                            }
                            catch (Exception ex)
                            {
                                Logger.LogError(ex, $"BMThink: Error finding nearest village. Removing the problematic party. Party: {mobileParty}");
                                Trash(mobileParty);
                                return;
                            }

                            if (target is not null && target.Owner is not null && BM.Avoidance is not null)
                            {
                                if (BM.Avoidance.ContainsKey(target.Owner)
                                    && MBRandom.RandomFloat * 100f <= BM.Avoidance[target.Owner])
                                {
                                    Logger.LogTrace($"{mobileParty.Name}({mobileParty.StringId}) avoided pillaging {target}");
                                    break;
                                }
                            }

                            if (target.OwnerClan == Hero.MainHero.Clan)
                                InformationManager.DisplayMessage(new InformationMessage($"{mobileParty.Name} is raiding your village {target.Name} near {target.Town?.Name}!"));

                            Logger.LogTrace($"{mobileParty.Name}({mobileParty.StringId} has decided to raid {target.Name}.");
                            mobileParty.SetMoveRaidSettlement(target, MobileParty.NavigationType.Default);
                            mobileParty.Ai.SetDoNotMakeNewDecisions(true);
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error in BMThink for party {mobileParty?.StringId}");
            }
        }

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
        {
            try
            {
                if (mobileParty?.Position is null)
                    return;

                var cachedBMs = GetCachedBMs(true);

                foreach (var BM in cachedBMs.WhereQ(bm => bm?.Leader is not null
                                               && bm.MobileParty?.Position is not null
                                               && bm.MobileParty.Position.ToVec2().Distance(mobileParty.Position.ToVec2()) < AdjustRadius))
                {
                    if (BM?.Avoidance is null)
                        continue;

                    var avoidanceKeysCopy = BM.Avoidance.Keys.ToListQ();
                    foreach (var heroKey in avoidanceKeysCopy)
                    {
                        if (heroKey is null)
                            continue;

                        if (!BM.Avoidance.ContainsKey(heroKey))
                            continue;

                        float currentValue = BM.Avoidance[heroKey];

                        BM.Avoidance[heroKey] = Math.Max(0, currentValue - Increment);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in AdjustAvoidance");
            }
        }

        private static void TryGrowing(MobileParty mobileParty)
        {
            if (Globals.Settings.GrowthPercent > 0
                && MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent
                && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                && mobileParty.MapEvent is null
                && IsAvailableBanditParty(mobileParty)
                && MBRandom.RandomFloat <= Globals.Settings.GrowthChance / 100f)
            {
                var eligibleToGrow = mobileParty.MemberRoster.GetTroopRoster().WhereQ(rosterElement =>
                        rosterElement.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !rosterElement.Character.IsHero
                        && mobileParty.ShortTermBehavior != AiBehavior.FleeToPoint
                        && !mobileParty.IsVisible
                        && !rosterElement.Character.Name.ToString().StartsWith("Glorious"))
                    .ToListQ();
                if (eligibleToGrow.Any())
                {
                    var growthAmount = mobileParty.MemberRoster.TotalManCount * Globals.Settings.GrowthPercent / 100f;

                    // (OG) Bump up growth to reach GlobalPowerPercent (synthetic but it helps warm up militia population)
                    // (OG) Thanks Erythion!

                    // FIX: Guard against division by zero
                    var boost = GlobalMilitiaPower > 0 ? CalculatedGlobalPowerLimit / GlobalMilitiaPower : 1;
                    growthAmount += Globals.Settings.GlobalPowerPercent / 100f * boost;
                    growthAmount = Mathf.Clamp(growthAmount, 1, 50);

                    //Log.Debug?.Log($"+++ Growing {mobileParty.Name}, total: {mobileParty.MemberRoster.TotalManCount}");

                    for (var i = 0; i < growthAmount && mobileParty.MemberRoster.TotalManCount + 1 < CalculatedMaxPartySize; i++)
                    {
                        if (eligibleToGrow.Count == 0)  // ADD: Guard empty list
                            break;

                        var rosterElement = eligibleToGrow.GetRandomElement();
                        if (rosterElement.Character is null) // ADD: Guard null troop
                            continue;

                        var troop = rosterElement.Character;
                        if (GlobalMilitiaPower + troop.GetPower() < CalculatedGlobalPowerLimit)
                        {
                            mobileParty.MemberRoster.AddToCounts(troop, 1);
                        }
                    }

                    AdjustCavalryCount(mobileParty.MemberRoster);
                    //var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                    //var strengthString = $"{Math.Round(mobileParty.Party.TotalStrength)} strength";
                    //Log.Debug?.Log($"{$"Grown to",-70} | {troopString,10} | {strengthString,12} |");
                    DoPowerCalculations();
                    // Log.Debug?.Log($"Grown to: {mobileParty.MemberRoster.TotalManCount}");
                }
            }
        }

        private static void SpawnBM()
        {
            if (!Globals.Settings.MilitiaSpawn)
                return;

            try
            {
                var validHideouts = Settlement.All
                    .WhereQ(s => s.IsHideout
                        && s.GatePosition.ToVec2().Distance(MobileParty.MainParty.Position.ToVec2()) > 100)
                    .ToListQ();

                if (validHideouts.Count == 0)
                {
                    Logger.LogWarning("No hideout available for spawning bandit militia.");
                    return;
                }

                var settlement = validHideouts.GetRandomElement();

                if (settlement is null)
                {
                    Logger.LogWarning("Selected hideout is null.");
                    return;
                }

                var maxIterations = (int)Math.Ceiling((Globals.Settings.GlobalPowerPercent - MilitiaPowerPercent) / 24f);
                maxIterations = Math.Min(maxIterations, SpawnLoopSafetyLimit);
                
                for (var i = 0; i < maxIterations; i++)
                {
                    if (MilitiaPowerPercent + 1 > Globals.Settings.GlobalPowerPercent)
                        break;

                    if (MBRandom.RandomInt(0, 101) > Globals.Settings.SpawnChance)
                        continue;

                    var min = Convert.ToInt32(Globals.Settings.MinPartySize);
                    var max = Convert.ToInt32(CalculatedMaxPartySize);

                    // if the MinPartySize is cranked it will throw ArgumentOutOfRangeException
                    if (max < min)
                        max = min;

                    var roster = TroopRoster.CreateDummyTroopRoster();
                    var size = Convert.ToInt32(MBRandom.RandomInt(min, max + 1) / 2f);
                    var foot = MBRandom.RandomInt(40, 61);
                    var range = MBRandom.RandomInt(20, MBRandom.RandomInt(35, 100 - foot) + 1);
                    var horse = 100 - foot - range;

                    // DRM has no cavalry
                    if (Globals.BasicCavalry.Count == 0)
                    {
                        foot += horse % 2 == 0
                            ? horse / 2
                            : horse / 2 + 1;
                        range += horse / 2;
                        horse = 0;
                    }

                    var formation = new List<int>
                    {
                        foot, range, horse
                    };

                    for (var index = 0; index < formation.Count; index++)
                    {
                        // FIX: Get troop list once and check it exists
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
                            if (troop is not null)  // ADD: Guard troop selection
                                roster.AddToCounts(troop, 1);
                        }
                    }

                    // FIX: Skip empty rosters
                    if (roster.TotalManCount == 0)
                    {
                        Logger.LogWarning("Skipping militia spawn with empty roster");
                        continue;
                    }

                    var banditClan = Clan.BanditFactions?.FirstOrDefault(c => c.Culture == settlement.Culture)
                        ?? Clan.BanditFactions?.FirstOrDefault()
                        ?? settlement.OwnerClan;

                    /* ORIGINAL CODE
                    var bm = MobileParty.CreateParty("Bandit_Militia",
                        new ModBanditMilitiaPartyComponent(settlement, null), m => m.ActualClan = settlement.OwnerClan); 
                    */

                    var banditMilitia = MobileParty.CreateParty("Bandit_Militia",
                        new ModBanditMilitiaPartyComponent(settlement, null, banditClan));
                    

                    if (banditMilitia is null)  // ADD: Guard CreateParty result
                    {
                        Logger.LogWarning("Failed to create militia party");
                        continue;
                    }

                    /*
                    // ADD: Force hostility regardless of Fourberie/diplomacy mods
                    try
                    {
                        if (bm.MapFaction != null && Hero.MainHero?.MapFaction != null)
                        {
                            // Check current war status
                            bool isAtWar = FactionManager.IsAtWarAgainstFaction(bm.MapFaction, Hero.MainHero.MapFaction);

                            if (!isAtWar)
                            {
                                // Declare war if not already at war
                                FactionManager.DeclareWar(bm.MapFaction, Hero.MainHero.MapFaction, true);
                                Logger.LogDebug($"Forced war declaration: {bm.Name} vs {Hero.MainHero.MapFaction.Name}");
                            }

                            // Also ensure clan relations are hostile (< -10 threshold)
                            if (bm.ActualClan != null && Hero.MainHero.Clan != null)
                            {
                                int currentRelation = bm.ActualClan.GetRelationWithClan(Hero.MainHero.Clan);
                                if (currentRelation > -10)
                                {
                                    ChangeRelationAction.ApplyRelationChangeBetweenHeroes(
                                        bm.LeaderHero ?? bm.ActualClan.Leader,
                                        Hero.MainHero,
                                        -50 - currentRelation, // Force to -50 or lower
                                        false
                                    );
                                    Logger.LogDebug($"Reset militia clan relations: {bm.ActualClan.Name} to -50");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogWarning($"Could not force hostility for {bm.StringId}: {ex.Message}");
                    }
                    */

                    try
                    {
                        InitMilitia(banditMilitia, new[] { roster, TroopRoster.CreateDummyTroopRoster() }, settlement.GatePosition);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Error initializing militia {banditMilitia.StringId}");
                        Trash(banditMilitia);
                        continue;
                    }

                    DoPowerCalculations();
                    
                    Logger.LogDebug($"Spawned {banditMilitia.Name}({banditMilitia.StringId}) at {banditMilitia.Position.ToVec2()}.");

                    if (Globals.Settings?.TestingMode == true)
                        TeleportMilitiasNearPlayer(banditMilitia);
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage("Problem spawning BM, please open a bug report with the BanditMilitias*.log file."));
                InformationManager.DisplayMessage(new InformationMessage($"{ex.Message}"));
                Logger.LogError(ex, "Error spawning bandit militia.");
            }
        }

        private static void TeleportMilitiasNearPlayer(MobileParty banditMilitia)
        {
            try
            {
                MobileParty targetParty = null;

                // Try to get main hero's party (normal case)
                if (Hero.MainHero?.PartyBelongedTo is not null)
                    targetParty = Hero.MainHero.PartyBelongedTo;

                // If hero is prisoner, get that party instead
                if (targetParty is null && Hero.MainHero?.PartyBelongedToAsPrisoner?.MobileParty is not null)
                    targetParty = Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;

                // Only teleport if we found a valid target
                if (targetParty?.Position != null)
                {
                    banditMilitia.Position = targetParty.Position;
                    Logger.LogTrace($"Teleported {banditMilitia.Name} to player at {banditMilitia.Position.ToVec2()}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in testing mode teleport");
            }
        }

        public override void SyncData(IDataStore dataStore)
        {
            dataStore.SyncData("Heroes", ref Heroes);
            if (dataStore.IsLoading)
            {
                // clean up heroes from an old bug
                Globals.Heroes.RemoveAll(hero => !Hero.AllAliveHeroes.Contains(hero));
            }
        }
    }
}