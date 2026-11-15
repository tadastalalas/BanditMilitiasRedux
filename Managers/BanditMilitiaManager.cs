using System;
using System.Collections.Generic;
using System.Linq;
using BanditMilitiasRedux.Behaviors;
using BanditMilitiasRedux.Compat;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Data;
using BanditMilitiasRedux.Diagnostics;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;
using TaleWorlds.TwoDimension;
using static BanditMilitiasRedux.Core.Globals;

namespace BanditMilitiasRedux.Managers;

// BMR-FEATURE: Militia merging, splitting, growth, training and map AI (patrol, raid, stuck rescue) | gate: always (details per setting)
internal static class BanditMilitiaManager
{
    private const string ReattachViolationTag = "BMR-REATTACH-VIOLATION";
    private const string StuckTag = "BMR-STUCK";

    private static PartyUpgraderCampaignBehavior? _upgraderCampaignBehavior;

    private static readonly List<TroopRosterElement> ValidTroopsBuffer = [];
    private static readonly List<TroopRosterElement> LootersBuffer = [];
    private static readonly List<TroopRosterElement> EligibleTroopsBuffer = [];
    private static readonly List<Hero> OtherHeroesBuffer = [];

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
        Dictionary<Hero, float> mergedAvoidances = new();

        if (mergeInitiatorParty.IsBanditMilitiaParty())
        {
            initiatorPartyLeader = mergeInitiatorParty.LeaderHero;
            AvoidanceManager.CalculateAverageAvoidance(mergeInitiatorParty.GetBanditMilitiaParty()!, mergedAvoidances);
            leaderMergeCounter++;
        }

        if (mergeTargetParty.IsBanditMilitiaParty())
        {
            targetPartyLeader = mergeTargetParty.LeaderHero;
            AvoidanceManager.CalculateAverageAvoidance(mergeTargetParty.GetBanditMilitiaParty()!, mergedAvoidances);
            leaderMergeCounter++;
        }

