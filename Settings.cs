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

        [SettingPropertyBool("{=BMSpawn}Enable Spontaneous Spawning", Order = 0, RequireRestart = false, HintText = "{=BMSpawnDesc}New Bandit Militias will form spontaneously as well as by merging together normally.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation", GroupOrder = 0)]
        public bool MilitiaSpawn { get; private set; } = false;

        [SettingPropertyInteger("{=BMSpawnChance}Hourly Spawn Chance %", 1, 100, Order = 1, RequireRestart = false, HintText = "{=BMSpawnChanceDesc}Bandit Militias will spawn hourly at this likelihood.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int SpawnChance { get; private set; } = 1;

        [SettingPropertyBool("{=BMSpawnLand}Allow Land Militias", Order = 2, RequireRestart = false, HintText = "{=BMSpawnLandDesc}Allow land Bandit Militias to exist. When disabled, land militias will not spawn, merge, or split.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public bool SpawnLandMilitias { get; private set; } = true;

        [SettingPropertyInteger("{=BMMaxPerClan}Max Militias Per Clan", 0, 50, Order = 3, RequireRestart = false, HintText = "{=BMMaxPerClanDesc}Maximum Bandit Militia parties per bandit clan. Set to 0 for no limit.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int MaxLandPartiesPerClan { get; private set; } = 10;

        [SettingPropertyInteger("{=BMMergeSize}Minimum Size to Merge", 1, 100, Order = 4, RequireRestart = false, HintText = "{=BMMergeSizeDesc}Bandit parties smaller than this will not merge into a Bandit Militia.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int MergeableSize { get; private set; } = 15;
        public int MinPartySize => MergeableSize * 2;

        [SettingPropertyInteger("{=BMSplit}Daily Split Chance %", 0, 100, Order = 5, RequireRestart = false, HintText = "{=BMSplitDesc}How likely every day Bandit Militias is to split when large enough.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int RandomSplitChance { get; private set; } = 5;

        [SettingPropertyInteger("{=BMDisperse}Disband Below Troop Count", 10, 100, Order = 6, RequireRestart = false, HintText = "{=BMDisperseDesc}Militias defeated with fewer than this many remaining troops will be disbanded.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int DisperseSize { get; private set; } = 20;

        // ==================== TRAINING & GROWTH ====================

        [SettingPropertyBool("{=BMTrain}Enable Militia Training", Order = 0, RequireRestart = false, HintText = "{=BMTrainDesc}Bandit heroes will train their militias.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth", GroupOrder = 1)]
        public bool CanTrain { get; private set; } = true;

        [SettingPropertyInteger("{=BMDailyTrain}Daily Training Chance %", 0, 100, Order = 1, RequireRestart = false, HintText = "{=BMDailyTrainDesc}Each day there is this % chance the militia will be trained.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public float TrainingChance { get; private set; } = 10;

        [SettingPropertyDropdown("{=BMXpBoost}Bonus XP on Training", Order = 2, RequireRestart = false, HintText = "{=BMXpBoostDesc}Extra XP granted when training occurs. Hardest grants enough to significantly upgrade troops. Off grants no bonus XP.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public Dropdown<string> XpGift { get; internal set; } = new(new[] { "{=BMXpOff}Off", "{=BMXpNormal}Normal", "{=BMXpHard}Hard", "{=BMXpHardest}Hardest" }, 1);

        [SettingPropertyInteger("{=BMUpgrade}Upgrade % of Troops per Training", 0, 100, Order = 3, RequireRestart = false, HintText = "{=BMUpgradeDesc}At most this percentage of troops will be upgraded each time training occurs. Looters are included.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int UpgradeUnitsPercent { get; private set; } = 25;

        [SettingPropertyInteger("{=BMTier}Max Troop Tier from Training", 1, 6, Order = 4, RequireRestart = false, HintText = "{=BMTierDesc}Training will never upgrade troops beyond this tier.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int MaxTrainingTier { get; private set; } = 4;

        [SettingPropertyInteger("{=BMGrowChance}Daily Growth Chance %", 0, 100, Order = 5, RequireRestart = false, HintText = "{=BMGrowChanceDesc}Each day there is this % chance the militia will gain troops. Set to 0 to disable growth.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int GrowthChance { get; private set; } = 50;

        [SettingPropertyInteger("{=BMGrowPercent}Troop Growth Amount %", 0, 100, Order = 6, RequireRestart = false, HintText = "{=BMGrowPercentDesc}When growth occurs, each troop type grows by this percentage of its current count.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int GrowthPercent { get; private set; } = 1;

        // ==================== POWER & BALANCE ====================

        [SettingPropertyInteger("{=BMPower}Global Power Cap %", 1, 100, Order = 0, RequireRestart = false, HintText = "{=BMPowerDesc}Caps the total combat strength of all Bandit Militias combined as a percentage of all other parties in the world. Higher values allow more and stronger BMs but can noticeably impact world balance.")]
        [SettingPropertyGroup("{=BMPower}Power & Balance", GroupOrder = 2)]
        public int GlobalPowerPercent { get; private set; } = 30;

        [SettingPropertyDropdown("{=BMGoldReward}Gold Reward on Hero Kill", Order = 1, RequireRestart = false, HintText = "{=BMGoldRewardDesc}How much gold the player receives for defeating a Bandit Militia hero.")]
        [SettingPropertyGroup("{=BMPower}Power & Balance")]
        public Dropdown<string> GoldReward { get; internal set; } = new(new[] { "{=BMGoldLow}Low", "{=BMGoldNormal}Normal", "{=BMGoldRich}Rich", "{=BMGoldRichest}Richest" }, 1);

        // ==================== BEHAVIOR & AI ====================

        [SettingPropertyInteger("{=BMWeaker}Attack Strength Tolerance %", 0, 100, Order = 0, RequireRestart = false, HintText = "{=BMWeakerDesc}BMs will not engage parties stronger than themselves by more than this %. 100 means attack regardless of strength difference.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI", GroupOrder = 3)]
        public int MaxStrengthDeltaPercent { get; private set; } = 50;

        [SettingPropertyBool("{=BMIgnore}Ignore Villagers & Caravans", Order = 1, RequireRestart = false, HintText = "{=BMIgnoreDesc}Bandit Militias will not attack villagers or caravans.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public bool IgnoreVillagersCaravans { get; private set; } = false;

        [SettingPropertyBool("{=BMPillage}Enable Village Raiding", Order = 2, RequireRestart = false, HintText = "{=BMPillageDesc}Allow Bandit Militias to raid villages.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public bool AllowPillaging { get; private set; } = true;

        [SettingPropertyFloatingInteger("{=BMPillageChance}Hourly Raid Chance %", 0, 100, Order = 3, RequireRestart = false, HintText = "{=BMPillageChanceDesc}Each hour every Bandit Militia has this % chance to consider raiding a nearby village. Keep this low.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public float PillagingChance { get; private set; } = 1;

        // ==================== APPEARANCE & CUSTOMIZATION ====================

        [SettingPropertyText("{=BMStringSetting}Militia Party Name", Order = 0, RequireRestart = false, HintText = "{=BMStringSettingDesc}What to name a Bandit Militia party.")]
        [SettingPropertyGroup("{=BMAppearance}Appearance & Customization", GroupOrder = 4)]
        public string BanditMilitiaString { get; set; } = "Bandit Militia";

        [SettingPropertyText("{=BMLeaderlessStringSetting}Leaderless Party Name", Order = 1, RequireRestart = false, HintText = "{=BMLeaderlessStringSettingDesc}What to name a Bandit Militia that has lost its leader.")]
        [SettingPropertyGroup("{=BMAppearance}Appearance & Customization")]
        public string LeaderlessBanditMilitiaString { get; set; } = "Leaderless Bandit Militia";

        [SettingPropertyBool("{=BMBanners}Unique Random Banners", Order = 2, RequireRestart = false, HintText = "{=BMBannersDesc}Each Bandit Militia gets a unique random banner. Disable to use the default bandit clan banner.")]
        [SettingPropertyGroup("{=BMAppearance}Appearance & Customization")]
        public bool RandomBanners { get; set; } = true;

        [SettingPropertyInteger("{=BMGenderSlider}Female Leader Chance %", 0, 100, Order = 3, RequireRestart = false, HintText = "{=BMGenderSliderDesc}Chance for a Bandit Militia leader to be female. 0 = all male, 100 = all female.")]
        [SettingPropertyGroup("{=BMAppearance}Appearance & Customization")]
        public int FemaleSpawnChance { get; set; } = 25;

        [SettingPropertyBool("{=BMCheckVoiceGender}Match Voice Lines to Gender", Order = 4, RequireRestart = false, HintText = "{=BMCheckVoiceGenderDesc}Ensures female leaders use female voice lines. Disable if female leaders are incorrectly silenced.")]
        [SettingPropertyGroup("{=BMAppearance}Appearance & Customization")]
        public bool CheckVoiceGender { get; set; } = true;

        // ==================== UI & NOTIFICATIONS ====================

        [SettingPropertyBool("{=BMRaidNotices}Notify on Village Raid", Order = 0, RequireRestart = false, HintText = "{=BMRaidNoticesDesc}Show a message when a Bandit Militia raids one of your villages.")]
        [SettingPropertyGroup("{=BMUI}UI & Notifications", GroupOrder = 5)]
        public bool ShowRaids { get; set; } = true;

        [SettingPropertyBool("{=BMSkipConversations}Skip Encounter Conversations", Order = 1, RequireRestart = false, HintText = "{=BMSkipConversationsDesc}Skip conversations when encountering Bandit Militias. Bribery will be unavailable if enabled.")]
        [SettingPropertyGroup("{=BMUI}UI & Notifications")]
        public bool SkipConversations { get; set; } = false;

        [SettingPropertyBool("{=BMRemovePrisonerMessages}Hide Prisoner Capture Messages", Order = 2, RequireRestart = false, HintText = "{=BMRemovePrisonerMessagesDesc}Suppress notifications when Bandit Militia heroes are taken or released as prisoners.")]
        [SettingPropertyGroup("{=BMUI}UI & Notifications")]
        public bool RemovePrisonerMessages { get; set; } = true;

        // ==================== ADVANCED ====================

        [SettingPropertyInteger("{=BMCooldown}Merge/Split Cooldown (Hours)", 0, 168, Order = 0, RequireRestart = false, HintText = "{=BMCooldownDesc}A Bandit Militia cannot merge or split again until this many in-game hours have passed.")]
        [SettingPropertyGroup("{=BMAdvanced}Advanced", GroupOrder = 6)]
        public int CooldownHours { get; private set; } = 24;

        [SettingPropertyInteger("{=BMMaxValue}Max Hero Equipment Value (Gold)", 1000, 100000, Order = 1, RequireRestart = false, HintText = "{=BMMaxValueDesc}Limits the gold value per equipment piece given to Bandit Militia heroes. Useful when other mods add very high-value loot.")]
        [SettingPropertyGroup("{=BMAdvanced}Advanced")]
        public int MaxItemValue { get; private set; } = 3750;

        [SettingPropertyBool("{=BMIgnoreSizePenalty}Ignore Party Size Speed Penalty", Order = 2, RequireRestart = false, HintText = "{=BMIgnoreSizePenaltyDesc}Bandit Militias move at full speed regardless of party size.")]
        [SettingPropertyGroup("{=BMAdvanced}Advanced")]
        public bool IgnoreSizePenalty { get; private set; } = true;

        [SettingPropertyDropdown("{=BMLoggingLevel}Log Level", Order = 3, RequireRestart = true, HintText = "{=BMDebugDesc}Change the log level. Requires restart.")]
        [SettingPropertyGroup("{=BMAdvanced}Advanced")]
        public Dropdown<LogLevel> MinLogLevel { get; private set; } = new([LogLevel.Trace, LogLevel.Debug, LogLevel.Information, LogLevel.Warning, LogLevel.Error, LogLevel.Critical, LogLevel.None], 2);

        [SettingPropertyBool("{=BMTesting}Testing Mode", Order = 4, RequireRestart = false, HintText = "{=BMTestingDesc}Teleports all Bandit Militias near the player.")]
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