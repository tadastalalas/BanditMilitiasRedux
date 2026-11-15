namespace BanditMilitiasRedux.Core;

// Gameplay numbers and fixed ids that are not player settings. Change here, not inline.
internal static class Balance
{
    // Percent math: "x %" settings are stored as 0..100.
    internal const int FullPercent = 100;
    internal const float PercentDivisor = 100f;
    internal const float PercentToFraction = 0.01f;

    // Merging and splitting
    internal const float FindNearbyBanditsRadius = 20;
    internal const float MergeDistanceSq = 2.25f;
    internal const float MinSizeRatioForSplit = 0.8f;
    internal const int MinPartySizeMergeMultiplier = 2; // Minimum militia size = Minimum Size to Merge * this.
    internal const int SplitPartsCount = 2; // A split militia becomes this many parties.
    internal const int MaxMountedShareDivisor = 2; // At most 1/this of a militia may be mounted.
    internal const int NearbyBuffersCapacity = 8;

    // Avoidance
    internal const float AvoidanceEffectToOtherBanditHeroesRadiusSq = 10000f;
    internal const int AvoidanceIncreaseMin = 15;
    internal const int AvoidanceIncreaseMax = 35;
    internal const int AvoidanceDecreaseAmount = 10;
    internal const float AvoidanceMergeAverageFactor = 0.5f; // Merged militias keep the average avoidance.

    // Map AI: patrol, stuck, raid
    internal const float StuckDistanceThresholdSq = 225f;
    internal const int StuckHourLimit = 12;
    internal const float ArrivedAtHideoutEpsilonSq = 0.1f;
    internal const int SettlementFindRange = 200;
    internal const int SettlementFindRangeWide = 400; // Fallback patrol search when no village is within SettlementFindRange of home.
    internal const int PatrolPickAttempts = 3; // Random patrol villages tried before giving up (unreachable ones are skipped).
    internal const float PatrolRepickDays = 3f; // A patrolling militia picks a new village near home after this many days.
    internal const float IdleRetryHours = 6f; // A militia that found nothing to patrol waits this long before searching again.
    internal const int StuckRescueLimit = 2; // Rescues in a row without moving before the militia just holds.
    internal const float RaidSearchRangeSq = 40000f; // Raid targets must be within 200 map units.
    internal const float RaidDistanceBias = 50f; // Raid score = Hearth / (distance + bias); higher bias favours rich villages over near ones.
    internal const int RaidTargetAttempts = 3; // Raid candidates tried when the best one is avoided or unreachable.
    internal const float RaidCapVillagesPerRaid = 10f; // Baseline raid cap = villages / this.
    internal const int CultureSearchRadius = 100; // Looters convert to the most common culture of settlements within this range.

    // Growth and training
    internal const float GrowthTroopsPerDayMax = 50; // Troops a militia may gain in one day.
    internal const float UpgradeHalfDivisor = 2f; // Training upgrades at most about half of a troop stack.
    internal static readonly int[] DifficultyXpValues = [0, 300, 600, 900]; // Per "Militia XP Boost" index.
    internal static readonly int[] TrainingIterationsByXpGift = [0, 1, 2, 4]; // Per "Militia XP Boost" index.
    internal const int TrainingIterationsFallback = 1;
    internal static readonly int[] BanditUpgradeXpCostByTier = [100, 100, 300, 550, 900, 1300, 1700]; // Index = target tier.
    internal const int BanditUpgradeXpCostAboveMaxTier = 2100;
    internal const int RecruitLevel = 11; // Culture recruits used for looter conversion.

    // Power
    internal const float PowerVarianceMin = 0.925f;
    internal const float PowerVarianceMax = 1.075f;
    internal const float PowerRecalcIntervalHours = 20; // DoPowerCalculations runs at most this often.
    internal const int MedianDivisor = 2;
    internal const float MaxPartySizeFloorMultiplier = 2f; // Max militia size never drops below minimum militia size * this.

    // Gold and bounty
    internal static readonly int[] GoldValues = [250, 500, 900, 2000]; // Per "Bandit Hero Gold Reward" index.
    internal const int LeaderMountRollRange = 2; // A new militia leader gets a mount with 1 in this chance.
    internal const int BountyRenownMin = 1;
    internal const int BountyRenownMax = 5;
    internal const int BountyGoldMin = 2000;
    internal const int BountyGoldMax = 10000;
    internal const float BountyStrengthForMaxReward = 300f;

    // Heroes
    internal const int TemplateFrequencyWeightMultiplier = 10;
    internal const int TemplateDefaultWeight = 100;
    internal const int NameGenerationMaxAttempts = 200;
    internal const float LeaderPowerVariationMax = 20f; // Slight power variation added on creation (0 to this).
    internal const int LeaderStartingLeadershipXp = 150;
    internal const int StartingGrainMin = 15;
    internal const int StartingGrainMax = 30;
    internal const int BannerCacheSize = 5000;
    internal const int VanillaExecutionHonorPenalty = -1000; // TraitLevelingHelper's Honor XP for executing a lord.

    // Notoriety: tier N is reached at index N-1.
    internal static readonly int[] NotorietyWinThresholds = [3, 6, 9, 12, 15, 18, 21, 24, 27, 30];
    internal static readonly int[] NotorietyDayThresholds = [21, 42, 63, 84, 105, 126, 147, 168, 189, 210];
    internal const float NotorietyFearMentionThreshold = 25f;

    // Equipment pool
    internal const int EquipmentSetCount = 10000;
    internal const int SlotRetryLimit = 20;
    internal const int ArmorMinValue = 1000;
    internal const int CavalryAdjustMaxIterations = 200;

    // Fixed vanilla / mod ids
    internal const string BanditMilitiaPartyStringId = "Bandit_Militia";
    internal const string LootersClanId = "looters";
    internal const string BlackFlagCultureId = "ad_bandit_blackflag";
    internal const string FallbackCultureId = "empire";
    internal const string SeasideHideoutPrefix = "hideout_seaside";
    internal const string CamelMonsterUsage = "camel";
    internal const string GloriousTroopNamePrefix = "Glorious";
    internal const string DesertersPartyPrefix = "deserters";
}
