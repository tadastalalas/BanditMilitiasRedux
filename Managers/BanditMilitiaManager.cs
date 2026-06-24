using System;
using System.Collections.Generic;
using System.Linq;
using BanditMilitiasRedux.Behaviours;
using BanditMilitiasRedux.Constructors;
using BanditMilitiasRedux.Helpers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.TwoDimension;
using static BanditMilitiasRedux.Globals;

namespace BanditMilitiasRedux.Managers
{
    internal static class BanditMilitiaManager
    {
        private static PartyUpgraderCampaignBehavior? _upgraderCampaignBehavior;

        internal static void ResetUpgraderBehavior() => _upgraderCampaignBehavior = null;

        private static readonly HashSet<string> VerbotenParties =
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
        
        internal static void TryMergeBanditParties(MobileParty mergeInitiatorParty, MobileParty mergeTargetParty)
        {
            int leaderMergeCounter = 0;
            Hero? initiatorPartyLeader = null;
            Hero? targetPartyLeader = null;
            Hero? newLeader = null;
            Settlement? homeSettlement = null;
            Clan? clanForNewLeaderHero = mergeInitiatorParty.ActualClan ?? mergeTargetParty.ActualClan ?? Clan.BanditFactions.FirstOrDefault();
            Dictionary<Hero, float> mergedAvoidances = new Dictionary<Hero, float>();

            if (mergeInitiatorParty.IsBanditMilitiaParty())
            {
                initiatorPartyLeader = mergeInitiatorParty.LeaderHero;
                AvoidanceManager.CalculateAverageAvoidance(mergeInitiatorParty.GetBanditMilitiaParty(), mergedAvoidances);
                leaderMergeCounter++;
            }

            if (mergeTargetParty.IsBanditMilitiaParty())
            {
                targetPartyLeader = mergeTargetParty.LeaderHero;
                AvoidanceManager.CalculateAverageAvoidance(mergeTargetParty.GetBanditMilitiaParty(), mergedAvoidances);
                leaderMergeCounter++;
            }

            if (leaderMergeCounter > 0)
            {
                Hero mergedLeader = SelectMergeLeader(initiatorPartyLeader, targetPartyLeader);
                newLeader = mergedLeader;
            }

            if (newLeader is null)
            {
                homeSettlement = Helper.BestSettlementForMerge(mergeInitiatorParty, mergeTargetParty);
                newLeader = BanditHeroCreator.ReuseOrCreateBanditHero(homeSettlement, clanForNewLeaderHero);
                if (newLeader is null)
                    return;
            }
            else
            {
                newLeader.PartyBelongedTo?.MemberRoster.RemoveTroop(newLeader.CharacterObject);
                homeSettlement ??= newLeader.BornSettlement ?? Helper.BestSettlementForMerge(mergeInitiatorParty, mergeTargetParty);
            }

            MobileParty banditMilitiaParty = CreateBanditMilitiaParty(newLeader, homeSettlement, mergedAvoidances);

            CopyInventoryFromTo(mergeInitiatorParty, banditMilitiaParty);
            CopyInventoryFromTo(mergeTargetParty, banditMilitiaParty);

            TroopRoster[] mergedRoster = MergeRosters(mergeInitiatorParty, mergeTargetParty);

            InitializeMilitiaParty(banditMilitiaParty, newLeader, mergedRoster, mergeInitiatorParty.Position);

            Helper.TryTrashMobilePartySafelyReservingBanditLeader(mergeInitiatorParty);
            Helper.TryTrashMobilePartySafelyReservingBanditLeader(mergeTargetParty);
        }

        private static TroopRoster[] MergeRosters(MobileParty sourceParty, MobileParty targetParty)
        {
            TroopRoster mergedTroops = TroopRoster.CreateDummyTroopRoster();
            TroopRoster mergedPrisoners = TroopRoster.CreateDummyTroopRoster();
            AddNonHeroMembers(mergedTroops, sourceParty.MemberRoster);
            AddNonHeroMembers(mergedTroops, targetParty.MemberRoster);
            AddPrisonersExcludingPlayer(mergedPrisoners, sourceParty.PrisonRoster);
            AddPrisonersExcludingPlayer(mergedPrisoners, targetParty.PrisonRoster);
            return [mergedTroops, mergedPrisoners];
        }