        if (leaderMergeCounter > 0)
            newLeader = SelectMergeLeader(initiatorPartyLeader, targetPartyLeader);

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
        foreach (TroopRosterElement rosterElement in source.GetTroopRoster())
        {
            if (rosterElement.Character.IsPlayerCharacter)
                continue;
            destination.AddToCounts(rosterElement.Character, rosterElement.Number, false,
                rosterElement.WoundedNumber, rosterElement.Xp);
        }
    }

    private static Hero SelectMergeLeader(Hero? initiatorLeaderHero, Hero? targetLeaderHero)
    {
        if (initiatorLeaderHero is null)
            return targetLeaderHero!;
        if (targetLeaderHero is null)
            return initiatorLeaderHero;
        return initiatorLeaderHero.Power >= targetLeaderHero.Power ? initiatorLeaderHero : targetLeaderHero;
    }

    private static MobileParty CreateBanditMilitiaParty(Hero ownerHero, Settlement homeSettlement, Dictionary<Hero, float> mergedAvoidances) =>
        MobileParty.CreateParty(Balance.BanditMilitiaPartyStringId, new BanditMilitiaPartyComponent(ownerHero, homeSettlement, mergedAvoidances));

    private static void CopyInventoryFromTo(MobileParty source, MobileParty destination)
    {
        ItemRoster sourceInventory = source.ItemRoster;
        ItemRoster destinationInventory = destination.ItemRoster;

        ItemRosterElement[] items = [.. sourceInventory];
        foreach (ItemRosterElement item in items)
        {
            if (item.IsEmpty)
                continue;

            ItemObject itemObject = item.EquipmentElement.Item;

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
        ReusableHeroesBehavior.RemoveHeroFromTheWaitingDictionary(newLeader);
        ConfigureGoldAndMounts(newBanditMilitiaParty);
        TrainBanditMilitiaParty(newBanditMilitiaParty);
        MilitiaTrackerManager.Refresh(newBanditMilitiaParty);
    }

    private static void ConfigureGoldAndMounts(MobileParty newBanditMilitiaParty)
    {
        BanditMilitiaPartyComponent? banditMilitiaParty = newBanditMilitiaParty.GetBanditMilitiaParty();
        if (banditMilitiaParty is null)
            return;

        Hero leaderHero = banditMilitiaParty.Leader;

        leaderHero.Gold += Convert.ToInt32(newBanditMilitiaParty.Party.EstimatedStrength * Balance.GoldValues[ModSettings.GoldRewardIndex]);

        if (MBRandom.RandomInt(0, Balance.LeaderMountRollRange) != 0 || Mounts.Count <= 0)
            return;

        ItemObject mount = Mounts.GetRandomElement();
        if (mount?.HorseComponent?.Monster is null)
            return;

        List<ItemObject> saddles = mount.HorseComponent.Monster.MonsterUsage == Balance.CamelMonsterUsage ? CamelSaddles : NonCamelSaddles;
        if (saddles.Count <= 0)
            return;

        // Equipment indices 10 / 11 = horse / horse harness.
        leaderHero.BattleEquipment[10] = new EquipmentElement(mount);
        leaderHero.BattleEquipment[11] = new EquipmentElement(saddles.GetRandomElement());
    }

    // Daily tick path (and on merge/split): reused buffers, no LINQ.
    internal static void TrainBanditMilitiaParty(MobileParty mobileParty)
    {
        if (!ModSettings.CanTrain || MilitiaPowerPercent > ModSettings.GlobalPowerPercent)
            return;

        int xpGiftIndex = ModSettings.XpGiftIndex;
        int[] iterationsByXpGift = Balance.TrainingIterationsByXpGift;
        int iterations = xpGiftIndex >= 0 && xpGiftIndex < iterationsByXpGift.Length ? iterationsByXpGift[xpGiftIndex] : Balance.TrainingIterationsFallback;
        bool banditOnly = ModSettings.UseBanditOnlyTroopTrees;

        ConvertLootersForTraining(mobileParty, banditOnly);

        _upgraderCampaignBehavior ??= Campaign.Current.GetCampaignBehavior<PartyUpgraderCampaignBehavior>();
        PartyTroopUpgradeModel troopUpgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
        List<TroopRosterElement> validTroopsList = ValidTroopsBuffer;
        int number, numberToUpgrade;
        int maxTrainingTier = ModSettings.MaxTrainingTier;

        for (int i = 0; i < iterations && MilitiaPowerPercent <= ModSettings.GlobalPowerPercent; i++)
        {
            validTroopsList.Clear();
            TroopRoster memberRoster = mobileParty.MemberRoster;
            for (int j = 0; j < memberRoster.Count; j++)
            {
                TroopRosterElement troop = memberRoster.GetElementCopyAtIndex(j);
                CharacterObject troopCharacter = troop.Character;
                if (troopCharacter.Tier >= maxTrainingTier)
                    continue;
                if (troopCharacter.IsHero)
                    continue;
                if (IsGloriousName(troopCharacter.Name))
                    continue;
                if (banditOnly ? SelectBanditUpgradeTarget(troopCharacter) is null : !troopUpgradeModel.IsTroopUpgradeable(mobileParty.Party, troopCharacter))
                    continue;
                validTroopsList.Add(troop);
            }

            if (validTroopsList.Count == 0)
                break;

            TroopRosterElement troopToTrain = validTroopsList[MBRandom.RandomInt(0, validTroopsList.Count)];
            number = troopToTrain.Number;
            if (number < 1)
                continue;

            int minNumberToUpgrade =
                Convert.ToInt32(ModSettings.UpgradeUnitsPercent * Balance.PercentToFraction * number * MBRandom.RandomFloat);
            minNumberToUpgrade = Math.Max(1, minNumberToUpgrade);
            numberToUpgrade = Convert.ToInt32((number + 1) / Balance.UpgradeHalfDivisor);
            numberToUpgrade = numberToUpgrade > minNumberToUpgrade
                ? Convert.ToInt32(MBRandom.RandomInt(minNumberToUpgrade, numberToUpgrade))
                : minNumberToUpgrade;

            int xpGain = numberToUpgrade * Balance.DifficultyXpValues[xpGiftIndex];
            mobileParty.MemberRoster.AddXpToTroop(troopToTrain.Character, xpGain);
        }
        validTroopsList.Clear();

        if (banditOnly)
            UpgradeReadyBanditTroops(mobileParty);
        else
            _upgraderCampaignBehavior.UpgradeReadyTroops(mobileParty.Party);
    }

    private static void ConvertLootersForTraining(MobileParty mobileParty, bool banditOnly)
    {
        if (ModSettings.UpgradeUnitsPercent <= 0)
            return;

        List<TroopRosterElement> allLooters = LootersBuffer;
        allLooters.Clear();
        TroopRoster memberRoster = mobileParty.MemberRoster;
        CharacterObject? looterTroop = Looters?.BasicTroop;
        for (int i = 0; i < memberRoster.Count; i++)
        {
            TroopRosterElement rosterElement = memberRoster.GetElementCopyAtIndex(i);
            if (rosterElement.Character == looterTroop)
                allLooters.Add(rosterElement);
        }

        if (allLooters.Count == 0)
            return;

        if (banditOnly)
            ConvertLootersToBanditTier(mobileParty, allLooters);
        else
            ConvertLootersToCultureRecruits(mobileParty, allLooters);
        allLooters.Clear();
    }

    private static void ConvertLootersToCultureRecruits(MobileParty mobileParty, List<TroopRosterElement> allLooters)
    {
        CultureObject culture = Helper.GetMostPrevalentFromNearbySettlements(mobileParty.Position.ToVec2());
        if (!Recruits.TryGetValue(culture, out List<CharacterObject>? cultureRecruits) || cultureRecruits.Count == 0)
            return;

        for (int i = 0; i < allLooters.Count; i++)
        {
            TroopRosterElement looter = allLooters[i];
            int numberToUpgrade = Convert.ToInt32(looter.Number * ModSettings.UpgradeUnitsPercent / Balance.PercentDivisor);
            if (numberToUpgrade == 0)
                continue;

            mobileParty.MemberRoster.AddToCounts(Looters?.BasicTroop, -numberToUpgrade);
            CharacterObject recruit = cultureRecruits[MBRandom.RandomInt(0, cultureRecruits.Count)];
            mobileParty.MemberRoster.AddToCounts(recruit, numberToUpgrade);
        }
    }

    private static void ConvertLootersToBanditTier(MobileParty mobileParty, List<TroopRosterElement> allLooters)
    {
        if (!BanditTroopsByTier.TryGetValue(1, out Dictionary<FormationClass, List<CharacterObject>>? byFormation) || byFormation.Count == 0)
            return;

        for (int i = 0; i < allLooters.Count; i++)
        {
            TroopRosterElement looter = allLooters[i];
            int numberToUpgrade = Convert.ToInt32(looter.Number * ModSettings.UpgradeUnitsPercent / Balance.PercentDivisor);
            if (numberToUpgrade == 0)
                continue;

            List<CharacterObject>? candidates = null;
            if (byFormation.TryGetValue(looter.Character.DefaultFormationClass, out List<CharacterObject>? matched) && matched.Count > 0)
                candidates = matched;
            else
                foreach (List<CharacterObject> list in byFormation.Values)
                {
                    if (list.Count == 0)
                        continue;
                    candidates = list;
                    break;
                }

            if (candidates is null)
                continue;

            mobileParty.MemberRoster.AddToCounts(Looters?.BasicTroop, -numberToUpgrade);
            CharacterObject recruit = candidates[MBRandom.RandomInt(0, candidates.Count)];
            mobileParty.MemberRoster.AddToCounts(recruit, numberToUpgrade);
        }
    }

    private static CharacterObject? SelectBanditUpgradeTarget(CharacterObject character)
    {
        CharacterObject[] nativeTargets = character.UpgradeTargets;
        int banditCount = 0;
        for (int i = 0; i < nativeTargets.Length; i++)
            if (nativeTargets[i].Occupation is Occupation.Bandit)
                banditCount++;

        if (banditCount > 0)
        {
            int chosen = MBRandom.RandomInt(0, banditCount);
            int seen = 0;
            for (int i = 0; i < nativeTargets.Length; i++)
            {
                if (nativeTargets[i].Occupation is not Occupation.Bandit)
                    continue;
                if (seen == chosen)
                    return nativeTargets[i];
                seen++;
            }
        }

        if (BanditTroopsByTier.TryGetValue(character.Tier + 1, out Dictionary<FormationClass, List<CharacterObject>>? byFormation)
            && byFormation.TryGetValue(character.DefaultFormationClass, out List<CharacterObject>? candidates)
            && candidates.Count > 0)
            return candidates[MBRandom.RandomInt(0, candidates.Count)];

        return null;
    }

    private static void UpgradeReadyBanditTroops(MobileParty mobileParty)
    {
        TroopRoster memberRoster = mobileParty.MemberRoster;

        for (int i = memberRoster.Count - 1; i >= 0; i--)
        {
            TroopRosterElement element = memberRoster.GetElementCopyAtIndex(i);
            CharacterObject character = element.Character;
            if (character.IsHero || character.Tier >= ModSettings.MaxTrainingTier)
                continue;

            CharacterObject? upgradeTarget = SelectBanditUpgradeTarget(character);
            if (upgradeTarget is null)
                continue;

            int xpCost = GetBanditUpgradeXpCost(character.Tier, upgradeTarget.Tier);
            if (xpCost <= 0)
                continue;

            int possibleCount = Math.Min(element.Number - element.WoundedNumber, element.Xp / xpCost);
            if (possibleCount <= 0)
                continue;

            memberRoster.SetElementXp(i, element.Xp - possibleCount * xpCost);
            memberRoster.AddToCounts(character, -possibleCount);
            memberRoster.AddToCounts(upgradeTarget, possibleCount);
        }
    }

    private static int GetBanditUpgradeXpCost(int fromTier, int toTier)
    {
        int[] costByTier = Balance.BanditUpgradeXpCostByTier;
        int cost = 0;
        for (int tier = fromTier + 1; tier <= toTier; tier++)
        {
            if (tier <= 1)
                cost += costByTier[1];
            else if (tier < costByTier.Length)
                cost += costByTier[tier];
            else
                cost += Balance.BanditUpgradeXpCostAboveMaxTier;
        }
        return cost;
    }

    // Daily tick path: reused buffers, no LINQ.
    internal static void TrySplitBanditMilitiaParty(MobileParty mobilePartyToSplit)
    {
        if (MilitiaPowerPercent > ModSettings.GlobalPowerPercent || mobilePartyToSplit.Party.MemberRoster.TotalManCount / Balance.SplitPartsCount < ModSettings.MinPartySize || mobilePartyToSplit.IsTooBusyToGrowOrSplit())
            return;
        if (SaSCompat.IsReservedBySaS(mobilePartyToSplit))
            return;
        if (MBRandom.RandomInt(0, Balance.FullPercent) > ModSettings.RandomSplitChance || mobilePartyToSplit.Party.MemberRoster.TotalManCount < Math.Max(1, CalculatedMaxPartySize * Balance.MinSizeRatioForSplit))
            return;

        Hero? partyLeaderOne = mobilePartyToSplit.LeaderHero;
        if (partyLeaderOne is null)
            return;

        Hero? partyLeaderTwo = ReusableHeroesBehavior.Instance?.TryGetReusableBanditHero(mobilePartyToSplit.ActualClan);
        if (partyLeaderTwo is null)
            return;

        if (BmrLog.IsDebugEnabled && ReusableHeroesBehavior.IsWaitingForReuse(partyLeaderOne))
            BmrLog.Debug($"{ReattachViolationTag}: partyLeaderOne {partyLeaderOne.Name} is ALREADY in waiting list at split start, party={mobilePartyToSplit.Name}\n{Environment.StackTrace}");

        TroopRoster troopRosterOne = TroopRoster.CreateDummyTroopRoster();
        TroopRoster troopRosterTwo = TroopRoster.CreateDummyTroopRoster();

        TroopRoster prisonerRosterOne = TroopRoster.CreateDummyTroopRoster();
        TroopRoster prisonerRosterTwo = TroopRoster.CreateDummyTroopRoster();

        ItemRoster itemRosterOne = new();
        ItemRoster itemRosterTwo = new();

        List<Hero> otherHeroes = CollectOtherHeroesByPowerDescending(mobilePartyToSplit.MemberRoster, partyLeaderOne);

        SplitRosters(mobilePartyToSplit, troopRosterOne, troopRosterTwo, prisonerRosterOne, prisonerRosterTwo, itemRosterOne, itemRosterTwo);

        for (int i = otherHeroes.Count - 1; i >= 0; i--)
        {
            Hero otherHero = otherHeroes[i];

            if (BmrLog.IsDebugEnabled && ReusableHeroesBehavior.IsWaitingForReuse(otherHero))
                BmrLog.Debug($"{ReattachViolationTag}: {otherHero.Name} is in waiting list but being distributed as a MEMBER during split of {mobilePartyToSplit.Name}\n{Environment.StackTrace}");

            TroopRoster targetParty = i % Balance.SplitPartsCount == 0 ? troopRosterOne : troopRosterTwo;
            targetParty.AddToCounts(otherHeroes[i].CharacterObject, 1, true);
        }
        otherHeroes.Clear();

        CreateSplitMilitiaParties(mobilePartyToSplit, troopRosterOne, troopRosterTwo, prisonerRosterOne, prisonerRosterTwo, itemRosterOne, itemRosterTwo, partyLeaderOne, partyLeaderTwo);
    }

    // Same order as the former OrderByDescending(Power): stable, strongest first.
    private static List<Hero> CollectOtherHeroesByPowerDescending(TroopRoster memberRoster, Hero partyLeader)
    {
        List<Hero> otherHeroes = OtherHeroesBuffer;
        otherHeroes.Clear();
        for (int i = 0; i < memberRoster.Count; i++)
        {
            TroopRosterElement element = memberRoster.GetElementCopyAtIndex(i);
            if (!element.Character.IsHero || element.Character.HeroObject == partyLeader || element.Character.IsPlayerCharacter)
                continue;

            Hero hero = element.Character.HeroObject;
            int insertAt = otherHeroes.Count;
            while (insertAt > 0 && otherHeroes[insertAt - 1].Power < hero.Power)
                insertAt--;
            otherHeroes.Insert(insertAt, hero);
        }
        return otherHeroes;
    }

    private static void SplitRosters(MobileParty mobilePartyToSplit, TroopRoster troopRosterOne, TroopRoster troopRosterTwo,
        TroopRoster prisonerRosterOne, TroopRoster prisonerRosterTwo, ItemRoster itemRosterOne, ItemRoster itemRosterTwo)
    {
        TroopRoster memberRoster = mobilePartyToSplit.MemberRoster;
        for (int i = 0; i < memberRoster.Count; i++)
        {
            TroopRosterElement rosterElement = memberRoster.GetElementCopyAtIndex(i);
            if (rosterElement.Character.HeroObject is null)
                SplitRosters(troopRosterOne, troopRosterTwo, rosterElement);
        }

        memberRoster.Clear();

        TroopRoster prisonRoster = mobilePartyToSplit.PrisonRoster;
        if (prisonRoster.TotalManCount > 0)
            for (int i = 0; i < prisonRoster.Count; i++)
            {
                TroopRosterElement rosterElement = prisonRoster.GetElementCopyAtIndex(i);
                if (rosterElement.Character.HeroObject is null)
                    SplitRosters(prisonerRosterOne, prisonerRosterTwo, rosterElement);
                else if (!rosterElement.Character.IsPlayerCharacter)
                    prisonerRosterOne.AddToCounts(rosterElement.Character, rosterElement.Number, false, rosterElement.WoundedNumber, rosterElement.Xp);
            }

        prisonRoster.Clear();

        foreach (ItemRosterElement item in mobilePartyToSplit.ItemRoster)
        {
            if (string.IsNullOrEmpty(item.EquipmentElement.Item?.Name?.ToString()))
                continue;

            int half = item.Amount / Balance.SplitPartsCount;
            if (half > 0)
                itemRosterOne.AddToCounts(item.EquipmentElement, half);
            itemRosterTwo.AddToCounts(item.EquipmentElement, item.Amount - half);
        }
        mobilePartyToSplit.ItemRoster.Clear();
    }

    private static void SplitRosters(TroopRoster roster1, TroopRoster roster2, TroopRosterElement rosterElement)
    {
        int number = rosterElement.Number;
        if (number <= 0)
            return;

        int countOne = number == 1 ? MBRandom.RandomInt(0, Balance.SplitPartsCount) : number / Balance.SplitPartsCount;
        int countTwo = number - countOne;
        int woundedOne = rosterElement.WoundedNumber * countOne / number;
        int xpOne = (int)((long)rosterElement.Xp * countOne / number);

        if (countOne > 0)
            roster1.AddToCounts(rosterElement.Character, countOne, false, woundedOne, xpOne);
        if (countTwo > 0)
            roster2.AddToCounts(rosterElement.Character, countTwo, false, rosterElement.WoundedNumber - woundedOne, rosterElement.Xp - xpOne);
    }

    private static void CreateSplitMilitiaParties(MobileParty mobilePartyToSplit, TroopRoster troopRosterOne, TroopRoster troopRosterTwo, TroopRoster prisonerRosterOne, TroopRoster prisonerRosterTwo, ItemRoster itemRosterOne, ItemRoster itemRosterTwo, Hero partyLeaderOne, Hero partyLeaderTwo)
    {
        Dictionary<Hero, float> avoidanceOne = mobilePartyToSplit.GetBanditMilitiaParty()?.Avoidance ?? [];
        Dictionary<Hero, float> avoidanceTwo = new(avoidanceOne);

        MobileParty newPartyOne = CreateBanditMilitiaParty(partyLeaderOne, partyLeaderOne.BornSettlement, avoidanceOne);
        MobileParty newPartyTwo = CreateBanditMilitiaParty(partyLeaderTwo, partyLeaderTwo.BornSettlement, avoidanceTwo);

        newPartyOne.ItemRoster.Add(itemRosterOne);
        newPartyTwo.ItemRoster.Add(itemRosterTwo);
        TroopRoster[] newRosterOne = [troopRosterOne, prisonerRosterOne];
        TroopRoster[] newRosterTwo = [troopRosterTwo, prisonerRosterTwo];

        InitializeMilitiaParty(newPartyOne, partyLeaderOne, newRosterOne, mobilePartyToSplit.Position);
        InitializeMilitiaParty(newPartyTwo, partyLeaderTwo, newRosterTwo, mobilePartyToSplit.Position);

        Helper.TryTrashMobilePartySafelyReservingBanditLeader(mobilePartyToSplit);
    }

    // Hot path (AI tick): no LINQ, no allocations.
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
                if (IdleRetryTimes.TryGetValue(banditMilitiaParty, out CampaignTime idleRetryTime) && CampaignTime.Now < idleRetryTime)
                    break;
                if (!TryStartHomePatrol(banditMilitiaParty, null))
                    IdleRetryTimes[banditMilitiaParty] = CampaignTime.Now + CampaignTime.Hours(Balance.IdleRetryHours);
            }
            break;
            case AiBehavior.GoToSettlement:
            if (banditMilitiaParty.TargetSettlement?.IsHideout == true)
            {
                Vec2 partyPos2D = banditMilitiaParty.Position.ToVec2();
                Vec2 targetPos2D = banditMilitiaParty.TargetSettlement.Position.ToVec2();
                if (!banditMilitiaParty.IsEngaging &&
                    partyPos2D.DistanceSquared(targetPos2D) < Balance.ArrivedAtHideoutEpsilonSq)
                    banditMilitiaParty.SetMoveModeHold();
            }
            break;
            case AiBehavior.PatrolAroundPoint:
            CampaignTime now = CampaignTime.Now;
            if (!PatrolRepickTimes.TryGetValue(banditMilitiaParty, out CampaignTime repickTime))
                PatrolRepickTimes[banditMilitiaParty] = now + CampaignTime.Days(Balance.PatrolRepickDays);
            else if (now >= repickTime)
            {
                PatrolRepickTimes[banditMilitiaParty] = now + CampaignTime.Days(Balance.PatrolRepickDays);
                if (TryStartHomePatrol(banditMilitiaParty, banditMilitiaParty.TargetSettlement))
                    break;
            }

            BanditMilitiaPartyComponent? banditMilitiaPartyComponent = banditMilitiaParty.GetBanditMilitiaParty();
            if (banditMilitiaParty.MapFaction is null)
                return;

            if (ModSettings.AllowPillaging && banditMilitiaParty.Party.EstimatedStrength > MilitiaPartyAveragePower && MBRandom.RandomFloat < ModSettings.PillagingChance * Balance.PercentToFraction)
            {
                if (CountRaidingMilitias() >= RaidCap)
                    break;

                Settlement? raidTarget = FindRaidTarget(banditMilitiaParty, banditMilitiaPartyComponent);
                if (raidTarget is null)
                    break;

                banditMilitiaParty.SetMoveRaidSettlement(raidTarget, MobileParty.NavigationType.Default, false);
                banditMilitiaParty.Ai.SetDoNotMakeNewDecisions(true);

                if (ModSettings.ShowRaids && Hero.MainHero?.Clan is not null && raidTarget.OwnerClan == Hero.MainHero.Clan)
                    InformationManager.DisplayMessage(new InformationMessage(
                        Texts.PlayerVillageBeingRaided(banditMilitiaParty.Name, raidTarget.Name, raidTarget.Village?.Bound?.Name?.ToString() ?? string.Empty).ToString()));
            }
            break;
        }
    }

    // Home = nearest infested hideout of the militia's own bandit culture, else its HomeSettlement; both must be reachable by land.
    private static Settlement? FindHomeSettlement(MobileParty mobileParty)
    {
        CultureObject? clanCulture = mobileParty.ActualClan?.Culture;
        List<Settlement>? hideouts = Hideouts;
        Vec2 position = mobileParty.Position.ToVec2();
        Settlement? nearestHideout = null;
        float nearestDistanceSq = float.MaxValue;
        if (clanCulture is not null && hideouts is not null)
        {
            for (int i = 0; i < hideouts.Count; i++)
            {
                Settlement hideout = hideouts[i];
                if (hideout?.Hideout is null || hideout.Culture != clanCulture)
                    continue;
                float distanceSq = hideout.GatePosition.ToVec2().DistanceSquared(position);
                if (distanceSq >= nearestDistanceSq || !hideout.Hideout.IsInfested)
                    continue;
                nearestDistanceSq = distanceSq;
                nearestHideout = hideout;
            }
        }

        if (nearestHideout is not null && IsReachableByLand(mobileParty, nearestHideout))
            return nearestHideout;

        Settlement? homeSettlement = mobileParty.HomeSettlement;
        return homeSettlement is not null && IsReachableByLand(mobileParty, homeSettlement) ? homeSettlement : null;
    }

    private static bool IsReachableByLand(MobileParty mobileParty, Settlement settlement)
        => Campaign.Current?.Models?.MapDistanceModel?.PathExistBetweenPoints(mobileParty.Position, settlement.GatePosition, MobileParty.NavigationType.Default) ?? true;

    private static bool TryStartHomePatrol(MobileParty mobileParty, Settlement? excludedSettlement)
    {
        Vec2 homePoint = FindHomeSettlement(mobileParty)?.GatePosition.ToVec2() ?? mobileParty.Position.ToVec2();
        Settlement? patrolTarget = PickReachablePatrolSettlement(mobileParty, homePoint, Balance.SettlementFindRange, excludedSettlement)
                                   ?? PickReachablePatrolSettlement(mobileParty, homePoint, Balance.SettlementFindRangeWide, excludedSettlement);
        if (patrolTarget is null)
            return false;

        mobileParty.SetMovePatrolAroundSettlement(patrolTarget, MobileParty.NavigationType.Default, false);
        PatrolRepickTimes[mobileParty] = CampaignTime.Now + CampaignTime.Days(Balance.PatrolRepickDays);
        IdleRetryTimes.Remove(mobileParty);
        return true;
    }

    private static Settlement? PickReachablePatrolSettlement(MobileParty mobileParty, Vec2 center, float range, Settlement? excludedSettlement)
    {
        for (int attempt = 0; attempt < Balance.PatrolPickAttempts; attempt++)
        {
            Settlement? candidate = PickRandomPatrolSettlement(center, range, excludedSettlement);
            if (candidate is null)
                return null;
            if (IsReachableByLand(mobileParty, candidate))
                return candidate;
        }
        return null;
    }

    private static Settlement? PickRandomPatrolSettlement(Vec2 center, float range, Settlement? excludedSettlement)
    {
        LocatableSearchData<Settlement> settlementsInRange = Settlement.StartFindingLocatablesAroundPosition(center, range);
        Settlement? chosen = null;
        int seen = 0;
        for (Settlement settlement = Settlement.FindNextLocatable(ref settlementsInRange);
             settlement != null;
             settlement = Settlement.FindNextLocatable(ref settlementsInRange))
        {
            if (settlement.IsHideout || settlement.IsTown || settlement.IsCastle || settlement == excludedSettlement)
                continue;
            seen++;
            if (MBRandom.RandomInt(seen) == 0)
                chosen = settlement;
        }
        return chosen;
    }

    private static int CountRaidingMilitias()
    {
        int raidingCount = 0;
        IReadOnlyList<BanditMilitiaPartyComponent?> cachedBanditMilitias = PowerCalculationManager.GetActiveBanditMilitiaParties();
        for (int i = 0, n = cachedBanditMilitias.Count; i < n; i++)
        {
            if (cachedBanditMilitias[i]?.MobileParty?.DefaultBehavior == AiBehavior.RaidSettlement)
                raidingCount++;
        }
        return raidingCount;
    }

    private static Settlement? FindRaidTarget(MobileParty banditMilitiaParty, BanditMilitiaPartyComponent? banditMilitiaPartyComponent)
    {
        List<Settlement> rejected = RaidTargetRejectBuffer;
        rejected.Clear();
        for (int attempt = 0; attempt < Balance.RaidTargetAttempts; attempt++)
        {
            Settlement? candidate = FindBestRaidVillage(banditMilitiaParty, rejected);
            if (candidate is null)
                return null;
            if (!IsVillageAvoided(banditMilitiaPartyComponent, candidate) && IsReachableByLand(banditMilitiaParty, candidate))
                return candidate;
            rejected.Add(candidate);
        }
        return null;
    }

    // Richest village (Hearth) weighed against distance, within RaidSearchRangeSq.
    private static Settlement? FindBestRaidVillage(MobileParty banditMilitiaParty, List<Settlement> rejected)
    {
        List<Settlement>? villages = Villages;
        if (villages is null)
            return null;

        Vec2 militiaPosition = banditMilitiaParty.Position.ToVec2();
        IFaction militiaFaction = banditMilitiaParty.MapFaction;
        Settlement? best = null;
        float bestScore = float.MinValue;
        for (int i = 0, n = villages.Count; i < n; i++)
        {
            Settlement villageSettlement = villages[i];
            Village? village = villageSettlement?.Village;
            if (village is null || village.VillageState is Village.VillageStates.BeingRaided or Village.VillageStates.Looted)
                continue;

            if (villageSettlement!.Owner is null)
                continue;

            if (villageSettlement.MapFaction is null || !villageSettlement.MapFaction.IsAtWarWith(militiaFaction))
                continue;

            if (villageSettlement.GetValue() <= 0)
                continue;

            float distanceSq = villageSettlement.GatePosition.ToVec2().DistanceSquared(militiaPosition);
            if (distanceSq > Balance.RaidSearchRangeSq || rejected.Contains(villageSettlement))
                continue;

            float score = village.Hearth / ((float)Math.Sqrt(distanceSq) + Balance.RaidDistanceBias);
            if (score <= bestScore)
                continue;

            bestScore = score;
            best = villageSettlement;
        }
        return best;
    }

    // One roll against the highest avoidance this militia holds toward any hero of the village owner clan.
    private static bool IsVillageAvoided(BanditMilitiaPartyComponent? banditMilitiaPartyComponent, Settlement village)
    {
        Dictionary<Hero, float>? avoidance = banditMilitiaPartyComponent?.Avoidance;
        Clan? ownerClan = village.OwnerClan;
        if (avoidance is null || avoidance.Count == 0 || ownerClan is null)
            return false;

        float maxAvoidance = 0f;
        MBReadOnlyList<Hero> clanHeroes = ownerClan.Heroes;
        for (int i = 0; i < clanHeroes.Count; i++)
        {
            Hero clanHero = clanHeroes[i];
            if (clanHero is not null && avoidance.TryGetValue(clanHero, out float value) && value > maxAvoidance)
                maxAvoidance = value;
        }
        return maxAvoidance > 0f && MBRandom.RandomFloat * Balance.PercentDivisor <= maxAvoidance;
    }

    // Called when a village gets looted: militias still marching to raid it are released.
    internal static void ReleaseMilitiasRaidingLootedVillage(Settlement villageSettlement, MobileParty? raiderParty)
    {
        IReadOnlyList<BanditMilitiaPartyComponent?> militias = PowerCalculationManager.GetActiveBanditMilitiaParties();
        for (int i = 0, n = militias.Count; i < n; i++)
        {
            MobileParty? militia = militias[i]?.MobileParty;
            if (militia is null || militia == raiderParty || militia.MapEvent is not null)
                continue;
            if (militia.DefaultBehavior != AiBehavior.RaidSettlement || militia.TargetSettlement != villageSettlement)
                continue;
            if (SaSCompat.IsReservedBySaS(militia))
                continue;

            militia.Ai?.SetDoNotMakeNewDecisions(false);
            militia.SetMoveModeHold();
        }
    }

    // Daily tick path: reused buffer, no LINQ.
    internal static void TryGrowMilitiaParty(MobileParty mobileParty)
    {
        if (!CanGrowMilitiaParty(mobileParty) || mobileParty.IsTooBusyToGrowOrSplit())
            return;

        int maxTrainingTier = ModSettings.MaxTrainingTier;
        List<TroopRosterElement> eligibleTroopsToGrow = EligibleTroopsBuffer;
        eligibleTroopsToGrow.Clear();
        TroopRoster memberRoster = mobileParty.MemberRoster;
        for (int i = 0; i < memberRoster.Count; i++)
        {
            TroopRosterElement rosterElement = memberRoster.GetElementCopyAtIndex(i);
            if (rosterElement.Character is not null
                && !rosterElement.Character.IsHero
                && rosterElement.Character.Tier < maxTrainingTier
                && !IsGloriousName(rosterElement.Character.Name))
                eligibleTroopsToGrow.Add(rosterElement);
        }

        if (eligibleTroopsToGrow.Count == 0)
            return;

        float growthTroopsAmountAllowed = mobileParty.MemberRoster.TotalManCount * (ModSettings.GrowthPercent / Balance.PercentDivisor);

        float troopsGrowthSmallBoost = GlobalMilitiaPower > 0 ? CalculatedGlobalPowerLimit / GlobalMilitiaPower : 1;
        growthTroopsAmountAllowed += ModSettings.GlobalPowerPercent / Balance.PercentDivisor * troopsGrowthSmallBoost;
        growthTroopsAmountAllowed = Mathf.Clamp(growthTroopsAmountAllowed, 1, Balance.GrowthTroopsPerDayMax);

        for (int i = 0; i < growthTroopsAmountAllowed && mobileParty.MemberRoster.TotalManCount + 1 < CalculatedMaxPartySize; i++)
        {
            TroopRosterElement rosterElement = eligibleTroopsToGrow.GetRandomElement();
            if (rosterElement.Character is null)
                continue;

            CharacterObject troop = rosterElement.Character;
            if (GlobalMilitiaPower + troop.GetPower() < CalculatedGlobalPowerLimit)
                mobileParty.MemberRoster.AddToCounts(troop, 1);
        }
        eligibleTroopsToGrow.Clear();

        EquipmentPool.AdjustCavalryCount(mobileParty.MemberRoster);
    }

    private static bool CanGrowMilitiaParty(MobileParty mobileParty) =>
        ModSettings.GrowthPercent > 0
        && MilitiaPowerPercent <= ModSettings.GlobalPowerPercent
        && IsAvailableBanditParty(mobileParty)
        && MBRandom.RandomFloat <= ModSettings.GrowthChance / Balance.PercentDivisor;

    internal static bool TryHandleStuckMilitia(MobileParty mobileParty, Vec2 currentMilitiaPartyPosition)
    {
        if (!StuckTracker.TryGetValue(mobileParty, out (Vec2 LastPos, int HourCount, int Rescues) stuckState)
            || currentMilitiaPartyPosition.DistanceSquared(stuckState.LastPos) >= Balance.StuckDistanceThresholdSq)
        {
            StuckTracker[mobileParty] = (currentMilitiaPartyPosition, 0, 0);
            return false;
        }

        int newCount = stuckState.HourCount + 1;
        if (newCount < Balance.StuckHourLimit)
        {
            StuckTracker[mobileParty] = (stuckState.LastPos, newCount, stuckState.Rescues);
            return false;
        }

        int rescues = stuckState.Rescues + 1;
        StuckTracker[mobileParty] = (currentMilitiaPartyPosition, 0, rescues);
        if (BmrLog.IsDebugEnabled)
            BmrLog.Debug($"{StuckTag}: {mobileParty.StringId} rescue {rescues} default={mobileParty.DefaultBehavior} shortTerm={mobileParty.ShortTermBehavior} target={mobileParty.TargetSettlement?.StringId ?? "none"} pos={currentMilitiaPartyPosition}");

        mobileParty.Ai?.SetDoNotMakeNewDecisions(false);
        if (rescues <= Balance.StuckRescueLimit)
            return TryStartHomePatrol(mobileParty, mobileParty.TargetSettlement);

        mobileParty.SetMoveModeHold();
        IdleRetryTimes[mobileParty] = CampaignTime.Now + CampaignTime.Hours(Balance.IdleRetryHours);
        return true;
    }

    // Partial hourly AI path: reused buffers.
    internal static void TryFindAndMergeNearbyBandits(MobileParty banditMilitiaParty, Vec2 currentMilitiaPartyPosition, CampaignTime timeNow, CampaignTime cooldownToMergeOrSplit)
    {
        if (!banditMilitiaParty.IsBanditMilitiaParty() && !CanProvideLeaderForMerge(banditMilitiaParty.ActualClan))
            return;

        List<MobileParty> nearbyBandits = NearbyBanditsBuffer;
        nearbyBandits.Clear();

        LocatableSearchData<MobileParty> locatableSearchData = MobileParty.StartFindingLocatablesAroundPosition(currentMilitiaPartyPosition, Balance.FindNearbyBanditsRadius);
        for (MobileParty nearbyLocatable = MobileParty.FindNextLocatable(ref locatableSearchData); nearbyLocatable != null; nearbyLocatable = MobileParty.FindNextLocatable(ref locatableSearchData))
        {
            if (nearbyLocatable == banditMilitiaParty)
                continue;

            if (nearbyLocatable.HasNavalNavigationCapability)
                continue;

            if (IsAvailableBanditParty(nearbyLocatable) && !nearbyLocatable.IsTooBusyToMerge() && !SaSCompat.IsReservedBySaS(nearbyLocatable))
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

        Dictionary<MobileParty, int> mountedCountByParty = MountedCountBuffer;
        mountedCountByParty.Clear();

        MobileParty? mergeTarget = null;

        for (int i = 0; i < nearbyBandits.Count; i++)
        {
            MobileParty target = nearbyBandits[i];
            int militiaTotalCount = banditMilitiaParty.MemberRoster.TotalManCount + target.MemberRoster.TotalManCount;

            if (militiaTotalCount < ModSettings.MinPartySize || militiaTotalCount > CalculatedMaxPartySize)
                continue;

            if (banditMilitiaParty.MapFaction is not null && target.MapFaction?.IsAtWarWith(banditMilitiaParty.MapFaction) == true)
                continue;

            if (target.IsBanditMilitiaParty())
            {
                BanditMilitiaPartyComponent targetBanditMilitiaParty = target.GetBanditMilitiaParty()!;
                if (timeNow < targetBanditMilitiaParty.LastMergedOrSplitDate + cooldownToMergeOrSplit)
                    continue;
            }

            if (!mountedCountByParty.TryGetValue(target, out int targetMountedCount))
            {
                targetMountedCount = EquipmentPool.NumMountedTroops(target.MemberRoster);
                mountedCountByParty[target] = targetMountedCount;
            }

            if (mobilePartyMountedCount + targetMountedCount > militiaTotalCount / Balance.MaxMountedShareDivisor)
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
        if (distanceToTargetSq <= Balance.MergeDistanceSq)
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

    internal static bool CanMobilePartiesMerge(MobileParty attackerMobileParty, MobileParty defenderMobileParty) =>
        MobileParty.MainParty != attackerMobileParty && MobileParty.MainParty != defenderMobileParty &&
        attackerMobileParty.IsBandit && defenderMobileParty.IsBandit &&
        attackerMobileParty.IsActive && defenderMobileParty.IsActive &&
        attackerMobileParty.MapEvent is null && defenderMobileParty.MapEvent is null &&
        !FactionManager.IsAtWarAgainstFaction(attackerMobileParty.MapFaction, defenderMobileParty.MapFaction) &&
        !attackerMobileParty.IsUsedByAQuest() && !defenderMobileParty.IsUsedByAQuest() &&
        !attackerMobileParty.HasNavalNavigationCapability && !defenderMobileParty.HasNavalNavigationCapability &&
        !SaSCompat.IsReservedBySaS(attackerMobileParty) && !SaSCompat.IsReservedBySaS(defenderMobileParty);

    internal static bool IfEngagedByThePlayer(MobileParty attackerParty, MobileParty defenderParty) =>
        MobileParty.MainParty.ShortTermBehavior == AiBehavior.EngageParty
        && (MobileParty.MainParty.ShortTermTargetParty == attackerParty
            || MobileParty.MainParty.ShortTermTargetParty == defenderParty);

    internal static bool IsAvailableBanditParty(MobileParty mobileParty) =>
        mobileParty is { IsBandit: true, CurrentSettlement: null, MapEvent: null }
        && mobileParty.Party.MemberRoster.TotalManCount > ModSettings.MergeableSize
        && !mobileParty.IsUsedByAQuest()
        && !VerbotenParties.Contains(mobileParty.StringId);

    private static bool IsGloriousName(TextObject name)
    {
        string value = name.Value;
        return value != null && value.StartsWith(Balance.GloriousTroopNamePrefix, StringComparison.Ordinal);
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
