using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;
using Microsoft.Extensions.Logging;
using TaleWorlds.Localization;

namespace BanditMilitias
{
    public class Settings : AttributeGlobalSettings<Settings>
    {
        public delegate void OnSettingsChangedDelegate();
        public static event OnSettingsChangedDelegate OnSettingsChanged;
        public override string FormatType => "json";
        public override string FolderName => "BanditMilitias";

        // ==================== SPAWNING & FORMATION ====================

        [SettingPropertyBool("{=BMSpawn}Bandit Militias Spawn (From Thin Air)", Order = 0, RequireRestart = false, HintText = "{=BMSpawnDesc}New Bandit Militias will form spontaneously as well as by merging together normally.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation", GroupOrder = 0)]
        public bool MilitiaSpawn { get; private set; } = false;

        [SettingPropertyInteger("{=BMSpawnChance}Spawn Chance Percent", 1, 100, Order = 1, RequireRestart = false, HintText = "{=BMSpawnChanceDesc}Bandit Militias will spawn hourly at this likelihood.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int SpawnChance { get; private set; } = 1;

        [SettingPropertyInteger("{=BMMinSize}Minimum Size", 1, 100, Order = 2, RequireRestart = false, HintText = "{=BMMinSizeDesc}No Bandit Militias smaller than this will form.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int MinPartySize { get; private set; } = 20;

        [SettingPropertyInteger("{=BMMergeSize}Mergeable Party Size", 1, 100, Order = 3, RequireRestart = false, HintText = "{=BMMergeSizeDesc}Small looter and bandit parties won't merge.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int MergeableSize { get; private set; } = 10;

        [SettingPropertyInteger("{=BMSplit}Random Daily Split Chance", 0, 100, Order = 4, RequireRestart = false, HintText = "{=BMSplitDesc}How likely every day Bandit Militias is to split when large enough.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int RandomSplitChance { get; private set; } = 5;

        [SettingPropertyInteger("{=BMCooldown}Change Cooldown", 0, 168, Order = 5, RequireRestart = false, HintText = "{=BMCooldownDesc}Bandit Militias won't merge or split a second time until this many hours go by.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int CooldownHours { get; private set; } = 24;

        [SettingPropertyInteger("{=BMDisperse}Disperse Militia Size", 10, 100, Order = 6, RequireRestart = false, HintText = "{=BMDisperseDesc}Militias defeated with fewer than this many remaining troops will be dispersed.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int DisperseSize { get; private set; } = 20;

        // ==================== TRAINING & GROWTH ====================

        [SettingPropertyBool("{=BMTrain}Train Militias", Order = 0, RequireRestart = false, HintText = "{=BMTrainDesc}Bandit heroes will train their militias.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth", GroupOrder = 1)]
        public bool CanTrain { get; private set; } = true;

        [SettingPropertyInteger("{=BMDailyTrain}Daily Training Chance", 0, 100, Order = 1, RequireRestart = false, HintText = "{=BMDailyTrainDesc}Each day they might train further.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public float TrainingChance { get; private set; } = 10;

        [SettingPropertyDropdown("{=BMXpBoost}Militia XP Boost", Order = 2, RequireRestart = false, HintText = "{=BMXpBoostDesc}Hardest grants enough XP to significantly upgrade troops. Off grants no bonus XP.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public Dropdown<string> XpGift { get; internal set; } = new(new[] { "{=BMXpOff}Off", "{=BMXpNormal}Normal", "{=BMXpHard}Hard", "{=BMXpHardest}Hardest" }, 1);

        [SettingPropertyInteger("{=BMLooter}Looter Conversions", 0, 100, Order = 3, RequireRestart = false, HintText = "How many looters get made into better units when training.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int LooterUpgradePercent { get; private set; } = 15;

        [SettingPropertyInteger("{=BMUpgrade}Upgrade Units", 0, 100, Order = 4, RequireRestart = false, HintText = "{=BMUpgradeDesc}Upgrade (at most) this percentage of troops when training occurs.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int UpgradeUnitsPercent { get; private set; } = 25;

        [SettingPropertyInteger("{=BMTier}Max Training Tier", 1, 6, Order = 5, RequireRestart = false, HintText = "{=BMTierDesc}BM won't train any units past this tier.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int MaxTrainingTier { get; private set; } = 4;

        [SettingPropertyInteger("{=BMGrowChance}Growth Chance Percent", 0, 100, Order = 6, RequireRestart = false, HintText = "{=BMGrowChanceDesc}Chance per day that the militia will gain more troops (0 for off).")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int GrowthChance { get; private set; } = 50;

        [SettingPropertyInteger("{=BMGrowPercent}Growth Percent", 0, 100, Order = 7, RequireRestart = false, HintText = "{=BMGrowPercentDesc}Grow each troop type by this percent.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int GrowthPercent { get; private set; } = 1;

        // ==================== POWER & BALANCE ====================

        [SettingPropertyInteger("{=BMPower}Global Power", 0, 1000, Order = 0, RequireRestart = false, HintText = "{=BMPowerDesc}Major setting. Setting higher means more, bigger BMs.")]
        [SettingPropertyGroup("{=BMPower}Power & Balance", GroupOrder = 2)]
        public int GlobalPowerPercent { get; private set; } = 15;

        [SettingPropertyInteger("{=BMMaxValue}Max Item Value", 1000, 1000000, Order = 1, RequireRestart = false, HintText = "{=BMMaxValueDesc}Limit the per-piece value of equipment given to the Heroes. Mostly for when other mods give you Hero loot.")]
        [SettingPropertyGroup("{=BMPower}Power & Balance")]
        public int MaxItemValue { get; private set; } = 2500;

        [SettingPropertyDropdown("{=BMGoldReward}Bandit Hero Gold Reward", Order = 2, RequireRestart = false)]
        [SettingPropertyGroup("{=BMPower}Power & Balance")]
        public Dropdown<string> GoldReward { get; internal set; } = new(new[] { "{=BMGoldLow}Low", "{=BMGoldNormal}Normal", "{=BMGoldRich}Rich", "{=BMGoldRichest}Richest" }, 1);

        // ==================== BEHAVIOR & AI ====================

        [SettingPropertyInteger("{=BMWeaker}Ignore Weaker Parties", 0, 100, Order = 0, RequireRestart = false, HintText = "{=BMWeakerDesc}10 means any party 10% weaker will be ignored. 100 attacks without restriction.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI", GroupOrder = 3)]
        public int MaxStrengthDeltaPercent { get; private set; } = 50;

        [SettingPropertyBool("{=BMIgnore}Ignore Villagers/Caravans", Order = 1, RequireRestart = false, HintText = "{=BMIgnoreDesc}They won't be attacked by BMs.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public bool IgnoreVillagersCaravans { get; private set; } = false;

        [SettingPropertyBool("{=BMPillage}Allow Pillaging", Order = 2, RequireRestart = false, HintText = "{=BMPillageDesc}Allow PILLAGING!.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public bool AllowPillaging { get; private set; } = true;

        [SettingPropertyFloatingInteger("{=BMPillageChance}Pillaging Chance", 0, 100, Order = 3, RequireRestart = false, HintText = "{=BMPillageChanceDesc}The chance of Bandit Militias AI to consider raiding a village. It triggers once per in-game hour for every bandit militia party, so a smaller value is advised.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public float PillagingChance { get; private set; } = 1;

        [SettingPropertyBool("{=BMIgnoreSizePenalty}Ignore Size Penalty", Order = 4, RequireRestart = false, HintText = "{=BMIgnoreSizePenaltyDesc}Bandit Militias will move at normal speed regardless of its party size.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public bool IgnoreSizePenalty { get; private set; } = true;

        // ==================== APPEARANCE & CUSTOMIZATION ====================

        [SettingPropertyText("{=BMStringSetting}Bandit Militia", Order = 0, RequireRestart = false, HintText = "{=BMStringSettingDesc}What to name a Bandit Militia.")]
        [SettingPropertyGroup("{=BMAppearance}Appearance & Customization", GroupOrder = 4)]
        public string BanditMilitiaString { get; set; } = "Bandit Militia";

        [SettingPropertyText("{=BMLeaderlessStringSetting}Leaderless Bandit Militia", Order = 1, RequireRestart = false, HintText = "{=BMLeaderlessStringSettingDesc}What to name a Bandit Militia with no leader.")]
        [SettingPropertyGroup("{=BMAppearance}Appearance & Customization")]
        public string LeaderlessBanditMilitiaString { get; set; } = "Leaderless Bandit Militia";

        [SettingPropertyBool("{=BMBanners}Random Banners", Order = 2, RequireRestart = false, HintText = "{=BMBannersDesc}BMs will have unique banners, or basic bandit clan ones.")]
        [SettingPropertyGroup("{=BMAppearance}Appearance & Customization")]
        public bool RandomBanners { get; set; } = true;

        [SettingPropertyInteger("{=BMGenderSlider}Leader Gender Ratio", 0, 100, Order = 3, RequireRestart = false, HintText = "{=BMGenderSliderDesc}Chance for Bandit Militia leaders to be female. Set to 0 for all male, or 100 for all female.")]
        [SettingPropertyGroup("{=BMAppearance}Appearance & Customization")]
        public int FemaleSpawnChance { get; set; } = 25;

        [SettingPropertyBool("{=BMCheckVoiceGender}Check Voice Gender", Order = 4, RequireRestart = false, HintText = "{=BMCheckVoiceGenderDesc}Double-check if the bandit voice lines match the gender. There are some official voice lines with male voices but don't specify gender, so female bandit leaders will speak with male voices if this option is disabled.")]
        [SettingPropertyGroup("{=BMAppearance}Appearance & Customization")]
        public bool CheckVoiceGender { get; set; } = true;

        // ==================== UI & NOTIFICATIONS ====================

        [SettingPropertyBool("{=BMMarkers}Militia Map Markers", Order = 0, RequireRestart = false, HintText = "{=BMMarkersDesc}Have omniscient view of BMs.")]
        [SettingPropertyGroup("{=BMUI}UI & Notifications", GroupOrder = 5)]
        public bool Trackers { get; private set; } = false;

        [SettingPropertyInteger("{=BMTrackSize}Minimum BM Size To Track", 1, 500, Order = 1, RequireRestart = false, HintText = "{=BMTrackSizeDesc}Any smaller BMs won't be tracked.")]
        [SettingPropertyGroup("{=BMUI}UI & Notifications")]
        public int TrackedSizeMinimum { get; private set; } = 50;

        [SettingPropertyBool("{=BMRaidNotices}Village Raid Notices", Order = 2, RequireRestart = false, HintText = "{=BMRaidNoticesDesc}When your fiefs are raided you'll see a banner message.")]
        [SettingPropertyGroup("{=BMUI}UI & Notifications")]
        public bool ShowRaids { get; set; } = true;

        [SettingPropertyBool("{=BMSkipConversations}Skip Conversations", Order = 3, RequireRestart = false, HintText = "{=BMSkipConversationsDesc}Skip conversations with Bandit Militias. You won't be able to bribe them if enabled.")]
        [SettingPropertyGroup("{=BMUI}UI & Notifications")]
        public bool SkipConversations { get; set; } = false;

        [SettingPropertyBool("{=BMRemovePrisonerMessages}Remove Prisoner Messages", Order = 4, RequireRestart = false, HintText = "{=BMRemovePrisonerMessagesDesc}Remove the messages of Bandit Militia Heroes being taken or released as prisoners.")]
        [SettingPropertyGroup("{=BMUI}UI & Notifications")]
        public bool RemovePrisonerMessages { get; set; } = true;

        // ==================== ADVANCED ====================

        [SettingPropertyDropdown("{=BMLoggingLevel}Log Level", Order = 0, RequireRestart = true, HintText = "{=BMDebugDesc}Change the log level, requires restart.")]
        [SettingPropertyGroup("{=BMAdvanced}Advanced", GroupOrder = 6)]
        public Dropdown<LogLevel> MinLogLevel { get; private set; } = new([LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical, LogLevel.None], 2);

        [SettingPropertyBool("{=BMTesting}Testing Mode", Order = 1, RequireRestart = false, HintText = "{=BMTestingDesc}Teleports BMs to you.")]
        [SettingPropertyGroup("{=BMAdvanced}Advanced")]
        public bool TestingMode { get; internal set; }

        // ==================== PRIVATE FIELDS ====================

        private const string id = "BanditMilitias";
        private string displayName = $"Bandit Militias Redux";

        // ==================== OVERRIDES ====================

        public override string Id => id;
        public override string DisplayName => displayName;

        public override void OnPropertyChanged(string propertyName = null)
        {
            base.OnPropertyChanged(propertyName);
            VerifyProperties();

            OnSettingsChanged?.Invoke();
        }

        private void VerifyProperties()
        {
            if (string.IsNullOrWhiteSpace(BanditMilitiaString))
            {
                BanditMilitiaString = new TextObject("{=BMStringSettingDefault}Bandit Militia").ToString();
            }

            if (string.IsNullOrWhiteSpace(LeaderlessBanditMilitiaString))
            {
                LeaderlessBanditMilitiaString = new TextObject("{=BMLeaderlessStringSettingDefault}Leaderless Bandit Militia").ToString();
            }
        }
    }
}