        private static void AddNonHeroMembers(TroopRoster destination, TroopRoster source)
        {
            foreach (TroopRosterElement rosterElement in source.GetTroopRoster())
                if (rosterElement.Character is { IsHero: false })
                    destination.AddToCounts(rosterElement.Character, rosterElement.Number, false,
                        rosterElement.WoundedNumber, rosterElement.Xp);
        }

        private static void AddPrisonersExcludingPlayer(TroopRoster destination, TroopRoster source)
        {
            foreach (var rosterElement in source.GetTroopRoster()
                         .Where(rosterElement => !rosterElement.Character.IsPlayerCharacter))
                destination.AddToCounts(rosterElement.Character, rosterElement.Number, false,
                    rosterElement.WoundedNumber, rosterElement.Xp);
        }

        private static Hero SelectMergeLeader(Hero? initiatorLeaderHero, Hero? targetLeaderHero)
        {
            if (initiatorLeaderHero is null)
                return targetLeaderHero;
            if (targetLeaderHero is null)
                return initiatorLeaderHero;
            return initiatorLeaderHero.Power >= targetLeaderHero.Power ? initiatorLeaderHero : targetLeaderHero;
        }

        private static MobileParty CreateBanditMilitiaParty(Hero ownerHero, Settlement homeSettlement, Dictionary<Hero, float> mergedAvoidances)
        {
            return MobileParty.CreateParty("Bandit_Militia", new BanditMilitiaPartyComponent(ownerHero, homeSettlement, mergedAvoidances));
        }

        private static void CopyInventoryFromTo(MobileParty source, MobileParty destination)
        {
            ItemRoster sourceInventory = source.ItemRoster;
            ItemRoster destinationInventory = destination.ItemRoster;

            foreach (ItemRosterElement item in sourceInventory)
            {
                if (item.IsEmpty)
                    continue;

                var itemObject = item.EquipmentElement.Item;

                if (itemObject == null)
                    continue;

                destinationInventory.AddToCounts(item.EquipmentElement, item.Amount);
                sourceInventory.AddToCounts(item.EquipmentElement, -item.Amount);
            }
        }

        private static void InitializeMilitiaParty(MobileParty newBanditMilitiaParty, Hero newLeader,
            TroopRoster[] mergedRoster, CampaignVec2 position)
        {
            newBanditMilitiaParty.InitializeMobilePartyAtPosition(mergedRoster[0], mergedRoster[1], position);
            newBanditMilitiaParty.ChangePartyLeader(newLeader);
            ConfigureGoldAndMounts(newBanditMilitiaParty);
            TrainBanditMilitiaParty(newBanditMilitiaParty);
        }

        private static void ConfigureGoldAndMounts(MobileParty newBanditMilitiaParty)
        {
            Hero leaderHero = newBanditMilitiaParty.GetBanditMilitiaParty().Leader;

            // Adding gold on purpose, let's see later if this will snowball.
            leaderHero.Gold += Convert.ToInt32(newBanditMilitiaParty.Party.EstimatedStrength * GoldValues[Settings.GoldReward.SelectedIndex]);

            if (MBRandom.RandomInt(0, 2) != 0 || Mounts.Count <= 0)
                return;

            ItemObject mount = Mounts.GetRandomElement();
            if (mount?.HorseComponent?.Monster is null)
                return;

            List<ItemObject> saddles = mount.HorseComponent.Monster.MonsterUsage == "camel" ? CamelSaddles : NonCamelSaddles;
            if (saddles.Count <= 0)
                return;

            leaderHero.BattleEquipment[10] = new EquipmentElement(mount);
            leaderHero.BattleEquipment[11] = new EquipmentElement(saddles.GetRandomElement());
        }

