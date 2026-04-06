using System;
using System.Collections.Generic;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using static BanditMilitias.Globals;
using static BanditMilitias.Helper;

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming

namespace BanditMilitias
{
    /// <summary>
    /// Owns the full lifecycle of bandit militia party creation, merging, splitting,
    /// initialisation, training, and availability checks.
    /// </summary>
    internal static class MilitiaPartyFactory
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<SubModule>();

        private const float ReductionFactor = 0.8f;
        private const float SplitDivisor = 2;
        private const float RemovedHero = 1;

        /// <summary>
        /// Maximum roster-padding iterations per split child to prevent an infinite
        /// spin when <see cref="Settings.MinPartySize"/> is large relative to the
        /// number of distinct troop types in the roster.
        /// </summary>
        private const int MaxPadIterations = 200;

        // ── Harmony field ref (ItemRoster — used only in CreateSplitMilitias) ─────

        private static readonly AccessTools.FieldRef<PartyBase, ItemRoster> ItemRoster =
            AccessTools.FieldRefAccess<PartyBase, ItemRoster>("<ItemRoster>k__BackingField");

        // ── Upgrader behavior (reset on new campaign/load) ────────────────────────

        private static PartyUpgraderCampaignBehavior UpgraderCampaignBehavior;

        internal static void ResetUpgraderBehavior() => UpgraderCampaignBehavior = null;

        // ── Quest / special-party blocklist ───────────────────────────────────────

        private static readonly HashSet<string> verbotenParties = new()
        {
            "ebdi_deserters_party",
            "caravan_ambush_quest",
            "arzagos_banner_piece_quest_raider_party",
            "istiana_banner_piece_quest_raider_party",
            "rescue_family_quest_raider_party",
            "destroy_raiders_conspiracy_quest",
            "radagos_raider_party",
            "locate_and_rescue_traveller_quest_raider_party",
            "company_of_trouble",
            "villagers_of_landlord_needs_access_to_village_common_quest",
            //Calradia Expanded Kingdoms in 3.0.2
            "manhunter"
        };

        // ── Availability checks ───────────────────────────────────────────────────

        internal static bool IsAvailableBanditParty(MobileParty __instance)
        {
            return __instance.IsBandit
                   && __instance.CurrentSettlement is null
                   && __instance.MapEvent is null
                   && __instance.Party.MemberRoster.TotalManCount > Globals.Settings.MergeableSize
                   && !__instance.IsTooBusyToMerge()
                   && !__instance.IsUsedByAQuest()
                   && !verbotenParties.Contains(__instance.StringId);
        }

        private static bool CanMergeNow(MobileParty __instance)
        {
            return __instance.MapEvent is null
                   && __instance.Party.MemberRoster.TotalManCount > 0
                   && !__instance.IsUsedByAQuest();
        }

        // ── Merge ─────────────────────────────────────────────────────────────────

