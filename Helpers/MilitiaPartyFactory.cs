using System;
using System.Collections.Generic;
using System.Linq;
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
using TaleWorlds.Localization;
using static BanditMilitias.Globals;
using static BanditMilitias.Helpers.Helper;

namespace BanditMilitias.Helpers
{
    internal static class MilitiaPartyFactory
    {
        private const float ReductionFactor = 0.8f;
        private const float SplitDivisor = 2;
        private const float RemovedHero = 1;

        private static readonly AccessTools.FieldRef<PartyBase, ItemRoster> ItemRoster =
            AccessTools.FieldRefAccess<PartyBase, ItemRoster>("<ItemRoster>k__BackingField");

        private static PartyUpgraderCampaignBehavior UpgraderCampaignBehavior;

        internal static void ResetUpgraderBehavior() => UpgraderCampaignBehavior = null;

        private static readonly HashSet<string> verbotenParties =
        [
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
            "manhunter"
        ];

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

        private static Settlement FindClosestHideout(Vec2 position)
        {
            Settlement bestLand = null, bestAny = null;
            float bestLandDist = float.MaxValue, bestAnyDist = float.MaxValue;

            foreach (var s in Hideouts)
            {
                var d = s.GatePosition.ToVec2().DistanceSquared(position);
                if (d < bestAnyDist) { bestAnyDist = d; bestAny = s; }
                if (Helper.IsLandHideout(s) && d < bestLandDist) { bestLandDist = d; bestLand = s; }
            }

            return bestLand ?? bestAny;
        }

