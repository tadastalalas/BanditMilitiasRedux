using System;

namespace BanditMilitiasRedux.Data;

// Shape of ModConfiguration.xml and the only place where defaults and ranges live (MCMSettings uses them too).
// One section per MCM group, same names and order.
public class ModConfiguration
{
    // Spawning & Formation
    public const int HeroPoolCapDefault = 4;
    public const int HeroPoolCapMin = 1;
    public const int HeroPoolCapMax = 20;
    public const int MergeableSizeDefault = 15;
    public const int MergeableSizeMin = 1;
    public const int MergeableSizeMax = 100;
    public const int RandomSplitChanceDefault = 5;

    // Training & Growth
    public const bool CanTrainDefault = true;
    public const bool UseBanditOnlyTroopTreesDefault = false;
    public const int DailyTrainingChanceDefault = 10;
    public const XpGiftLevel XpGiftDefault = XpGiftLevel.Normal;
    public const int UpgradeUnitsPercentDefault = 25;
    public const int MaxTrainingTierDefault = 4;
    public const int MaxTrainingTierMin = 1;
    public const int MaxTrainingTierMax = 6;
    public const int GrowthChanceDefault = 50;
    public const int GrowthPercentDefault = 1;

    // Power & Balance
    public const int GlobalPowerPercentDefault = 20;
    public const int GlobalPowerPercentMin = 1;
    public const GoldRewardLevel GoldRewardDefault = GoldRewardLevel.Normal;

    // Behavior & AI
    public const int MaxStrongerTargetPercentDefault = 0;
    public const int MaxWeakerTargetPercentDefault = 50;
    public const bool IgnoreVillagersCaravansDefault = false;
    public const int ExecutionHonorXpDefault = 0;
    public const int ExecutionHonorXpMin = 0;
    public const int ExecutionHonorXpMax = 1000;
    public const bool AllowPillagingDefault = true;
    public const float PillagingChanceDefault = 1f;
    public const int RaidCapPercentDefault = 100;
    public const int RaidCapPercentMin = 10;
    public const int RaidCapPercentMax = 500;

    // Appearance & Customization
    public const string BanditMilitiaStringDefault = "Bandit Militia";
    public const string LeaderlessBanditMilitiaStringDefault = "Leaderless Bandit Militia";
    public const bool RandomBannersDefault = true;
    public const int FemaleSpawnChanceDefault = 25;
    public const bool CheckVoiceGenderDefault = true;

    // UI & Notifications
    public const bool ShowRaidsDefault = true;
    public const bool SkipConversationsDefault = false;
    public const bool RemovePrisonerMessagesDefault = true;
    public const bool ShowMilitiaMarkersDefault = true;
    public const int TrackedSizeMinimumDefault = 50;
    public const int TrackedSizeMinimumMin = 0;
    public const int TrackedSizeMinimumMax = 500;

    // Advanced
    public const int CooldownHoursDefault = 24;
    public const int CooldownHoursMin = 0;
    public const int CooldownHoursMax = 168;
    public const int MaxItemValueDefault = 3750;
    public const int MaxItemValueMin = 1000;
    public const int MaxItemValueMax = 100000;
    public const bool IgnoreSizePenaltyDefault = true;

    // Technical settings
    public const bool LoggingEnabledDefault = false;

    // Shared range for every "%" setting.
    public const int PercentMin = 0;
    public const int PercentMax = 100;

    // Order = MCM dropdown index (MCM saves the index): append only.
    public enum XpGiftLevel { Off, Normal, Hard, Hardest }

    // Order = MCM dropdown index (MCM saves the index): append only.
    public enum GoldRewardLevel { Low, Normal, Rich, Richest }

    public SpawningAndFormationSection SpawningAndFormation { get; set; } = new();
    public TrainingAndGrowthSection TrainingAndGrowth { get; set; } = new();
    public PowerAndBalanceSection PowerAndBalance { get; set; } = new();
    public BehaviorAndAiSection BehaviorAndAi { get; set; } = new();
    public AppearanceAndCustomizationSection AppearanceAndCustomization { get; set; } = new();
    public UiAndNotificationsSection UiAndNotifications { get; set; } = new();
    public AdvancedSection Advanced { get; set; } = new();

    // Always the last section.
    public TechnicalSettingsSection TechnicalSettings { get; set; } = new();