        public static void TrainBanditMilitiaParty(MobileParty mobileParty)
        {
            if (!Settings.CanTrain || MilitiaPowerPercent > Settings.GlobalPowerPercent)
                return;

            int iterations = Settings.XpGift.SelectedIndex switch { 0 => 0, 1 => 1, 2 => 2, 3 => 4, _ => 1, };

            int number, numberToUpgrade;
            if (Settings.UpgradeUnitsPercent > 0)
            {
                List<TroopRosterElement> allLooters = mobileParty.MemberRoster.GetTroopRoster()
                    .WhereQ(looter => looter.Character == Looters?.BasicTroop).ToList();

                if (allLooters.Count > 0)
                {
                    var culture = Helper.GetMostPrevalentFromNearbySettlements(mobileParty.Position.ToVec2());

                    if (Recruits.TryGetValue(culture, out var cultureRecruits) && cultureRecruits.Count > 0)
                    {
                        foreach (var looter in allLooters)
                        {
                            number = looter.Number;
                            numberToUpgrade = Convert.ToInt32(number * Settings.UpgradeUnitsPercent / 100f);
                            if (numberToUpgrade == 0)
                                continue;

                            mobileParty.MemberRoster.AddToCounts(Looters?.BasicTroop, -numberToUpgrade);
                            var recruit = cultureRecruits[MBRandom.RandomInt(0, cultureRecruits.Count)];
                            mobileParty.MemberRoster.AddToCounts(recruit, numberToUpgrade);
                        }
                    }
                }
            }

            _upgraderCampaignBehavior ??= Campaign.Current.GetCampaignBehavior<PartyUpgraderCampaignBehavior>();
            var troopUpgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
            var validTroopsList = new List<TroopRosterElement>(mobileParty.MemberRoster.Count);

            for (var i = 0; i < iterations && MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent; i++)
            {
                validTroopsList.Clear();
                foreach (var troop in mobileParty.MemberRoster.GetTroopRoster())
                {
                    var troopCharacter = troop.Character;
                    if (troopCharacter.Tier >= Settings.MaxTrainingTier)
                        continue;
                    if (troopCharacter.IsHero)
                        continue;
                    if (IsGloriousName(troopCharacter.Name))
                        continue;
                    if (!troopUpgradeModel.IsTroopUpgradeable(mobileParty.Party, troopCharacter))
                        continue;
                    validTroopsList.Add(troop);
                }

                if (validTroopsList.Count == 0)
                    break;

                var troopToTrain = validTroopsList[MBRandom.RandomInt(0, validTroopsList.Count)];
                number = troopToTrain.Number;
                if (number < 1)
                    continue;

                var minNumberToUpgrade =
                    Convert.ToInt32(Settings.UpgradeUnitsPercent * 0.01f * number * MBRandom.RandomFloat);
                minNumberToUpgrade = Math.Max(1, minNumberToUpgrade);
                numberToUpgrade = Convert.ToInt32((number + 1) / 2f);
                numberToUpgrade = numberToUpgrade > minNumberToUpgrade
                    ? Convert.ToInt32(MBRandom.RandomInt(minNumberToUpgrade, numberToUpgrade))
                    : minNumberToUpgrade;

                int xpGain = numberToUpgrade * DifficultyXpValues[Settings.XpGift.SelectedIndex];
                mobileParty.MemberRoster.AddXpToTroop(troopToTrain.Character, xpGain);
            }

            _upgraderCampaignBehavior.UpgradeReadyTroops(mobileParty.Party);
        }

        internal static void TrySplitBanditMilitiaParty(MobileParty mobilePartyToSplit)
        {
            if (MilitiaPowerPercent > Settings.GlobalPowerPercent || mobilePartyToSplit.Party.MemberRoster.TotalManCount / 2 < Settings.MinPartySize || mobilePartyToSplit.IsTooBusyToGrowOrSplit())
                return;
            if (MBRandom.RandomInt(0, 101) > Settings.RandomSplitChance|| mobilePartyToSplit.Party.MemberRoster.TotalManCount < Math.Max(1, CalculatedMaxPartySize * MinSizeRatioForSplit))
                return;
            
            Hero? partyLeaderOne = mobilePartyToSplit.LeaderHero;
            Hero? partyLeaderTwo = ReusableHeroesBehavior.Instance?.TryGetReusableBanditHero(mobilePartyToSplit.ActualClan);
            
            if (partyLeaderOne is null || partyLeaderTwo is null)
                return;
            
            var troopRosterOne = TroopRoster.CreateDummyTroopRoster();
            var troopRosterTwo = TroopRoster.CreateDummyTroopRoster();
            
            var prisonerRosterOne = TroopRoster.CreateDummyTroopRoster();
            var prisonerRosterTwo = TroopRoster.CreateDummyTroopRoster();
            
            var itemRosterOne = new ItemRoster();
            var itemRosterTwo = new ItemRoster();
            
            SplitRosters(mobilePartyToSplit, troopRosterOne, troopRosterTwo, prisonerRosterOne, prisonerRosterTwo, itemRosterOne, itemRosterTwo);
            
            List<Hero> otherHeroes = mobilePartyToSplit.MemberRoster.GetTroopRoster()
                .WhereQ(t => t.Character.IsHero && t.Character.HeroObject != partyLeaderOne && !t.Character.IsPlayerCharacter)
                .SelectQ(t => t.Character.HeroObject)
                .OrderByDescending(h => h.Power)
                .ToListQ();
            
            for (int i = otherHeroes.Count - 1; i >= 0; i--)
            {
                TroopRoster targetParty = i % 2 == 0 ? troopRosterOne : troopRosterTwo;
                targetParty.AddToCounts(otherHeroes[i].CharacterObject, 1, true);
            }
            
            CreateSplitMilitiaParties(mobilePartyToSplit, troopRosterOne, troopRosterTwo, prisonerRosterOne, prisonerRosterTwo, itemRosterOne, itemRosterTwo, partyLeaderOne, partyLeaderTwo);
        }

