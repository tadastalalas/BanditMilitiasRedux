using BanditMilitiasRedux.Core;
using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;

namespace BanditMilitiasRedux.Data;

// Used only when MCM is loaded. The only file that may use MCM types.
// Property names and types are released (MCM saves values by them): never rename or retype.
internal class MCMSettings : AttributeGlobalSettings<MCMSettings>
{
    // Released Id: changing it resets every player's saved settings.
    private const string SettingsId = ModInfo.ModuleId;
    private const string SaveFormat = "json2";

    internal delegate void OnSettingsChangedDelegate();

    internal static event OnSettingsChangedDelegate? OnSettingsChanged;

    public override string Id => SettingsId;

    public override string DisplayName => Texts.ModName.ToString();

    public override string FolderName => ModInfo.ModuleId;

    public override string FormatType => SaveFormat;

    internal int XpGiftIndex => XpGift?.SelectedIndex ?? (int)ModConfiguration.XpGiftDefault;

    internal int GoldRewardIndex => GoldReward?.SelectedIndex ?? (int)ModConfiguration.GoldRewardDefault;

    public override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        VerifyProperties();

        OnSettingsChanged?.Invoke();
    }

    // Rebuilds the dropdowns with item names in the game language, keeping the saved index.
    internal void LocalizeDropdowns()
    {
        XpGift = new Dropdown<string>([Texts.XpOff.ToString(), Texts.XpNormal.ToString(), Texts.XpHard.ToString(), Texts.XpHardest.ToString()], XpGiftIndex);
        GoldReward = new Dropdown<string>([Texts.GoldLow.ToString(), Texts.GoldNormal.ToString(), Texts.GoldRich.ToString(), Texts.GoldRichest.ToString()], GoldRewardIndex);
    }

    private void VerifyProperties()
    {
        if (string.IsNullOrWhiteSpace(BanditMilitiaString))
            BanditMilitiaString = Texts.BanditMilitiaDefaultName.ToString();

        if (string.IsNullOrWhiteSpace(LeaderlessBanditMilitiaString))
            LeaderlessBanditMilitiaString = Texts.LeaderlessBanditMilitiaDefaultName.ToString();
    }

    #region Spawning & Formation
    [SettingPropertyInteger(Texts.Ids.HeroPoolCap, ModConfiguration.HeroPoolCapMin, ModConfiguration.HeroPoolCapMax, Order = 0, RequireRestart = false, HintText = Texts.Ids.HeroPoolCapHint)]
    [SettingPropertyGroup(Texts.Ids.GroupSpawning, GroupOrder = 0)]
    public int HeroPoolCap { get; private set; } = ModConfiguration.HeroPoolCapDefault;

    [SettingPropertyInteger(Texts.Ids.MergeableSize, ModConfiguration.MergeableSizeMin, ModConfiguration.MergeableSizeMax, Order = 1, RequireRestart = false, HintText = Texts.Ids.MergeableSizeHint)]
    [SettingPropertyGroup(Texts.Ids.GroupSpawning, GroupOrder = 0)]
    public int MergeableSize { get; private set; } = ModConfiguration.MergeableSizeDefault;

    [SettingPropertyInteger(Texts.Ids.RandomSplitChance, ModConfiguration.PercentMin, ModConfiguration.PercentMax, Order = 2, RequireRestart = false, HintText = Texts.Ids.RandomSplitChanceHint)]
    [SettingPropertyGroup(Texts.Ids.GroupSpawning, GroupOrder = 0)]
    public int RandomSplitChance { get; private set; } = ModConfiguration.RandomSplitChanceDefault;
    #endregion

    #region Training & Growth
    [SettingPropertyBool(Texts.Ids.CanTrain, Order = 0, RequireRestart = false, HintText = Texts.Ids.CanTrainHint)]
    [SettingPropertyGroup(Texts.Ids.GroupTraining, GroupOrder = 1)]
    public bool CanTrain { get; private set; } = ModConfiguration.CanTrainDefault;

    [SettingPropertyBool(Texts.Ids.UseBanditOnlyTroopTrees, Order = 1, RequireRestart = false, HintText = Texts.Ids.UseBanditOnlyTroopTreesHint)]
    [SettingPropertyGroup(Texts.Ids.GroupTraining, GroupOrder = 1)]
    public bool UseBanditOnlyTroopTrees { get; private set; } = ModConfiguration.UseBanditOnlyTroopTreesDefault;

    [SettingPropertyInteger(Texts.Ids.DailyTrainingChance, ModConfiguration.PercentMin, ModConfiguration.PercentMax, Order = 2, RequireRestart = false, HintText = Texts.Ids.DailyTrainingChanceHint)]
    [SettingPropertyGroup(Texts.Ids.GroupTraining, GroupOrder = 1)]
    public int DailyTrainingChance { get; private set; } = ModConfiguration.DailyTrainingChanceDefault;

    // Item order = ModConfiguration.XpGiftLevel (MCM saves the index).
    [SettingPropertyDropdown(Texts.Ids.XpGift, Order = 3, RequireRestart = false, HintText = Texts.Ids.XpGiftHint)]
    [SettingPropertyGroup(Texts.Ids.GroupTraining, GroupOrder = 1)]
    public Dropdown<string> XpGift { get; internal set; } = new([Texts.Ids.XpOff, Texts.Ids.XpNormal, Texts.Ids.XpHard, Texts.Ids.XpHardest], (int)ModConfiguration.XpGiftDefault);

    [SettingPropertyInteger(Texts.Ids.UpgradeUnitsPercent, ModConfiguration.PercentMin, ModConfiguration.PercentMax, Order = 4, RequireRestart = false, HintText = Texts.Ids.UpgradeUnitsPercentHint)]
    [SettingPropertyGroup(Texts.Ids.GroupTraining, GroupOrder = 1)]
    public int UpgradeUnitsPercent { get; private set; } = ModConfiguration.UpgradeUnitsPercentDefault;

    [SettingPropertyInteger(Texts.Ids.MaxTrainingTier, ModConfiguration.MaxTrainingTierMin, ModConfiguration.MaxTrainingTierMax, Order = 5, RequireRestart = false, HintText = Texts.Ids.MaxTrainingTierHint)]
    [SettingPropertyGroup(Texts.Ids.GroupTraining, GroupOrder = 1)]
    public int MaxTrainingTier { get; private set; } = ModConfiguration.MaxTrainingTierDefault;

    [SettingPropertyInteger(Texts.Ids.GrowthChance, ModConfiguration.PercentMin, ModConfiguration.PercentMax, Order = 6, RequireRestart = false, HintText = Texts.Ids.GrowthChanceHint)]
    [SettingPropertyGroup(Texts.Ids.GroupTraining, GroupOrder = 1)]
    public int GrowthChance { get; private set; } = ModConfiguration.GrowthChanceDefault;

    [SettingPropertyInteger(Texts.Ids.GrowthPercent, ModConfiguration.PercentMin, ModConfiguration.PercentMax, Order = 7, RequireRestart = false, HintText = Texts.Ids.GrowthPercentHint)]
    [SettingPropertyGroup(Texts.Ids.GroupTraining, GroupOrder = 1)]
    public int GrowthPercent { get; private set; } = ModConfiguration.GrowthPercentDefault;
    #endregion

    #region Power & Balance
    [SettingPropertyInteger(Texts.Ids.GlobalPowerPercent, ModConfiguration.GlobalPowerPercentMin, ModConfiguration.PercentMax, Order = 0, RequireRestart = false, HintText = Texts.Ids.GlobalPowerPercentHint)]
    [SettingPropertyGroup(Texts.Ids.GroupPower, GroupOrder = 2)]
    public int GlobalPowerPercent { get; private set; } = ModConfiguration.GlobalPowerPercentDefault;

    // Item order = ModConfiguration.GoldRewardLevel (MCM saves the index).
    [SettingPropertyDropdown(Texts.Ids.GoldReward, Order = 1, RequireRestart = false, HintText = Texts.Ids.GoldRewardHint)]
    [SettingPropertyGroup(Texts.Ids.GroupPower, GroupOrder = 2)]
    public Dropdown<string> GoldReward { get; internal set; } = new([Texts.Ids.GoldLow, Texts.Ids.GoldNormal, Texts.Ids.GoldRich, Texts.Ids.GoldRichest], (int)ModConfiguration.GoldRewardDefault);
    #endregion

    #region Behavior & AI
    [SettingPropertyInteger(Texts.Ids.MaxStrongerTargetPercent, ModConfiguration.PercentMin, ModConfiguration.PercentMax, Order = 0, RequireRestart = false, HintText = Texts.Ids.MaxStrongerTargetPercentHint)]
    [SettingPropertyGroup(Texts.Ids.GroupBehavior, GroupOrder = 3)]
    public int MaxStrongerTargetPercent { get; private set; } = ModConfiguration.MaxStrongerTargetPercentDefault;

    [SettingPropertyInteger(Texts.Ids.MaxWeakerTargetPercent, ModConfiguration.PercentMin, ModConfiguration.PercentMax, Order = 1, RequireRestart = false, HintText = Texts.Ids.MaxWeakerTargetPercentHint)]
    [SettingPropertyGroup(Texts.Ids.GroupBehavior, GroupOrder = 3)]
    public int MaxWeakerTargetPercent { get; private set; } = ModConfiguration.MaxWeakerTargetPercentDefault;

    [SettingPropertyBool(Texts.Ids.IgnoreVillagersCaravans, Order = 2, RequireRestart = false, HintText = Texts.Ids.IgnoreVillagersCaravansHint)]
    [SettingPropertyGroup(Texts.Ids.GroupBehavior, GroupOrder = 3)]
    public bool IgnoreVillagersCaravans { get; private set; } = ModConfiguration.IgnoreVillagersCaravansDefault;

    [SettingPropertyInteger(Texts.Ids.ExecutionHonorXp, ModConfiguration.ExecutionHonorXpMin, ModConfiguration.ExecutionHonorXpMax, Order = 3, RequireRestart = false, HintText = Texts.Ids.ExecutionHonorXpHint)]
    [SettingPropertyGroup(Texts.Ids.GroupBehavior, GroupOrder = 3)]
    public int ExecutionHonorXp { get; private set; } = ModConfiguration.ExecutionHonorXpDefault;

    [SettingPropertyBool(Texts.Ids.AllowPillaging, Order = 4, RequireRestart = false, HintText = Texts.Ids.AllowPillagingHint)]
    [SettingPropertyGroup(Texts.Ids.GroupBehavior, GroupOrder = 3)]
    public bool AllowPillaging { get; private set; } = ModConfiguration.AllowPillagingDefault;

    [SettingPropertyFloatingInteger(Texts.Ids.PillagingChance, ModConfiguration.PercentMin, ModConfiguration.PercentMax, Order = 5, RequireRestart = false, HintText = Texts.Ids.PillagingChanceHint)]
    [SettingPropertyGroup(Texts.Ids.GroupBehavior, GroupOrder = 3)]
    public float PillagingChance { get; private set; } = ModConfiguration.PillagingChanceDefault;

    [SettingPropertyInteger(Texts.Ids.RaidCapPercent, ModConfiguration.RaidCapPercentMin, ModConfiguration.RaidCapPercentMax, Order = 6, RequireRestart = false, HintText = Texts.Ids.RaidCapPercentHint)]
    [SettingPropertyGroup(Texts.Ids.GroupBehavior, GroupOrder = 3)]
    public int RaidCapPercent { get; private set; } = ModConfiguration.RaidCapPercentDefault;
    #endregion

    #region Appearance & Customization
    [SettingPropertyText(Texts.Ids.BanditMilitiaString, Order = 0, RequireRestart = false, HintText = Texts.Ids.BanditMilitiaStringHint)]
    [SettingPropertyGroup(Texts.Ids.GroupAppearance, GroupOrder = 4)]
    public string BanditMilitiaString { get; set; } = ModConfiguration.BanditMilitiaStringDefault;

    [SettingPropertyText(Texts.Ids.LeaderlessBanditMilitiaString, Order = 1, RequireRestart = false, HintText = Texts.Ids.LeaderlessBanditMilitiaStringHint)]
    [SettingPropertyGroup(Texts.Ids.GroupAppearance, GroupOrder = 4)]
    public string LeaderlessBanditMilitiaString { get; set; } = ModConfiguration.LeaderlessBanditMilitiaStringDefault;

    [SettingPropertyBool(Texts.Ids.RandomBanners, Order = 2, RequireRestart = false, HintText = Texts.Ids.RandomBannersHint)]
    [SettingPropertyGroup(Texts.Ids.GroupAppearance, GroupOrder = 4)]
    public bool RandomBanners { get; set; } = ModConfiguration.RandomBannersDefault;

    [SettingPropertyInteger(Texts.Ids.FemaleSpawnChance, ModConfiguration.PercentMin, ModConfiguration.PercentMax, Order = 3, RequireRestart = false, HintText = Texts.Ids.FemaleSpawnChanceHint)]
    [SettingPropertyGroup(Texts.Ids.GroupAppearance, GroupOrder = 4)]
    public int FemaleSpawnChance { get; set; } = ModConfiguration.FemaleSpawnChanceDefault;

    [SettingPropertyBool(Texts.Ids.CheckVoiceGender, Order = 4, RequireRestart = false, HintText = Texts.Ids.CheckVoiceGenderHint)]
    [SettingPropertyGroup(Texts.Ids.GroupAppearance, GroupOrder = 4)]
    public bool CheckVoiceGender { get; set; } = ModConfiguration.CheckVoiceGenderDefault;
    #endregion

    #region UI & Notifications
    [SettingPropertyBool(Texts.Ids.ShowRaids, Order = 0, RequireRestart = false, HintText = Texts.Ids.ShowRaidsHint)]
    [SettingPropertyGroup(Texts.Ids.GroupUi, GroupOrder = 5)]
    public bool ShowRaids { get; set; } = ModConfiguration.ShowRaidsDefault;

    [SettingPropertyBool(Texts.Ids.SkipConversations, Order = 1, RequireRestart = false, HintText = Texts.Ids.SkipConversationsHint)]
    [SettingPropertyGroup(Texts.Ids.GroupUi, GroupOrder = 5)]
    public bool SkipConversations { get; set; } = ModConfiguration.SkipConversationsDefault;

    [SettingPropertyBool(Texts.Ids.RemovePrisonerMessages, Order = 2, RequireRestart = false, HintText = Texts.Ids.RemovePrisonerMessagesHint)]
    [SettingPropertyGroup(Texts.Ids.GroupUi, GroupOrder = 5)]
    public bool RemovePrisonerMessages { get; set; } = ModConfiguration.RemovePrisonerMessagesDefault;

    [SettingPropertyBool(Texts.Ids.ShowMilitiaMarkers, Order = 3, RequireRestart = false, HintText = Texts.Ids.ShowMilitiaMarkersHint)]
    [SettingPropertyGroup(Texts.Ids.GroupUi, GroupOrder = 5)]
    public bool ShowMilitiaMarkers { get; set; } = ModConfiguration.ShowMilitiaMarkersDefault;

    [SettingPropertyInteger(Texts.Ids.TrackedSizeMinimum, ModConfiguration.TrackedSizeMinimumMin, ModConfiguration.TrackedSizeMinimumMax, Order = 4, RequireRestart = false, HintText = Texts.Ids.TrackedSizeMinimumHint)]
    [SettingPropertyGroup(Texts.Ids.GroupUi, GroupOrder = 5)]
    public int TrackedSizeMinimum { get; set; } = ModConfiguration.TrackedSizeMinimumDefault;
    #endregion

    #region Advanced
    [SettingPropertyInteger(Texts.Ids.CooldownHours, ModConfiguration.CooldownHoursMin, ModConfiguration.CooldownHoursMax, Order = 0, RequireRestart = false, HintText = Texts.Ids.CooldownHoursHint)]
    [SettingPropertyGroup(Texts.Ids.GroupAdvanced, GroupOrder = 6)]
    public int CooldownHours { get; private set; } = ModConfiguration.CooldownHoursDefault;

    [SettingPropertyInteger(Texts.Ids.MaxItemValue, ModConfiguration.MaxItemValueMin, ModConfiguration.MaxItemValueMax, Order = 1, RequireRestart = false, HintText = Texts.Ids.MaxItemValueHint)]
    [SettingPropertyGroup(Texts.Ids.GroupAdvanced, GroupOrder = 6)]
    public int MaxItemValue { get; private set; } = ModConfiguration.MaxItemValueDefault;

    [SettingPropertyBool(Texts.Ids.IgnoreSizePenalty, Order = 2, RequireRestart = false, HintText = Texts.Ids.IgnoreSizePenaltyHint)]
    [SettingPropertyGroup(Texts.Ids.GroupAdvanced, GroupOrder = 6)]
    public bool IgnoreSizePenalty { get; private set; } = ModConfiguration.IgnoreSizePenaltyDefault;
    #endregion

    // "Technical settings" is always the last group and "Logging for debugging" its last entry.
    [SettingPropertyBool(Texts.Ids.LoggingEnabled, Order = 0, RequireRestart = false, HintText = Texts.Ids.LoggingEnabledHint)]
    [SettingPropertyGroup(Texts.Ids.GroupTechnical, GroupOrder = 7)]
    public bool LoggingEnabled { get; private set; } = ModConfiguration.LoggingEnabledDefault;
}
