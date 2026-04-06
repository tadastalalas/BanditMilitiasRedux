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

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    /// <summary>
    /// Owns all autonomous BM AI decisions, growth, avoidance adjustment, and spawning.
    /// <see cref="MilitiaBehavior"/> is the thin campaign-event orchestrator;
    /// this class contains every algorithm that has no dependency on the behaviour instance.
    /// </summary>
    internal static class MilitiaBehaviorService
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<SubModule>();

        private const float EffectRadius = 100;
        private const int AdjustRadius = 50;
        private const int SettlementFindRange = 200;
        private const int SpawnLoopSafetyLimit = 100;

        // ── AI ────────────────────────────────────────────────────────────────────

        internal static void BMThink(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty?.Ai is null || mobileParty.Ai.IsDisabled || mobileParty.Ai.DoNotMakeNewDecisions || !mobileParty.IsBM())
                    return;

                // Naval BMs are driven entirely by AiHourlyTickEvent score injection +
                // SetLandNavigationAccess(false), exactly like vanilla PiratesCampaignBehavior.
                // They need no custom per-tick decisions here — any order issued from BMThink
                // risks overriding the engine's naval routing.
                if (mobileParty.HasNavalNavigationCapability)
                    return;

                Settlement target;
                switch (mobileParty.DefaultBehavior)
                {
                    case AiBehavior.None:
                    case AiBehavior.Hold:
                        if (mobileParty.TargetSettlement is null)
                        {
                            var homeSettlement = mobileParty.GetBM()?.HomeSettlement;
                            if (homeSettlement?.StringId.StartsWith("hideout_seaside") == true
                                && mobileParty.HasNavalNavigationCapability)
                            {
                                mobileParty.DesiredAiNavigationType = MobileParty.NavigationType.Naval;
                                mobileParty.SetMovePatrolAroundPoint(homeSettlement.GatePosition, MobileParty.NavigationType.Naval);
                                break;
                            }

                            // Naval parties that don't have a seaside home yet should still
                            // stay at sea — never issue a land-patrol order for them.
                            if (mobileParty.HasNavalNavigationCapability)
                                break;

                            var validSettlements = Settlement.All
                                .WhereQ(s => s != null
                                    && !s.IsHideout
                                    && s.GatePosition.ToVec2().Distance(mobileParty.Position.ToVec2()) < SettlementFindRange)
                                .ToListQ();

                            if (validSettlements.Count > 0)
                            {
                                target = validSettlements.GetRandomElement();
                                mobileParty.SetMovePatrolAroundSettlement(target, MobileParty.NavigationType.Default, false);
                            }
                        }
                        break;

                    case AiBehavior.GoToSettlement:
                        if (mobileParty.TargetSettlement?.IsHideout == true)
                        {
                            if (!mobileParty.IsEngaging && mobileParty.Position.ToVec2().Distance(mobileParty.TargetSettlement.Position.ToVec2()) == 0f)
                                mobileParty.SetMoveModeHold();
                        }
                        else if (mobileParty.IsCurrentlyAtSea
                            && mobileParty.TargetSettlement == mobileParty.GetBM()?.HomeSettlement
                            && mobileParty.Position.ToVec2().Distance(mobileParty.TargetSettlement.GatePosition.ToVec2()) < 10f)
                        {
                            mobileParty.SetMovePatrolAroundPoint(mobileParty.GetBM().HomeSettlement.GatePosition, MobileParty.NavigationType.Naval);
                        }
                        break;

                    case AiBehavior.PatrolAroundPoint:
                        var BM = mobileParty.GetBM();
                        if (BM is null || mobileParty.MapFaction is null)
                            return;

                        if (mobileParty.IsCurrentlyAtSea)
                        {
                            if (MBRandom.RandomFloat < 0.05f)
                            {
                                var homeSettlement = BM.HomeSettlement;
                                if (homeSettlement is not null)
                                    mobileParty.SetMovePatrolAroundPoint(homeSettlement.GatePosition, MobileParty.NavigationType.Naval);
                            }
                            break;
                        }

                        // PILLAGE!
                        if (Globals.Settings?.AllowPillaging == true
                            && mobileParty.LeaderHero is not null
                            && mobileParty.Party.EstimatedStrength > MilitiaPartyAveragePower
                            && MBRandom.RandomFloat < (Globals.Settings?.PillagingChance ?? 0) * 0.01f)
                        {
                            var raidingCount = Helper.GetCachedBMs().CountQ(m => m?.MobileParty?.ShortTermBehavior is AiBehavior.RaidSettlement);

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
                                Helper.Trash(mobileParty);
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

        // ── Growth ────────────────────────────────────────────────────────────────

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

                // Bump up growth to reach GlobalPowerPercent (synthetic but it helps warm up militia population)
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
                DoPowerCalculations();
            }
        }

        // ── Avoidance ─────────────────────────────────────────────────────────────

        internal static void AdjustAvoidance(MobileParty mobileParty)
        {
            try
            {
                if (mobileParty?.Position is null)
                    return;

                foreach (var BM in Helper.GetCachedBMs(true).WhereQ(bm => bm?.Leader is not null
                                               && bm.MobileParty?.Position is not null
                                               && bm.MobileParty.Position.ToVec2().Distance(mobileParty.Position.ToVec2()) < AdjustRadius))
                {
                    if (BM?.Avoidance is null)
                        continue;

                    var avoidanceKeysCopy = BM.Avoidance.Keys.ToListQ();
                    foreach (var heroKey in avoidanceKeysCopy)
                    {
                        if (heroKey is null || !BM.Avoidance.ContainsKey(heroKey))
                            continue;

                        BM.Avoidance[heroKey] = Math.Max(0, BM.Avoidance[heroKey] - MilitiaBehavior.Increment);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in AdjustAvoidance");
            }
        }

        // ── Spawn ─────────────────────────────────────────────────────────────────

        internal static void SpawnBM()
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

                var maxIterations = (int)Math.Ceiling((Globals.Settings.GlobalPowerPercent - MilitiaPowerPercent) / 24f);
                maxIterations = Math.Min(maxIterations, SpawnLoopSafetyLimit);

                for (var i = 0; i < maxIterations; i++)
                {
                    if (MilitiaPowerPercent + 1 > Globals.Settings.GlobalPowerPercent)
                        break;

                    if (MBRandom.RandomInt(0, 101) > Globals.Settings.SpawnChance)
                        continue;

                    // Pick a random hideout first, derive the clan from its culture,
                    // then re-pick a hideout that actually matches that clan's type
                    // (seaside for naval clans, land for land clans).
                    var baseSettlement = validHideouts.GetRandomElement();
                    if (baseSettlement is null)
                    {
                        Logger.LogWarning("Selected hideout is null.");
                        continue;
                    }

                    var banditClan = Clan.BanditFactions?.FirstOrDefault(c => c.Culture == baseSettlement.Culture)
                        ?? Clan.BanditFactions?.FirstOrDefault()
                        ?? baseSettlement.OwnerClan;

                    bool isNavalClan = banditClan?.HasNavalNavigationCapability == true;

                    // Respect the SpawnLandMilitias / SpawnNavalMilitias settings
                    if (isNavalClan && !Globals.Settings.SpawnNavalMilitias)
                    {
                        Logger.LogDebug($"SpawnBM: skipping naval spawn for {banditClan?.Name} — naval militias disabled by settings.");
                        continue;
                    }

                    if (!isNavalClan && !Globals.Settings.SpawnLandMilitias)
                    {
                        Logger.LogDebug($"SpawnBM: skipping land spawn for {banditClan?.Name} — land militias disabled by settings.");
                        continue;
                    }

                    // Ensure the home settlement matches the clan's navigation type
                    var settlement = validHideouts
                        .WhereQ(s => s.StringId.StartsWith("hideout_seaside") == isNavalClan)
                        .ToListQ()
                        .GetRandomElement()
                        ?? baseSettlement;

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
                    {
                        Logger.LogWarning("Skipping militia spawn with empty roster");
                        continue;
                    }

                    // Per-clan cap — naval and land clans have separate limits since
                    // naval clans have far fewer native parties and flood the map faster.
                    bool isNavalClan2 = banditClan?.HasNavalNavigationCapability == true;
                    int clanCap = isNavalClan2
                        ? Globals.Settings.MaxNavalPartiesPerClan
                        : Globals.Settings.MaxLandPartiesPerClan;

                    if (clanCap > 0)
                    {
                        var partiesForClan = Helper.GetCachedBMs()
                            .CountQ(bm => bm.MobileParty?.ActualClan == banditClan);
                        if (partiesForClan >= clanCap)
                        {
                            Logger.LogDebug($"SpawnBM: skipping spawn for {banditClan?.Name} ({(isNavalClan2 ? "naval" : "land")}) — at cap ({partiesForClan}/{clanCap}).");
                            continue;
                        }
                    }

                    var banditMilitia = MobileParty.CreateParty("Bandit_Militia",
                        new ModBanditMilitiaPartyComponent(settlement, null, banditClan));

                    if (banditMilitia is null)
                    {
                        Logger.LogWarning("Failed to create militia party.");
                        continue;
                    }

                    try
                    {
                        MilitiaPartyFactory.InitMilitia(banditMilitia, new[] { roster, TroopRoster.CreateDummyTroopRoster() }, settlement.GatePosition);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Error initializing militia {banditMilitia.StringId}.");
                        Helper.Trash(banditMilitia);
                        continue;
                    }

                    if (!banditMilitia.HasNavalNavigationCapability)
                        banditMilitia.DesiredAiNavigationType = MobileParty.NavigationType.Default;

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

        // ── Testing ───────────────────────────────────────────────────────────────

        internal static void TeleportMilitiasNearPlayer(MobileParty banditMilitia)
        {
            try
            {
                Logger.LogDebug($"Testing mode is activated.");

                MobileParty targetParty = null;

                if (Hero.MainHero?.PartyBelongedTo is not null)
                    targetParty = Hero.MainHero.PartyBelongedTo;

                if (targetParty is null && Hero.MainHero?.PartyBelongedToAsPrisoner?.MobileParty is not null)
                    targetParty = Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;

                if (targetParty?.Position == null)
                    return;

                bool playerIsAtSea = targetParty.IsCurrentlyAtSea;
                string homeSettlementId = banditMilitia.GetBM()?.HomeSettlement?.StringId ?? "NULL";
                bool militiaIsSeaType = homeSettlementId.StartsWith("hideout_seaside");

                Logger.LogDebug($"TeleportCheck: {banditMilitia.Name} | homeSettlement={homeSettlementId} | playerIsAtSea={playerIsAtSea} | militiaIsSeaType={militiaIsSeaType}");

                if (playerIsAtSea != militiaIsSeaType)
                {
                    Logger.LogDebug($"Skipping teleport of {banditMilitia.Name} — terrain mismatch");
                    return;
                }

                banditMilitia.Position = targetParty.Position;
                Logger.LogDebug($"Teleported {banditMilitia.Name} to player at {banditMilitia.Position.ToVec2()}");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in testing mode teleport");
            }
        }
    }
}