        private static void SplitRosters(MobileParty mobilePartyToSplit, TroopRoster troopRosterOne, TroopRoster troopRosterTwo,
            TroopRoster prisonerRosterOne, TroopRoster prisonerRosterTwo, ItemRoster itemRosterOne, ItemRoster itemRosterTwo)
        {
            foreach (var rosterElement in mobilePartyToSplit.MemberRoster.GetTroopRoster().WhereQ(x => x.Character.HeroObject is null))
                SplitRosters(troopRosterOne, troopRosterTwo, rosterElement);

            mobilePartyToSplit.MemberRoster.Clear();

            if (mobilePartyToSplit.PrisonRoster.TotalManCount > 0)
                foreach (var rosterElement in mobilePartyToSplit.PrisonRoster.GetTroopRoster().WhereQ(x => x.Character.HeroObject is null))
                    SplitRosters(prisonerRosterOne, prisonerRosterTwo, rosterElement);

            mobilePartyToSplit.PrisonRoster.Clear();

            foreach (var item in mobilePartyToSplit.ItemRoster)
            {
                if (string.IsNullOrEmpty(item.EquipmentElement.Item?.Name?.ToString()))
                    continue;

                int half = Math.Max(1, item.Amount / 2);
                itemRosterOne.AddToCounts(item.EquipmentElement, half);
                int remainder = item.Amount % 2;
                itemRosterTwo.AddToCounts(item.EquipmentElement, half + remainder);
            }
            mobilePartyToSplit.ItemRoster.Clear();
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
                int half = rosterElement.Number / 2;
                roster1.AddToCounts(rosterElement.Character, half);
                int remainder = rosterElement.Number % 2;
                roster2.AddToCounts(rosterElement.Character, Math.Max(1, half + remainder));
            }
        }

        private static void CreateSplitMilitiaParties(MobileParty mobilePartyToSplit, TroopRoster troopRosterOne, TroopRoster troopRosterTwo, TroopRoster prisonerRosterOne, TroopRoster prisonerRosterTwo, ItemRoster itemRosterOne, ItemRoster itemRosterTwo, Hero partyLeaderOne, Hero partyLeaderTwo)
        {
            MobileParty newPartyOne = CreateBanditMilitiaParty(partyLeaderOne, partyLeaderOne.BornSettlement, mobilePartyToSplit.GetBanditMilitiaParty().Avoidance);
            MobileParty newPartyTwo = CreateBanditMilitiaParty(partyLeaderTwo, partyLeaderTwo.BornSettlement, mobilePartyToSplit.GetBanditMilitiaParty().Avoidance);

            newPartyOne.ItemRoster.Add(itemRosterOne);
            newPartyTwo.ItemRoster.Add(itemRosterTwo);
            TroopRoster[] newRosterOne = [troopRosterOne, prisonerRosterOne];
            TroopRoster[] newRosterTwo = [troopRosterTwo, prisonerRosterTwo];

            InitializeMilitiaParty(newPartyOne, partyLeaderOne, newRosterOne, mobilePartyToSplit.Position);
            InitializeMilitiaParty(newPartyTwo, partyLeaderTwo, newRosterTwo, mobilePartyToSplit.Position);

            Helper.TryTrashMobilePartySafelyReservingBanditLeader(mobilePartyToSplit);
        }