        internal static bool TryMergeParties(MobileParty mobileParty, MobileParty mergeTarget)
        {
            try
            {
                if (mobileParty?.IsActive != true || mergeTarget?.IsActive != true)
                    return false;

                if (!CanMergeNow(mobileParty) || !CanMergeNow(mergeTarget))
                {
                    mobileParty?.SetMoveModeHold();
                    mergeTarget?.SetMoveModeHold();
                    return false;
                }

                var rosters = MergeRosters(mobileParty, mergeTarget);

                if (rosters == null || rosters.Length != 2)
                    return false;
                
                Hero leaderHero = (mobileParty.LeaderHero?.Power ?? 0) >= (mergeTarget.LeaderHero?.Power ?? 0)
                    ? mobileParty.LeaderHero
                    : mergeTarget.LeaderHero;
                
                if (mobileParty.ActualClan?.HasNavalNavigationCapability == true
                    || mergeTarget.ActualClan?.HasNavalNavigationCapability == true)
                {
                    mobileParty.SetMoveModeHold();
                    mergeTarget.SetMoveModeHold();
                    return false;
                }

                Settlement mobilePartyHomeSettlement = mobileParty.HomeSettlement?.IsHideout ?? false ? mobileParty.HomeSettlement : null;
                Settlement mergeTargetHomeSettlement = mergeTarget.HomeSettlement?.IsHideout ?? false ? mergeTarget.HomeSettlement : null;
                Settlement leaderHomeSettlement = leaderHero?.HomeSettlement?.IsHideout ?? false ? leaderHero.HomeSettlement : null;

                Settlement bestSettlement = new[] { leaderHomeSettlement, mobilePartyHomeSettlement, mergeTargetHomeSettlement }
                    .FirstOrDefault(s => s != null && Helper.IsLandHideout(s));

                if (bestSettlement is null)
                {
                    bestSettlement = FindClosestHideout(mobileParty.Position.ToVec2());

                    if (bestSettlement is null)
                        return false;
                }

                var mergedClan = mobileParty.ActualClan
                    ?? mergeTarget.ActualClan
                    ?? Clan.BanditFactions.FirstOrDefault(c => c.Culture == bestSettlement.Culture)
                    ?? bestSettlement.OwnerClan;

                var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(bestSettlement, leaderHero, mergedClan));
                
                try
                {
                    InitMilitia(bm, rosters, mobileParty.Position);
                    
                    bm.DesiredAiNavigationType = MobileParty.NavigationType.Default;

                    var calculatedAvoidance = new Dictionary<Hero, float>();
                    void CalcAverageAvoidance(ModBanditMilitiaPartyComponent BM)
                    {
                        foreach (var entry in BM.Avoidance)
                        {
                            if (calculatedAvoidance.TryGetValue(entry.Key, out var existing))
                                calculatedAvoidance[entry.Key] = (existing + entry.Value) * 0.5f;
                            else
                                calculatedAvoidance[entry.Key] = entry.Value;
                        }
                    }

                    if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent BM1)
                        CalcAverageAvoidance(BM1);
                    if (mergeTarget.PartyComponent is ModBanditMilitiaPartyComponent BM2)
                        CalcAverageAvoidance(BM2);

                    bm.GetBM().Avoidance = calculatedAvoidance;

                    if (Globals.Settings.TestingMode)
                        MilitiaBehaviorService.TeleportMilitiasNearPlayer(bm);

                    CopyFoodTo(bm.ItemRoster, mobileParty.ItemRoster);
                    CopyFoodTo(bm.ItemRoster, mergeTarget.ItemRoster);

                    bm.Party.SetVisualAsDirty();
                    Helper.Trash(mobileParty);
                    Helper.Trash(mergeTarget);
                    PowerCalculationService.DoPowerCalculations();
                    return true;
                }
                catch (Exception)
                {
                    if (mobileParty?.IsActive == true) Helper.Trash(mobileParty);
                    if (mergeTarget?.IsActive == true) Helper.Trash(mergeTarget);
                    if (bm?.IsActive == true) Helper.Trash(bm);
                    throw;
                }
            }
            catch (Exception)
            {
                try { if (mobileParty?.IsActive == true) Helper.Trash(mobileParty); } catch { }
                try { if (mergeTarget?.IsActive == true) Helper.Trash(mergeTarget); } catch { }
                return false;
            }
        }

        private static void CopyFoodTo(ItemRoster destination, ItemRoster source)
        {
            foreach (var item in source)
            {
                if (item.IsEmpty) continue;
                var it = item.EquipmentElement.Item;
                if (it != null && it.IsFood)
                    destination.AddToCounts(item.EquipmentElement, item.Amount);
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

        internal static bool TrySplitParty(MobileParty mobileParty)
        {
            if (MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent
                || mobileParty.Party.MemberRoster.TotalManCount / SplitDivisor - RemovedHero < Globals.Settings.MinPartySize
                || !mobileParty.IsBM()
                || mobileParty.IsTooBusyToMerge())
            {
                return false;
            }

            if (mobileParty.ActualClan?.HasNavalNavigationCapability == true)
                return false;

            int roll = MBRandom.RandomInt(0, 101);

            if (roll > Globals.Settings.RandomSplitChance
                || mobileParty.Party.MemberRoster.TotalManCount < Math.Max(1, CalculatedMaxPartySize * ReductionFactor))
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
                .SelectQ(t => t.Character.HeroObject)
                .OrderByQ(h => h.Power)
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
                    continue;

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
                    return;

                if (original.HomeSettlement == null)
                {
                    Helper.Trash(original);
                    return;
                }

                original.MemberRoster.RemoveIf(t => t.Character.IsHero);

                for (int i = heroes.Count - 1; i >= 0; i--)
                {
                    TroopRoster targetParty = i % 2 == 0 ? party1 : party2;
                    targetParty.AddToCounts(heroes[i].CharacterObject, 1, true);
                }

                PadPartyToMinSize(party1);
                PadPartyToMinSize(party2);

                var splitHome = original.HomeSettlement;

                if (splitHome == null || !Helper.IsLandHideout(splitHome))
                    splitHome = FindClosestHideout(original.Position.ToVec2()) ?? original.HomeSettlement;

                var bm1 = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(splitHome, heroes[0], original.ActualClan));
                var bm2 = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(splitHome, heroes.Count >= 2 ? heroes[1] : null, original.ActualClan));
                var rosters1 = new[] { party1, prisoners1 };
                var rosters2 = new[] { party2, prisoners2 };
                InitMilitia(bm1, rosters1, original.Position);
                InitMilitia(bm2, rosters2, original.Position);
                var originalAvoidance = original.GetBM().Avoidance;
                bm1.GetBM().Avoidance = new Dictionary<Hero, float>(originalAvoidance);
                bm2.GetBM().Avoidance = new Dictionary<Hero, float>(originalAvoidance);
                ItemRoster(bm1.Party) = inventory1;
                ItemRoster(bm2.Party) = inventory2;
                bm1.Party.SetVisualAsDirty();
                bm2.Party.SetVisualAsDirty();
                Helper.Trash(original);
                PowerCalculationService.DoPowerCalculations();
            }
            catch (Exception)
            {
                if (original?.IsActive == true)
                    Helper.Trash(original);
            }
        }

        private static void PadPartyToMinSize(TroopRoster party)
        {
            int deficit = Globals.Settings.MinPartySize - party.TotalManCount;
            if (deficit <= 0 || party.Count == 0)
                return;

            const int maxTypes = 4;
            int typesToUse = Math.Min(maxTypes, party.Count);
            int baseShare = deficit / typesToUse;
            int remainder = deficit % typesToUse;

            var usedIndices = new HashSet<int>(typesToUse);
            var picks = new CharacterObject[typesToUse];
            int picked = 0;
            int attempts = 0;
            while (picked < typesToUse && attempts < party.Count * 2)
            {
                int idx = MBRandom.RandomInt(0, party.Count);
                if (usedIndices.Add(idx))
                {
                    picks[picked++] = party.GetCharacterAtIndex(idx);
                }
                attempts++;
            }
            typesToUse = picked;

            for (int i = 0; i < typesToUse; i++)
            {
                var troop = picks[i];
                if (troop == null) continue;

                int amount = baseShare + (i == 0 ? remainder : 0);
                if (amount > 0)
                    party.AddToCounts(troop, amount);
            }
        }

        internal static void InitMilitia(MobileParty militia, TroopRoster[] rosters, CampaignVec2 position)
        {
            try
            {
                militia.InitializeMobilePartyAtPosition(rosters[0], rosters[1], position);
                ConfigureMilitia(militia);
                TrainMilitia(militia);
            }
            catch (Exception)
            {
                if (militia?.IsActive == true)
                    Helper.Trash(militia);
                throw;
            }
        }

        private static void ConfigureMilitia(MobileParty mobileParty)
        {
            var bm = mobileParty.GetBM();
            if (mobileParty.LeaderHero is null || mobileParty.GetBM()?.Leader is null)
                return;

            mobileParty.LeaderHero.Gold = Convert.ToInt32(
                mobileParty.Party.EstimatedStrength * Globals.GoldValues[Globals.Settings.GoldReward.SelectedIndex]);
            
            CharacterObject leaderCharacterObject = bm.Leader.CharacterObject;
            
            if (mobileParty.MemberRoster.Contains(leaderCharacterObject))
                mobileParty.MemberRoster.RemoveTroop(leaderCharacterObject);
            mobileParty.MemberRoster.AddToCounts(leaderCharacterObject, 1, true);
            
            if (MBRandom.RandomInt(0, 2) == 0 && Mounts.Count > 0)
            {
                var mount = Mounts.GetRandomElement();
                if (mount?.HorseComponent?.Monster is null)
                    return;

                var saddles = mount.HorseComponent.Monster.MonsterUsage == "camel" ? CamelSaddles : NonCamelSaddles;
                if (saddles.Count > 0)
                {
                    var leader = bm.Leader;
                    if (leader is null)
                        return;

                    leader.BattleEquipment[10] = new EquipmentElement(mount);
                    leader.BattleEquipment[11] = new EquipmentElement(saddles.GetRandomElement());
                }
            }
        }

        internal static void TrainMilitia(MobileParty mobileParty)
        {
            try
            {
                if (!Globals.Settings.CanTrain || MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent)
                    return;

                var iterations = Globals.Settings.XpGift.SelectedIndex switch
                {
                    0 => 0,
                    1 => 1,
                    2 => 2,
                    3 => 4,
                    _ => 0,
                };

                int number, numberToUpgrade;
                if (Globals.Settings.UpgradeUnitsPercent > 0)
                {
                    var allLooters = mobileParty.MemberRoster.GetTroopRoster()
                        .WhereQ(e => e.Character == Looters.BasicTroop).ToList();

                    if (allLooters.Count > 0)
                    {
                        var culture = Helper.GetMostPrevalentFromNearbySettlements(mobileParty.Position.ToVec2());

                        if (Globals.Recruits.TryGetValue(culture, out var cultureRecruits) && cultureRecruits.Count > 0)
                        {
                            foreach (var looter in allLooters)
                            {
                                number = looter.Number;
                                numberToUpgrade = Convert.ToInt32(number * Globals.Settings.UpgradeUnitsPercent / 100f);
                                if (numberToUpgrade == 0)
                                    continue;

                                mobileParty.MemberRoster.AddToCounts(Globals.Looters.BasicTroop, -numberToUpgrade);
                                var recruit = cultureRecruits[MBRandom.RandomInt(0, cultureRecruits.Count)];
                                mobileParty.MemberRoster.AddToCounts(recruit, numberToUpgrade);
                            }
                        }
                    }
                }

                UpgraderCampaignBehavior ??= Campaign.Current.GetCampaignBehavior<PartyUpgraderCampaignBehavior>();
                var troopUpgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                var validTroopsList = new List<TroopRosterElement>(mobileParty.MemberRoster.Count);

                for (var i = 0; i < iterations && Globals.MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent; i++)
                {
                    validTroopsList.Clear();
                    foreach (var e in mobileParty.MemberRoster.GetTroopRoster())
                    {
                        var ch = e.Character;
                        if (ch.Tier >= Globals.Settings.MaxTrainingTier) continue;
                        if (ch.IsHero) continue;
                        if (IsGloriousName(ch.Name)) continue;
                        if (!troopUpgradeModel.IsTroopUpgradeable(mobileParty.Party, ch)) continue;
                        validTroopsList.Add(e);
                    }

                    if (validTroopsList.Count == 0)
                        break;

                    var troopToTrain = validTroopsList[MBRandom.RandomInt(0, validTroopsList.Count)];
                    number = troopToTrain.Number;
                    if (number < 1)
                        continue;

                    var minNumberToUpgrade = Convert.ToInt32(Globals.Settings.UpgradeUnitsPercent * 0.01f * number * MBRandom.RandomFloat);
                    minNumberToUpgrade = Math.Max(1, minNumberToUpgrade);
                    numberToUpgrade = Convert.ToInt32((number + 1) / 2f);
                    numberToUpgrade = numberToUpgrade > minNumberToUpgrade
                        ? Convert.ToInt32(MBRandom.RandomInt(minNumberToUpgrade, numberToUpgrade))
                        : minNumberToUpgrade;

                    var xpGain = numberToUpgrade * Globals.DifficultyXpValues[Globals.Settings.XpGift.SelectedIndex];
                    mobileParty.MemberRoster.AddXpToTroop(troopToTrain.Character, xpGain);
                    UpgraderCampaignBehavior.UpgradeReadyTroops(mobileParty.Party);
                }
            }
            catch (Exception)
            {
                Helper.Trash(mobileParty);
            }
        }

        private static bool IsGloriousName(TextObject name)
        {
            var v = name?.Value;
            return v != null && v.StartsWith("Glorious", StringComparison.Ordinal);
        }
    }
}