    internal void Clamp()
    {
        SpawningAndFormation ??= new SpawningAndFormationSection();
        TrainingAndGrowth ??= new TrainingAndGrowthSection();
        PowerAndBalance ??= new PowerAndBalanceSection();
        BehaviorAndAi ??= new BehaviorAndAiSection();
        AppearanceAndCustomization ??= new AppearanceAndCustomizationSection();
        UiAndNotifications ??= new UiAndNotificationsSection();
        Advanced ??= new AdvancedSection();
        TechnicalSettings ??= new TechnicalSettingsSection();

        SpawningAndFormation.HeroPoolCap = Clamp(SpawningAndFormation.HeroPoolCap, HeroPoolCapMin, HeroPoolCapMax);
        SpawningAndFormation.MergeableSize = Clamp(SpawningAndFormation.MergeableSize, MergeableSizeMin, MergeableSizeMax);
        SpawningAndFormation.RandomSplitChance = Clamp(SpawningAndFormation.RandomSplitChance, PercentMin, PercentMax);

        TrainingAndGrowth.DailyTrainingChance = Clamp(TrainingAndGrowth.DailyTrainingChance, PercentMin, PercentMax);
        TrainingAndGrowth.UpgradeUnitsPercent = Clamp(TrainingAndGrowth.UpgradeUnitsPercent, PercentMin, PercentMax);
        TrainingAndGrowth.MaxTrainingTier = Clamp(TrainingAndGrowth.MaxTrainingTier, MaxTrainingTierMin, MaxTrainingTierMax);
        TrainingAndGrowth.GrowthChance = Clamp(TrainingAndGrowth.GrowthChance, PercentMin, PercentMax);
        TrainingAndGrowth.GrowthPercent = Clamp(TrainingAndGrowth.GrowthPercent, PercentMin, PercentMax);

        PowerAndBalance.GlobalPowerPercent = Clamp(PowerAndBalance.GlobalPowerPercent, GlobalPowerPercentMin, PercentMax);

        BehaviorAndAi.MaxStrongerTargetPercent = Clamp(BehaviorAndAi.MaxStrongerTargetPercent, PercentMin, PercentMax);
        BehaviorAndAi.MaxWeakerTargetPercent = Clamp(BehaviorAndAi.MaxWeakerTargetPercent, PercentMin, PercentMax);
        BehaviorAndAi.ExecutionHonorXp = Clamp(BehaviorAndAi.ExecutionHonorXp, ExecutionHonorXpMin, ExecutionHonorXpMax);
        float pillagingChance = BehaviorAndAi.PillagingChance;
        BehaviorAndAi.PillagingChance = float.IsNaN(pillagingChance) ? PillagingChanceDefault : Math.Min(Math.Max(pillagingChance, PercentMin), PercentMax);
        BehaviorAndAi.RaidCapPercent = Clamp(BehaviorAndAi.RaidCapPercent, RaidCapPercentMin, RaidCapPercentMax);

        AppearanceAndCustomization.BanditMilitiaString ??= BanditMilitiaStringDefault;
        AppearanceAndCustomization.LeaderlessBanditMilitiaString ??= LeaderlessBanditMilitiaStringDefault;
        AppearanceAndCustomization.FemaleSpawnChance = Clamp(AppearanceAndCustomization.FemaleSpawnChance, PercentMin, PercentMax);

        UiAndNotifications.TrackedSizeMinimum = Clamp(UiAndNotifications.TrackedSizeMinimum, TrackedSizeMinimumMin, TrackedSizeMinimumMax);

        Advanced.CooldownHours = Clamp(Advanced.CooldownHours, CooldownHoursMin, CooldownHoursMax);
        Advanced.MaxItemValue = Clamp(Advanced.MaxItemValue, MaxItemValueMin, MaxItemValueMax);
    }

    private static int Clamp(int value, int min, int max) => Math.Min(Math.Max(value, min), max);

    public class SpawningAndFormationSection
    {
        public int HeroPoolCap { get; set; } = HeroPoolCapDefault;
        public int MergeableSize { get; set; } = MergeableSizeDefault;
        public int RandomSplitChance { get; set; } = RandomSplitChanceDefault;
    }

    public class TrainingAndGrowthSection
    {
        public bool CanTrain { get; set; } = CanTrainDefault;
        public bool UseBanditOnlyTroopTrees { get; set; } = UseBanditOnlyTroopTreesDefault;
        public int DailyTrainingChance { get; set; } = DailyTrainingChanceDefault;
        public XpGiftLevel XpGift { get; set; } = XpGiftDefault;
        public int UpgradeUnitsPercent { get; set; } = UpgradeUnitsPercentDefault;
        public int MaxTrainingTier { get; set; } = MaxTrainingTierDefault;
        public int GrowthChance { get; set; } = GrowthChanceDefault;
        public int GrowthPercent { get; set; } = GrowthPercentDefault;
    }

    public class PowerAndBalanceSection
    {
        public int GlobalPowerPercent { get; set; } = GlobalPowerPercentDefault;
        public GoldRewardLevel GoldReward { get; set; } = GoldRewardDefault;
    }

    public class BehaviorAndAiSection
    {
        public int MaxStrongerTargetPercent { get; set; } = MaxStrongerTargetPercentDefault;
        public int MaxWeakerTargetPercent { get; set; } = MaxWeakerTargetPercentDefault;
        public bool IgnoreVillagersCaravans { get; set; } = IgnoreVillagersCaravansDefault;
        public int ExecutionHonorXp { get; set; } = ExecutionHonorXpDefault;
        public bool AllowPillaging { get; set; } = AllowPillagingDefault;
        public float PillagingChance { get; set; } = PillagingChanceDefault;
        public int RaidCapPercent { get; set; } = RaidCapPercentDefault;
    }

    public class AppearanceAndCustomizationSection
    {
        public string BanditMilitiaString { get; set; } = BanditMilitiaStringDefault;
        public string LeaderlessBanditMilitiaString { get; set; } = LeaderlessBanditMilitiaStringDefault;
        public bool RandomBanners { get; set; } = RandomBannersDefault;
        public int FemaleSpawnChance { get; set; } = FemaleSpawnChanceDefault;
        public bool CheckVoiceGender { get; set; } = CheckVoiceGenderDefault;
    }

    public class UiAndNotificationsSection
    {
        public bool ShowRaids { get; set; } = ShowRaidsDefault;
        public bool SkipConversations { get; set; } = SkipConversationsDefault;
        public bool RemovePrisonerMessages { get; set; } = RemovePrisonerMessagesDefault;
        public bool ShowMilitiaMarkers { get; set; } = ShowMilitiaMarkersDefault;
        public int TrackedSizeMinimum { get; set; } = TrackedSizeMinimumDefault;
    }

    public class AdvancedSection
    {
        public int CooldownHours { get; set; } = CooldownHoursDefault;
        public int MaxItemValue { get; set; } = MaxItemValueDefault;
        public bool IgnoreSizePenalty { get; set; } = IgnoreSizePenaltyDefault;
    }

    public class TechnicalSettingsSection
    {
        // Always the last entry.
        public bool LoggingEnabled { get; set; } = LoggingEnabledDefault;
    }
}