        // Not sure if this is needed.
        /*
        internal static void DisperseLeaderlessMilitia(MobileParty mobileParty)
        {
            if (mobileParty?.IsActive != true || !mobileParty.IsBanditMilitiaParty())
                return;

            var clan = mobileParty.ActualClan ?? Clan.BanditFactions.FirstOrDefault();
            var hideout = Helper.ResolveHideout(mobileParty);
            if (clan is null || hideout is null)
            {
                Helper.TryTrashMobilePartySafelyReservingBanditLeader(mobileParty);
                return;
            }

            // Too small to split: degrade this one party to a vanilla bandit party in place (rosters kept).
            if (mobileParty.MemberRoster.TotalManCount < Globals.Settings.MinPartySize * 2)
            {
                BanditPartyComponent.ConvertPartyToBanditParty(mobileParty, clan, hideout, false);
                mobileParty.Party.SetVisualAsDirty();
                return;
            }

            var members1 = TroopRoster.CreateDummyTroopRoster();
            var members2 = TroopRoster.CreateDummyTroopRoster();
            var prisoners1 = TroopRoster.CreateDummyTroopRoster();
            var prisoners2 = TroopRoster.CreateDummyTroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();

            var position = mobileParty.Position;
            SplitRosters(mobileParty, members1, members2, prisoners1, prisoners2, inventory1, inventory2);

            // Original becomes the first vanilla bandit party (its rosters were cleared by SplitRosters).
            BanditPartyComponent.ConvertPartyToBanditParty(mobileParty, clan, hideout, false);
            FillBanditParty(mobileParty, members1, prisoners1, inventory1);
            mobileParty.Party.SetVisualAsDirty();

            // Second vanilla bandit party.
            var second =
                BanditPartyComponent.CreateBanditParty(clan.StringId + "_1", clan, hideout, false, null, position);
            second.ActualClan = clan;
            FillBanditParty(second, members2, prisoners2, inventory2);
            second.Party.SetVisualAsDirty();
        }

        private static void FillBanditParty(MobileParty party, TroopRoster members, TroopRoster prisoners,
            ItemRoster inventory)
        {
            if (members.TotalManCount > 0) party.MemberRoster.Add(members);
            if (prisoners.TotalManCount > 0) party.PrisonRoster.Add(prisoners);
            foreach (var item in inventory)
                if (!item.IsEmpty)
                    party.ItemRoster.AddToCounts(item.EquipmentElement, item.Amount);
        }
        */