        internal static bool TryMergeParties(MobileParty mobileParty, MobileParty mergeTarget)
        {
            try
            {
                Logger.LogDebug($"TryMergeParties: START {mobileParty?.StringId} + {mergeTarget?.StringId}");

                if (mobileParty?.IsActive != true || mergeTarget?.IsActive != true)
                {
                    Logger.LogWarning("Merge cancelled: target parties invalid");
                    return false;
                }

                if (!CanMergeNow(mobileParty) || !CanMergeNow(mergeTarget))
                {
                    mobileParty?.SetMoveModeHold();
                    mergeTarget?.SetMoveModeHold();
                    Logger.LogDebug($"TryMergeParties: CanMergeNow failed");
                    return false;
                }

                Logger.LogDebug($"TryMergeParties: MergeRosters");
                var rosters = MergeRosters(mobileParty, mergeTarget);
                if (rosters == null || rosters.Length != 2)
                {
                    Logger.LogError("MergeRosters returned invalid rosters");
                    return false;
                }
                Logger.LogDebug($"TryMergeParties: rosters OK - members={rosters[0].TotalManCount}");

                Hero leaderHero = (mobileParty.LeaderHero?.Power ?? 0) >= (mergeTarget.LeaderHero?.Power ?? 0)
                    ? mobileParty.LeaderHero
                    : mergeTarget.LeaderHero;
                Logger.LogDebug($"TryMergeParties: leaderHero={leaderHero?.Name.ToString() ?? "NULL"}");

                bool mergedClanIsNaval = mobileParty.ActualClan?.HasNavalNavigationCapability == true
                    || mergeTarget.ActualClan?.HasNavalNavigationCapability == true;

                // Respect the SpawnLandMilitias / SpawnNavalMilitias settings
                if (mergedClanIsNaval && !Globals.Settings.SpawnNavalMilitias)
                {
                    Logger.LogDebug($"TryMergeParties: skipping naval merge for {mobileParty.StringId} + {mergeTarget.StringId} — naval militias disabled by settings.");
                    mobileParty.SetMoveModeHold();
                    mergeTarget.SetMoveModeHold();
                    return false;
                }

                if (!mergedClanIsNaval && !Globals.Settings.SpawnLandMilitias)
                {
                    Logger.LogDebug($"TryMergeParties: skipping land merge for {mobileParty.StringId} + {mergeTarget.StringId} — land militias disabled by settings.");
                    mobileParty.SetMoveModeHold();
                    mergeTarget.SetMoveModeHold();
                    return false;
                }

                Settlement mobilePartyHomeSettlement = mobileParty.HomeSettlement?.IsHideout ?? false ? mobileParty.HomeSettlement : null;
                Settlement mergeTargetHomeSettlement = mergeTarget.HomeSettlement?.IsHideout ?? false ? mergeTarget.HomeSettlement : null;
                Settlement leaderHomeSettlement = leaderHero?.HomeSettlement?.IsHideout ?? false ? leaderHero.HomeSettlement : null;

                Settlement bestSettlement = new[] { leaderHomeSettlement, mobilePartyHomeSettlement, mergeTargetHomeSettlement }
                    .FirstOrDefault(s => s != null && s.StringId.StartsWith("hideout_seaside") == mergedClanIsNaval);

                if (bestSettlement is null)
                {
                    bestSettlement = Hideouts
                        .WhereQ(s => s.StringId.StartsWith("hideout_seaside") == mergedClanIsNaval)
                        .OrderByQ(s => s.GatePosition.ToVec2().Distance(mobileParty.Position.ToVec2()))
                        .FirstOrDefault()
                        ?? Hideouts.OrderByQ(s => s.GatePosition.ToVec2().Distance(mobileParty.Position.ToVec2())).FirstOrDefault();
                    if (bestSettlement is null)
                    {
                        Logger.LogWarning($"TryMergeParties: No hideout found, cancelling merge for {mobileParty.StringId} + {mergeTarget.StringId}");
                        return false;
                    }
                }

                var sourceClanWithShips = (mobileParty.ActualClan?.HasNavalNavigationCapability == true ? mobileParty.ActualClan : null)
                    ?? (mergeTarget.ActualClan?.HasNavalNavigationCapability == true ? mergeTarget.ActualClan : null);
                var mergedClan = sourceClanWithShips
                    ?? mobileParty.ActualClan
                    ?? mergeTarget.ActualClan
                    ?? Clan.BanditFactions.FirstOrDefault(c => c.Culture == bestSettlement.Culture)
                    ?? bestSettlement.OwnerClan;

                Logger.LogDebug($"TryMergeParties: bestSettlement={bestSettlement?.Name.ToString() ?? "NULL"}");
                Logger.LogDebug($"TryMergeParties: CreateParty");
                var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(bestSettlement, leaderHero, mergedClan));
                Logger.LogDebug($"TryMergeParties: CreateParty done, bm={bm?.StringId ?? "NULL"}, bm.IsActive={bm?.IsActive}");
                Logger.LogDebug($"TryMergeParties: ActualClan={bm.ActualClan?.Name.ToString() ?? "NULL"}");

                try
                {
                    Logger.LogDebug($"TryMergeParties: InitMilitia");
                    InitMilitia(bm, rosters, mobileParty.Position);
                    Logger.LogDebug($"TryMergeParties: InitMilitia done, bm.LeaderHero={bm.LeaderHero?.Name.ToString() ?? "NULL"}, bm.IsActive={bm.IsActive}");

                    if (!bm.HasNavalNavigationCapability)
                        bm.DesiredAiNavigationType = MobileParty.NavigationType.Default;

                    var calculatedAvoidance = new Dictionary<Hero, float>();
                    void CalcAverageAvoidance(ModBanditMilitiaPartyComponent BM)
                    {
                        foreach (var entry in BM.Avoidance)
                            if (!calculatedAvoidance.TryGetValue(entry.Key, out _))
                                calculatedAvoidance.Add(entry.Key, entry.Value);
                            else
                            {
                                calculatedAvoidance[entry.Key] += entry.Value;
                                calculatedAvoidance[entry.Key] /= 2;
                            }
                    }

                    if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent BM1)
                        CalcAverageAvoidance(BM1);
                    if (mergeTarget.PartyComponent is ModBanditMilitiaPartyComponent BM2)
                        CalcAverageAvoidance(BM2);

                    bm.GetBM().Avoidance = calculatedAvoidance;

                    if (Globals.Settings.TestingMode)
                        MilitiaBehaviorService.TeleportMilitiasNearPlayer(bm);

                    foreach (var item in mobileParty.ItemRoster.Where(i => !i.IsEmpty && i.EquipmentElement.Item?.IsFood == true))
                        bm.ItemRoster.AddToCounts(item.EquipmentElement, item.Amount);
                    foreach (var item in mergeTarget.ItemRoster.Where(i => !i.IsEmpty && i.EquipmentElement.Item?.IsFood == true))
                        bm.ItemRoster.AddToCounts(item.EquipmentElement, item.Amount);

                    bm.Party.SetVisualAsDirty();
                    Logger.LogDebug($"TryMergeParties: SUCCESS - {bm.Name}({bm.StringId}) merged from {mobileParty.Name}({mobileParty.StringId}) and {mergeTarget.Name}({mergeTarget.StringId})");
                    Helper.Trash(mobileParty);
                    Helper.Trash(mergeTarget);
                    DoPowerCalculations();
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"TryMergeParties: INNER CATCH - InitMilitia or post-init failed");
                    if (mobileParty?.IsActive == true) Helper.Trash(mobileParty);
                    if (mergeTarget?.IsActive == true) Helper.Trash(mergeTarget);
                    Helper.Trash(bm);
                    throw;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"TryMergeParties: OUTER CATCH - {mobileParty?.StringId} + {mergeTarget?.StringId}");
                try { if (mobileParty?.IsActive == true) Helper.Trash(mobileParty); } catch { }
                try { if (mergeTarget?.IsActive == true) Helper.Trash(mergeTarget); } catch { }
                return false;
            }
        }

        internal static TroopRoster[] MergeRosters(MobileParty sourceParty, MobileParty targetParty)
        {
            var outMembers = TroopRoster.CreateDummyTroopRoster();
            var outPrisoners = TroopRoster.CreateDummyTroopRoster();
            var members = new[] { sourceParty.MemberRoster, targetParty.MemberRoster };
            var prisoners = new[] { sourceParty.PrisonRoster, targetParty.PrisonRoster };

            foreach (var roster in members)
            {
                outMembers.Add(roster);
                roster.Clear();
            }

            foreach (var roster in prisoners)
            {
                outPrisoners.Add(roster);
                roster.Clear();
            }

            return [outMembers, outPrisoners];
        }

        // ── Split ─────────────────────────────────────────────────────────────────

        internal static bool TrySplitParty(MobileParty mobileParty)
        {
            if (MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent
                || mobileParty.Party.MemberRoster.TotalManCount / SplitDivisor - RemovedHero < Globals.Settings.MinPartySize
                || !mobileParty.IsBM()
                || mobileParty.IsTooBusyToMerge())
            {
                return false;
            }

            // Respect the SpawnLandMilitias / SpawnNavalMilitias settings — no split
            // means the party simply continues as-is rather than being destroyed.
            bool isNavalParty = mobileParty.ActualClan?.HasNavalNavigationCapability == true;
            if (isNavalParty && !Globals.Settings.SpawnNavalMilitias)
                return false;
            if (!isNavalParty && !Globals.Settings.SpawnLandMilitias)
                return false;

            int roll = MBRandom.RandomInt(0, 101);
            if (roll > Globals.Settings.RandomSplitChance
                || mobileParty.Party.MemberRoster.TotalManCount > Math.Max(1, CalculatedMaxPartySize * ReductionFactor))
            {
                return false;
            }

            var party1 = TroopRoster.CreateDummyTroopRoster();
            var party2 = TroopRoster.CreateDummyTroopRoster();
            var prisoners1 = TroopRoster.CreateDummyTroopRoster();
            var prisoners2 = TroopRoster.CreateDummyTroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();
            var heroes = mobileParty.Party.MemberRoster
                .GetTroopRoster()
                .WhereQ(t => t.Character.IsHero)
                .Select(t => t.Character.HeroObject)
                .OrderByQ(h => -h.Power)
                .ToListQ();
            SplitRosters(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            CreateSplitMilitias(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2, heroes);
            return true;
        }

        private static void SplitRosters(MobileParty original, TroopRoster troops1, TroopRoster troops2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            foreach (var rosterElement in original.MemberRoster.GetTroopRoster().WhereQ(x => x.Character.HeroObject is null))
            {
                SplitRosters(troops1, troops2, rosterElement);
            }

            original.MemberRoster.Clear();

            if (original.PrisonRoster.TotalManCount > 0)
            {
                foreach (var rosterElement in original.PrisonRoster.GetTroopRoster().WhereQ(x => x.Character.HeroObject != Hero.MainHero))
                {
                    SplitRosters(prisoners1, prisoners2, rosterElement);
                }
            }

            original.PrisonRoster.Clear();

            foreach (var item in original.ItemRoster)
            {
                if (string.IsNullOrEmpty(item.EquipmentElement.Item?.Name?.ToString()))
                {
                    Logger.LogWarning("Bad item: " + item.EquipmentElement);
                    continue;
                }

                var half = Math.Max(1, item.Amount / 2);
                inventory1.AddToCounts(item.EquipmentElement, half);
                var remainder = item.Amount % 2;
                inventory2.AddToCounts(item.EquipmentElement, half + remainder);
            }

            original.ItemRoster.Clear();
        }

        private static void SplitRosters(TroopRoster roster1, TroopRoster roster2, TroopRosterElement rosterElement)
        {
            if (rosterElement.Number == 1)
            {
                if (MBRandom.RandomInt(0, 2) == 0)
                    roster1.AddToCounts(rosterElement.Character, 1);
                else
                    roster2.AddToCounts(rosterElement.Character, 1);
            }
            else
            {
                var half = Math.Max(1, rosterElement.Number / 2);
                roster1.AddToCounts(rosterElement.Character, half);
                var remainder = rosterElement.Number % 2;
                roster2.AddToCounts(rosterElement.Character, Math.Max(1, half + remainder));
            }
        }

        private static void CreateSplitMilitias(MobileParty original, TroopRoster party1, TroopRoster party2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2, List<Hero> heroes)
        {
            try
            {
                if (original?.IsActive != true || heroes == null || heroes.Count == 0)
                {
                    Logger.LogWarning($"Invalid split attempt: original={original?.StringId}, heroes={heroes?.Count}");
                    return;
                }

                if (original.HomeSettlement == null)
                {
                    Logger.LogError("Original militia has no HomeSettlement");
                    Helper.Trash(original);
                    return;
                }

                original.MemberRoster.RemoveIf(t => t.Character.IsHero);

                for (int i = heroes.Count - 1; i >= 0; i--)
                {
                    TroopRoster targetParty = i % 2 == 0 ? party1 : party2;
                    targetParty.AddToCounts(heroes[i].CharacterObject, 1, true);
                }

                // Pad party1 up to MinPartySize.
                // Bug fix: cap iterations to avoid infinite spin when MinPartySize
                // exceeds the number of troops that can actually be added.
                int padIterations1 = 0;
                while (party1.TotalManCount < Globals.Settings.MinPartySize && party1.Count > 0)
                {
                    if (++padIterations1 > MaxPadIterations)
                    {
                        Logger.LogWarning($"CreateSplitMilitias: party1 padding hit safety cap ({MaxPadIterations}) at {party1.TotalManCount}/{Globals.Settings.MinPartySize}");
                        break;
                    }
                    var troop = party1.GetCharacterAtIndex(MBRandom.RandomInt(0, party1.Count));
                    if (troop == null) break;
                    if (!Helper.IsRegistered(troop))
                        Helper.Meow();
                    party1.AddToCounts(troop, 1);
                }

                // Pad party2 up to MinPartySize.
                int padIterations2 = 0;
                while (party2.TotalManCount < Globals.Settings.MinPartySize && party2.Count > 0)
                {
                    if (++padIterations2 > MaxPadIterations)
                    {
                        Logger.LogWarning($"CreateSplitMilitias: party2 padding hit safety cap ({MaxPadIterations}) at {party2.TotalManCount}/{Globals.Settings.MinPartySize}");
                        break;
                    }
                    var troop = party2.GetCharacterAtIndex(MBRandom.RandomInt(0, party2.Count));
                    if (troop == null) break;
                    if (!Helper.IsRegistered(troop))
                        Helper.Meow();
                    party2.AddToCounts(troop, 1);
                }

                var originalIsNaval = original.ActualClan?.HasNavalNavigationCapability == true;
                var splitHome = original.HomeSettlement;

                // If the original's HomeSettlement doesn't match the clan's navigation
                // type (can happen with old saves or bad merges), find the correct one.
                if (splitHome == null || splitHome.StringId.StartsWith("hideout_seaside") != originalIsNaval)
                {
                    splitHome = Hideouts
                        .WhereQ(s => s.StringId.StartsWith("hideout_seaside") == originalIsNaval)
                        .OrderByQ(s => s.GatePosition.ToVec2().Distance(original.Position.ToVec2()))
                        .FirstOrDefault()
                        ?? original.HomeSettlement;
                }

                var bm1 = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(splitHome, heroes[0], original.ActualClan));
                var bm2 = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(splitHome, heroes.Count >= 2 ? heroes[1] : null, original.ActualClan));
                var rosters1 = new[] { party1, prisoners1 };
                var rosters2 = new[] { party2, prisoners2 };
                InitMilitia(bm1, rosters1, original.Position);
                InitMilitia(bm2, rosters2, original.Position);
                var avoidanceCopy = new Dictionary<Hero, float>(original.GetBM().Avoidance);
                bm1.GetBM().Avoidance = avoidanceCopy;
                bm2.GetBM().Avoidance = new Dictionary<Hero, float>(avoidanceCopy);
                Logger.LogDebug($"{original.Name}({original.StringId}) split into {bm1.Name}({bm1.StringId}) and {bm2.Name}({bm2.StringId})");
                ItemRoster(bm1.Party) = inventory1;
                ItemRoster(bm2.Party) = inventory2;
                bm1.Party.SetVisualAsDirty();
                bm2.Party.SetVisualAsDirty();
                Helper.Trash(original);
                DoPowerCalculations();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error splitting {original?.StringId}");
                if (original?.IsActive == true) Helper.Trash(original);
            }
        }

        // ── Initialisation ────────────────────────────────────────────────────────

        internal static void InitMilitia(MobileParty militia, TroopRoster[] rosters, CampaignVec2 position)
        {
            try
            {
                if (militia.ActualClan?.HasNavalNavigationCapability == true
                    && militia.Party.Ships.Count == 0)
                {
                    var hulls = militia.ActualClan.DefaultPartyTemplate.ShipHulls;
                    if (hulls != null && hulls.Count > 0)
                    {
                        int troopCount = rosters[0].TotalManCount;
                        int capacity = 0;
                        int safetyLimit = 50;
                        while (capacity < troopCount && safetyLimit-- > 0)
                        {
                            var stack = hulls.GetRandomElement();
                            new Ship(stack.ShipHull) { Owner = militia.Party };
                            capacity += stack.ShipHull.TotalCrewCapacity;
                        }
                        Logger.LogTrace($"{militia.Name}({militia.StringId}) assigned {militia.Party.Ships.Count} ships for {troopCount} troops (capacity={capacity}).");
                    }
                }

                militia.InitializeMobilePartyAtPosition(rosters[0], rosters[1], position);
                ConfigureMilitia(militia);
                TrainMilitia(militia);

                if (militia.HasNavalNavigationCapability)
                {
                    militia.SetLandNavigationAccess(false);
                    militia.DesiredAiNavigationType = MobileParty.NavigationType.Naval;

                    // Vanilla PiratesCampaignBehavior.GetSpawnPosition always resolves
                    // the patrol anchor via NavigationHelper with NavigationType.Naval,
                    // guaranteeing a valid sea navmesh face. We do the same here so that
                    // SpawnBM's GatePosition (which may be coastal) is never used raw.
                    var navalPatrolPos = NavigationHelper.FindPointAroundPosition(
                        position, MobileParty.NavigationType.Naval, 20f, 0f, true, false);

                    militia.GetBM().NavalPatrolPosition = navalPatrolPos;
                    militia.SetMovePatrolAroundPoint(navalPatrolPos, MobileParty.NavigationType.Naval);
                }

                Logger.LogTrace($"{militia.Name}({militia.StringId})[{militia.ActualClan}] initialized with {militia.MemberRoster.TotalRegulars} troops and {militia.MemberRoster.TotalHeroes} heroes. [{militia.MemberRoster.GetTroopRoster()
                    .WhereQ(t => t.Character.IsHero)
                    .SelectQ(t => t.Character.HeroObject.Name.ToString()).Join()}]");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to initialize militia {militia?.StringId}");
                if (militia?.IsActive == true)
                    Helper.Trash(militia);
                throw;
            }
        }

        private static void ConfigureMilitia(MobileParty mobileParty)
        {
            Logger.LogDebug($"ConfigureMilitia: START {mobileParty?.StringId}, LeaderHero={mobileParty?.LeaderHero?.Name.ToString() ?? "NULL"}, GetBM().Leader={mobileParty?.GetBM()?.Leader?.Name.ToString() ?? "NULL"}");

            mobileParty.LeaderHero.Gold = Convert.ToInt32(mobileParty.Party.EstimatedStrength * GoldMap.ElementAt(Globals.Settings.GoldReward.SelectedIndex).Value);
            Logger.LogDebug($"ConfigureMilitia: Gold set");

            CharacterObject leaderCharacterObject = mobileParty.GetBM().Leader.CharacterObject;
            Logger.LogDebug($"ConfigureMilitia: leaderCharacterObject={leaderCharacterObject?.StringId ?? "NULL"}");

            if (mobileParty.MemberRoster.Contains(leaderCharacterObject))
                mobileParty.MemberRoster.RemoveTroop(leaderCharacterObject);
            mobileParty.MemberRoster.AddToCounts(leaderCharacterObject, 1, true);
            Logger.LogDebug($"ConfigureMilitia: roster updated");

            if (MBRandom.RandomInt(0, 2) == 0 && Mounts.Count > 0)
            {
                var mount = Mounts.GetRandomElement();
                if (mount?.HorseComponent?.Monster is null) return;

                var saddles = mount.HorseComponent.Monster.MonsterUsage == "camel" ? CamelSaddles : NonCamelSaddles;
                if (saddles.Count > 0)
                {
                    var leader = mobileParty.GetBM()?.Leader;
                    if (leader is null) return;

                    leader.BattleEquipment[10] = new EquipmentElement(mount);
                    leader.BattleEquipment[11] = new EquipmentElement(saddles.GetRandomElement());
                }
            }
            Logger.LogDebug($"ConfigureMilitia: DONE");
        }

        internal static void TrainMilitia(MobileParty mobileParty)
        {
            try
            {
                if (!Globals.Settings.CanTrain || MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent)
                    return;

                int iterations = default;
                switch (Globals.Settings.XpGift.SelectedValue)
                {
                    case "Off":
                        break;
                    case "Normal":
                        iterations = 1;
                        break;
                    case "Hard":
                        iterations = 2;
                        break;
                    case "Hardest":
                        iterations = 4;
                        break;
                }

                int number, numberToUpgrade;
                if (Globals.Settings.UpgradeUnitsPercent > 0)
                {
                    var allLooters = mobileParty.MemberRoster.GetTroopRoster()
                        .WhereQ(e => e.Character == Looters.BasicTroop).ToList();
                    if (allLooters.Any())
                    {
                        var culture = Helper.GetMostPrevalentFromNearbySettlements(mobileParty.Position.ToVec2());
                        foreach (var looter in allLooters)
                        {
                            number = looter.Number;
                            numberToUpgrade = Convert.ToInt32(number * Globals.Settings.UpgradeUnitsPercent / 100f);
                            if (numberToUpgrade == 0)
                                continue;

                            mobileParty.MemberRoster.AddToCounts(Globals.Looters.BasicTroop, -numberToUpgrade);
                            var recruit = Globals.Recruits[culture][MBRandom.RandomInt(0, Globals.Recruits[culture].Count)];
                            mobileParty.MemberRoster.AddToCounts(recruit, numberToUpgrade);
                        }
                    }
                }

                var troopUpgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                for (var i = 0; i < iterations && Globals.MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent; i++)
                {
                    var validTroops = mobileParty.MemberRoster.GetTroopRoster().WhereQ(e =>
                        e.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !e.Character.IsHero
                        && !e.Character.Name.ToString().StartsWith("Glorious")
                        && troopUpgradeModel.IsTroopUpgradeable(mobileParty.Party, e.Character));
                    var validTroopsList = validTroops.ToList();
                    if (validTroopsList.Count == 0)
                        break;

                    var troopToTrain = validTroopsList.GetRandomElement();
                    number = troopToTrain.Number;
                    if (number < 1)
                        continue;

                    var minNumberToUpgrade = Convert.ToInt32(Globals.Settings.UpgradeUnitsPercent * 0.01f * number * MBRandom.RandomFloat);
                    minNumberToUpgrade = Math.Max(1, minNumberToUpgrade);
                    numberToUpgrade = Convert.ToInt32((number + 1) / 2f);
                    numberToUpgrade = numberToUpgrade > minNumberToUpgrade ? Convert.ToInt32(MBRandom.RandomInt(minNumberToUpgrade, numberToUpgrade)) : minNumberToUpgrade;
                    var xpGain = numberToUpgrade * DifficultyXpMap.ElementAt(Globals.Settings.XpGift.SelectedIndex).Value;
                    mobileParty.MemberRoster.AddXpToTroop(troopToTrain.Character, xpGain);
                    UpgraderCampaignBehavior ??= Campaign.Current.GetCampaignBehavior<PartyUpgraderCampaignBehavior>();
                    UpgraderCampaignBehavior.UpgradeReadyTroops(mobileParty.Party);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Bandit Militias is failing to configure parties!");
                Helper.Trash(mobileParty);
            }
        }
    }
}