        internal static void Think(MobileParty banditMilitiaParty)
        {
            if (banditMilitiaParty?.Ai is null || banditMilitiaParty.Ai.IsDisabled || banditMilitiaParty.Ai.DoNotMakeNewDecisions)
                return;

            switch (banditMilitiaParty.DefaultBehavior)
            {
                case AiBehavior.None:
                case AiBehavior.Hold:
                if (banditMilitiaParty.TargetSettlement is null)
                {
                    var settlementsInRange = Settlement.StartFindingLocatablesAroundPosition(banditMilitiaParty.Position.ToVec2(), SettlementFindRange);
                    Settlement? chosen = null;
                    int seen = 0;
                    for (var settlement = Settlement.FindNextLocatable(ref settlementsInRange);
                         settlement != null;
                         settlement = Settlement.FindNextLocatable(ref settlementsInRange))
                    {
                        if (settlement.IsHideout || settlement.IsTown || settlement.IsCastle)
                            continue;
                        seen++;
                        if (MBRandom.RandomInt(seen) == 0)
                            chosen = settlement;
                    }
                    if (chosen is not null)
                        banditMilitiaParty.SetMovePatrolAroundSettlement(chosen, MobileParty.NavigationType.Default, false);
                }
                break;
                case AiBehavior.GoToSettlement:
                if (banditMilitiaParty.TargetSettlement?.IsHideout == true)
                {
                    var partyPos2D = banditMilitiaParty.Position.ToVec2();
                    var targetPos2D = banditMilitiaParty.TargetSettlement.Position.ToVec2();
                    if (!banditMilitiaParty.IsEngaging &&
                        partyPos2D.DistanceSquared(targetPos2D) < ArrivedAtHideoutEpsilonSq)
                        banditMilitiaParty.SetMoveModeHold();
                }
                break;
                case AiBehavior.PatrolAroundPoint:
                var banditMilitiaPartyComponent = banditMilitiaParty.GetBanditMilitiaParty();
                if (banditMilitiaParty.MapFaction is null)
                    return;

                if (Settings.AllowPillaging && banditMilitiaParty.Party.EstimatedStrength > MilitiaPartyAveragePower && MBRandom.RandomFloat < Settings.PillagingChance * 0.01f)
                {
                    Vec2 militiaPosition = banditMilitiaParty.Position.ToVec2();
                    Settlement nearest = null;
                    float nearestDistSq = float.MaxValue;
                    List<Settlement> villages = Villages;
                    for (int i = 0, n = villages.Count; i < n; i++)
                    {
                        Settlement villageSettlement = villages[i];
                        
                        Village.VillageStates villageState = villageSettlement.Village.VillageState;
                        if (villageState is Village.VillageStates.BeingRaided or Village.VillageStates.Looted)
                            continue;
                        
                        if (villageSettlement.Owner is null)
                            continue;
                        
                        if (villageSettlement.MapFaction is null || !villageSettlement.MapFaction.IsAtWarWith(banditMilitiaParty.MapFaction))
                            continue;
                        
                        if (villageSettlement.GetValue() <= 0)
                            continue;

                        float distanceToVillageSq = villageSettlement.GatePosition.ToVec2().DistanceSquared(militiaPosition);
                        if (distanceToVillageSq >= nearestDistSq)
                            continue;
                        
                        nearestDistSq = distanceToVillageSq;
                        nearest = villageSettlement;
                    }
                    Settlement nearestVillage = nearest;

                    int raidingCount = 0;
                    var cachedBanditMilitias = PowerCalculationManager.GetActiveBanditMilitiaParties();
                    for (int i = 0, n = cachedBanditMilitias.Count; i < n; i++)
                    {
                        if (cachedBanditMilitias[i]?.MobileParty?.ShortTermBehavior == AiBehavior.RaidSettlement)
                            raidingCount++;
                    }

                    if (raidingCount >= RaidCap)
                        break;

                    if (nearestVillage.OwnerClan is not null && banditMilitiaPartyComponent.Avoidance is not null)
                    {
                        foreach (Hero villageClanHero in nearestVillage.OwnerClan.Heroes)
                        {
                            if (villageClanHero is null
                                || !banditMilitiaPartyComponent.Avoidance.TryGetValue(villageClanHero, out var avoidanceValue)
                                || MBRandom.RandomFloat * 100f > avoidanceValue)
                                continue;
                            nearestVillage = null;
                            break;
                        }

                        if (nearestVillage is null)
                            break;
                    }

                    banditMilitiaParty.SetMoveRaidSettlement(nearestVillage, MobileParty.NavigationType.Default, false);
                    banditMilitiaParty.Ai.SetDoNotMakeNewDecisions(true);

                    if (Hero.MainHero?.Clan is not null && nearestVillage.OwnerClan == Hero.MainHero.Clan)
                        InformationManager.DisplayMessage(new InformationMessage(
                            $"{banditMilitiaParty.Name} is raiding your village {nearestVillage.Name} near {nearestVillage.Town?.Name}!"));
                }
                break;
            }
        }

        internal static void TryGrowMilitiaParty(MobileParty mobileParty)
        {
            if (!CanGrowMilitiaParty(mobileParty) && !mobileParty.IsTooBusyToGrowOrSplit())
                return;
            
            int maxTrainingTier = Settings.MaxTrainingTier;
            List<TroopRosterElement> eligibleTroopsToGrow = mobileParty.MemberRoster.GetTroopRoster().WhereQ(rosterElement =>
                    rosterElement.Character is not null
                    && !rosterElement.Character.IsHero
                    && rosterElement.Character.Tier < maxTrainingTier
                    && !IsGloriousName(rosterElement.Character.Name))
                .ToListQ();

            if (!eligibleTroopsToGrow.Any())
                return;

            float growthTroopsAmountAllowed = mobileParty.MemberRoster.TotalManCount * (Settings.GrowthPercent / 100f);

            float troopsGrowthSmallBoost = GlobalMilitiaPower > 0 ? CalculatedGlobalPowerLimit / GlobalMilitiaPower : 1;
            growthTroopsAmountAllowed += Settings.GlobalPowerPercent / 100f * troopsGrowthSmallBoost;
            growthTroopsAmountAllowed = Mathf.Clamp(growthTroopsAmountAllowed, 1, 50);

            for (var i = 0; i < growthTroopsAmountAllowed && mobileParty.MemberRoster.TotalManCount + 1 < CalculatedMaxPartySize; i++)
            {
                var rosterElement = eligibleTroopsToGrow.GetRandomElement();
                if (rosterElement.Character is null)
                    continue;

                var troop = rosterElement.Character;
                if (GlobalMilitiaPower + troop.GetPower() < CalculatedGlobalPowerLimit)
                    mobileParty.MemberRoster.AddToCounts(troop, 1);
            }

            EquipmentPool.AdjustCavalryCount(mobileParty.MemberRoster);
        }

        private static bool CanGrowMilitiaParty(MobileParty mobileParty)
        {
            return Settings.GrowthPercent > 0
                   && MilitiaPowerPercent <= Settings.GlobalPowerPercent
                   && IsAvailableBanditParty(mobileParty)
                   && MBRandom.RandomFloat <= Settings.GrowthChance / 100f;
        }

        internal static bool TryHandleStuckMilitia(MobileParty mobileParty, Vec2 currentMilitiaPartyPosition)
        {
            if (StuckTracker.TryGetValue(mobileParty, out var stuckState))
            {
                if (currentMilitiaPartyPosition.DistanceSquared(stuckState.LastPos) < StuckDistanceThresholdSq)
                {
                    var newCount = stuckState.HourCount + 1;
                    StuckTracker[mobileParty] = (stuckState.LastPos, newCount);

                    if (newCount < StuckHourLimit)
                        return false;
                    
                    StuckTracker.Remove(mobileParty);

                    var escapePool = Hideouts
                        .WhereQ(s => s != null && s.GatePosition.ToVec2().DistanceSquared(currentMilitiaPartyPosition) > EscapeMinDistanceSq).ToListQ();

                    if (escapePool.Count == 0)
                        escapePool = Settlement.All.WhereQ(s => s is { IsTown: false, IsCastle: false }
                                                                && s.GatePosition.ToVec2().DistanceSquared(currentMilitiaPartyPosition) > EscapeMinDistanceSq).ToListQ();

                    if (escapePool.Count <= 0)
                        return false;
                    
                    var escapeTarget = escapePool.GetRandomElement();
                    mobileParty.SetMoveGoToPoint(escapeTarget.GatePosition, MobileParty.NavigationType.Default);
                    return true;
                }
                StuckTracker[mobileParty] = (currentMilitiaPartyPosition, 0);
            }
            else
            {
                StuckTracker[mobileParty] = (currentMilitiaPartyPosition, 0);
            }
            return false;
        }

        internal static void TryFindAndMergeNearbyBandits(MobileParty banditMilitiaParty, Vec2 currentMilitiaPartyPosition, CampaignTime timeNow, CampaignTime cooldownToMergeOrSplit)
        {
            if (!banditMilitiaParty.IsBanditMilitiaParty() && !CanProvideLeaderForMerge(banditMilitiaParty.ActualClan))
                return;
            
            List<MobileParty> nearbyBandits = NearbyBanditsBuffer;
            nearbyBandits.Clear();
            
            var locatableSearchData1 = MobileParty.StartFindingLocatablesAroundPosition(currentMilitiaPartyPosition, FindNearbyBanditsRadius);
            for (MobileParty nearbyLocatable = MobileParty.FindNextLocatable(ref locatableSearchData1); nearbyLocatable != null; nearbyLocatable = MobileParty.FindNextLocatable(ref locatableSearchData1))
            {
                if (nearbyLocatable == banditMilitiaParty)
                    continue;

                if (nearbyLocatable.HasNavalNavigationCapability)
                    continue;

                if (IsAvailableBanditParty(nearbyLocatable) && !nearbyLocatable.IsTooBusyToMerge())
                    nearbyBandits.Add(nearbyLocatable);
            }

            if (nearbyBandits.Count == 0)
            {
                if (banditMilitiaParty.IsBanditMilitiaParty())
                    Think(banditMilitiaParty);
                return;
            }

            int mobilePartyMountedCount = EquipmentPool.NumMountedTroops(banditMilitiaParty.MemberRoster);
            nearbyBandits.Sort((a, b) => a.Position.ToVec2().DistanceSquared(currentMilitiaPartyPosition).CompareTo(b.Position.ToVec2().DistanceSquared(currentMilitiaPartyPosition)));

            var mountedCountByParty = MountedCountBuffer;
            mountedCountByParty.Clear();

            MobileParty? mergeTarget = null;
            
            foreach (MobileParty target in nearbyBandits)
            {
                int militiaTotalCount = banditMilitiaParty.MemberRoster.TotalManCount + target.MemberRoster.TotalManCount;

                if (militiaTotalCount < Settings.MinPartySize || militiaTotalCount > CalculatedMaxPartySize)
                    continue;

                if (banditMilitiaParty.MapFaction is not null && target.MapFaction?.IsAtWarWith(banditMilitiaParty.MapFaction) == true)
                    continue;

                if (target.IsBanditMilitiaParty())
                {
                    BanditMilitiaPartyComponent targetBanditMilitiaParty = target.GetBanditMilitiaParty();
                    if (timeNow < targetBanditMilitiaParty.LastMergedOrSplitDate + cooldownToMergeOrSplit)
                        continue;
                }

                if (!mountedCountByParty.TryGetValue(target, out var targetMountedCount))
                {
                    targetMountedCount = EquipmentPool.NumMountedTroops(target.MemberRoster);
                    mountedCountByParty[target] = targetMountedCount;
                }

                if (mobilePartyMountedCount + targetMountedCount > militiaTotalCount / 2)
                    continue;

                if (MobileParty.MainParty.ShortTermBehavior == AiBehavior.EngageParty && MobileParty.MainParty.ShortTermTargetParty == target)
                    continue;

                mergeTarget = target;
                break;
            }

            if (mergeTarget is null)
            {
                if (banditMilitiaParty.IsBanditMilitiaParty())
                    Think(banditMilitiaParty);
                return;
            }

            if (Campaign.Current?.Models?.MapDistanceModel is null)
                return;
            
            float distanceToTargetSq = currentMilitiaPartyPosition.DistanceSquared(mergeTarget.Position.ToVec2());
            if (distanceToTargetSq <= MergeDistanceSq)
            {
                if (!CanMobilePartiesMerge(banditMilitiaParty, mergeTarget))
                    return;

                if (IfEngagedByThePlayer(banditMilitiaParty, mergeTarget))
                    return;
                
                TryMergeBanditParties(banditMilitiaParty, mergeTarget);
            }
            else if (banditMilitiaParty.TargetParty != mergeTarget)
            {
                banditMilitiaParty.SetMoveEngageParty(mergeTarget, MobileParty.NavigationType.Default);
                mergeTarget.SetMoveEngageParty(banditMilitiaParty, MobileParty.NavigationType.Default);
            }
        }

        internal static bool CanMobilePartiesMerge(MobileParty attackerMobileParty, MobileParty defenderMobileParty)
        {
            return MobileParty.MainParty != attackerMobileParty && MobileParty.MainParty != defenderMobileParty &&
                   attackerMobileParty.IsBandit && defenderMobileParty.IsBandit  &&
                   attackerMobileParty.IsActive && defenderMobileParty.IsActive &&
                   attackerMobileParty.MapEvent is null && defenderMobileParty.MapEvent is null &&
                   !FactionManager.IsAtWarAgainstFaction(attackerMobileParty.MapFaction, defenderMobileParty.MapFaction) &&
                   !attackerMobileParty.IsUsedByAQuest() && !defenderMobileParty.IsUsedByAQuest() &&
                   !attackerMobileParty.HasNavalNavigationCapability && !defenderMobileParty.HasNavalNavigationCapability;
        }

        internal static bool IfEngagedByThePlayer(MobileParty attackerParty, MobileParty defenderParty)
        {
            return MobileParty.MainParty.ShortTermBehavior == AiBehavior.EngageParty
                   && (MobileParty.MainParty.ShortTermTargetParty == attackerParty
                       || MobileParty.MainParty.ShortTermTargetParty == defenderParty);
        }
        
        internal static bool IsAvailableBanditParty(MobileParty mobileParty)
        {
            return mobileParty is { IsBandit: true, CurrentSettlement: null, MapEvent: null }
                   && mobileParty.Party.MemberRoster.TotalManCount > Settings.MergeableSize
                   && !mobileParty.IsUsedByAQuest()
                   && !VerbotenParties.Contains(mobileParty.StringId);
        }

        private static bool IsGloriousName(TextObject name)
        {
            string v = name.Value;
            return v != null && v.StartsWith("Glorious", StringComparison.Ordinal);
        }
        
        private static bool CanProvideLeaderForMerge(Clan? clan)
        {
            clan ??= Clan.BanditFactions.FirstOrDefault();
            if (clan is null)
                return false;

            if (ReusableHeroesBehavior.Instance?.HasReusableBanditHero(clan) == true)
                return true;

            return !Helper.IsHeroCapReached(clan);
        }
    }
}