using MCM.Abstractions.Attributes;
using MCM.Abstractions.Attributes.v2;
using MCM.Abstractions.Base.Global;
using MCM.Common;
using TaleWorlds.Localization;

namespace BanditMilitiasRedux
{
    public class MCMSettings : AttributeGlobalSettings<MCMSettings>
    {
        public delegate void OnSettingsChangedDelegate();
        public static event OnSettingsChangedDelegate? OnSettingsChanged;
        public override string Id => "BanditMilitiasRedux";
        public override string DisplayName => $"Bandit Militias Redux";
        public override string FolderName => "BanditMilitiasRedux";
        public override string FormatType => "json2";
        public override void OnPropertyChanged(string? propertyName = null)
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

        // ==================== SPAWNING & FORMATION ====================

        [SettingPropertyInteger("{=BMHeroPoolCap}Bandit Hero Pool Cap (per clan)", 1, 20, Order = 0, RequireRestart = false, HintText = "{=BMHeroPoolCapDesc}Maximum number of bandit militia heroes per bandit clan kept alive in the recycle pool. Released, escaped, and imprisoned BM heroes are reused up to this cap; killed/executed heroes are removed permanently and new heroes are being created.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int HeroPoolCap { get; private set; } = 4;

        [SettingPropertyInteger("{=BMMergeSize}Minimum Size to Merge", 1, 100, Order = 1, RequireRestart = false, HintText = "{=BMMergeSizeDesc}Bandit parties smaller than this will not merge into a Bandit Militia.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int MergeableSize { get; private set; } = 15;
        public int MinPartySize => MergeableSize * 2;

        [SettingPropertyInteger("{=BMSplit}Daily Split Chance %", 0, 100, Order = 2, RequireRestart = false, HintText = "{=BMSplitDesc}How likely every day Bandit Militias is to split when large enough.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int RandomSplitChance { get; private set; } = 5;

        [SettingPropertyInteger("{=BMDisperse}Disband Below Troop Count", 10, 100, Order = 3, RequireRestart = false, HintText = "{=BMDisperseDesc}Militias defeated with fewer than this many remaining troops will be disbanded.")]
        [SettingPropertyGroup("{=BMSpawning}Spawning & Formation")]
        public int DisperseSize { get; private set; } = 20;

        // ==================== TRAINING & GROWTH ====================

        [SettingPropertyBool("{=BMTrain}Enable Militia Training", Order = 0, RequireRestart = false, HintText = "{=BMTrainDesc}Bandit heroes will train their militias.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth", GroupOrder = 1)]
        public bool CanTrain { get; private set; } = true;

        [SettingPropertyInteger("{=BMDailyTrain}Daily Training Chance %", 0, 100, Order = 1, RequireRestart = false, HintText = "{=BMDailyTrainDesc}Each day there is this % chance the militia will be trained.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int TrainingChance { get; private set; } = 10;

        [SettingPropertyDropdown("{=BMXpBoost}Bonus XP on Training", Order = 2, RequireRestart = false, HintText = "{=BMXpBoostDesc}Extra XP granted when training occurs. Hardest grants enough to significantly upgrade troops. Off grants no bonus XP.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public Dropdown<string> XpGift { get; internal set; } = new(["{=BMXpOff}Off", "{=BMXpNormal}Normal", "{=BMXpHard}Hard", "{=BMXpHardest}Hardest"], 1);

        [SettingPropertyInteger("{=BMUpgrade}Upgrade % of Troops per Training", 0, 100, Order = 3, RequireRestart = false, HintText = "{=BMUpgradeDesc}At most this percentage of troops will be upgraded each time training occurs. Looters are included.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int UpgradeUnitsPercent { get; private set; } = 25;

        [SettingPropertyInteger("{=BMTier}Max Troop Tier from Training", 1, 6, Order = 4, RequireRestart = false, HintText = "{=BMTierDesc}Training will never upgrade troops beyond this tier.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int MaxTrainingTier { get; private set; } = 4;

        [SettingPropertyInteger("{=BMGrowChance}Daily Growth Chance %", 0, 100, Order = 5, RequireRestart = false, HintText = "{=BMGrowChanceDesc}Each day there is this % chance the militia will gain troops. Set to 0 to disable growth.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int GrowthChance { get; private set; } = 50;

        [SettingPropertyInteger("{=BMGrowPercent}Troop Growth Amount %", 0, 100, Order = 6, RequireRestart = false, HintText = "{=BMGrowPercentDesc}When growth occurs, total party size grows by this percentage of its current count.")]
        [SettingPropertyGroup("{=BMTraining}Training & Growth")]
        public int GrowthPercent { get; private set; } = 1;

        // ==================== POWER & BALANCE ====================

        [SettingPropertyInteger("{=BMPower}Global Power Cap %", 1, 100, Order = 0, RequireRestart = false, HintText = "{=BMPowerDesc}Caps the total combat strength of all Bandit Militias combined as a percentage of all other parties in the world. Higher values allow more and stronger BMs but can noticeably impact world balance.")]
        [SettingPropertyGroup("{=BMPower}Power & Balance", GroupOrder = 2)]
        public int GlobalPowerPercent { get; private set; } = 20;

        [SettingPropertyDropdown("{=BMGoldReward}Gold Reward on Hero Kill", Order = 1, RequireRestart = false, HintText = "{=BMGoldRewardDesc}How much gold the player receives for defeating a Bandit Militia hero.")]
        [SettingPropertyGroup("{=BMPower}Power & Balance")]
        public Dropdown<string> GoldReward { get; internal set; } = new(["{=BMGoldLow}Low", "{=BMGoldNormal}Normal", "{=BMGoldRich}Rich", "{=BMGoldRichest}Richest"], 1);

        // ==================== BEHAVIOR & AI ====================

        [SettingPropertyInteger("{=BMStronger}Max Stronger Target %", 0, 100, Order = 0, RequireRestart = false, HintText = "{=BMStrongerDesc}BMs will not engage parties stronger than themselves by more than this %. 100 means attack regardless of how strong the target is.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI", GroupOrder = 3)]
        public int MaxStrongerTargetPercent { get; private set; } = 0;

        [SettingPropertyInteger("{=BMWeaker}Max Weaker Target %", 0, 100, Order = 1, RequireRestart = false, HintText = "{=BMWeakerDesc}BMs will not engage parties weaker than themselves by more than this %. 100 means attack regardless of how weak the target is.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public int MaxWeakerTargetPercent { get; private set; } = 50;

        [SettingPropertyBool("{=BMIgnore}Ignore Villagers & Caravans", Order = 2, RequireRestart = false, HintText = "{=BMIgnoreDesc}Bandit Militias will not attack villagers or caravans.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public bool IgnoreVillagersCaravans { get; private set; } = false;

        [SettingPropertyBool("{=BMPillage}Enable Village Raiding", Order = 3, RequireRestart = false, HintText = "{=BMPillageDesc}Allow Bandit Militias to raid villages.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public bool AllowPillaging { get; private set; } = true;

        [SettingPropertyFloatingInteger("{=BMPillageChance}Hourly Raid Chance %", 0, 100, Order = 4, RequireRestart = false, HintText = "{=BMPillageChanceDesc}Each hour every Bandit Militia has this % chance to consider raiding a nearby village. Keep this low.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public float PillagingChance { get; private set; } = 1;

        [SettingPropertyInteger("{=BMRaidCapScale}Raid Cap Scale %", 10, 500, Order = 5, RequireRestart = false, HintText = "{=BMRaidCapScaleDesc}Scales the maximum number of simultaneous village raids. The baseline is auto-calculated as (total villages ÷ 10). 100% = default. Lower = fewer raids at once, Higher = more raids at once.")]
        [SettingPropertyGroup("{=BMAIBehavior}Behavior & AI")]
        public int RaidCapPercent { get; private set; } = 100;

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

        [SettingPropertyBool("{=BMChatLogging}Enable Chat Logging", Order = 4, RequireRestart = false, HintText = "{=BMChatLoggingDesc}Write diagnostic messages to the in-game chat. Off by default; enable only when troubleshooting.")]
        [SettingPropertyGroup("{=BMAdvanced}Advanced")]
        public bool EnableChatLogging { get; private set; } = false;

        [SettingPropertyBool("{=BMFileLogging}Enable File Logging", Order = 4, RequireRestart = false, HintText = "{=BMFileLoggingDesc}Write diagnostic messages to Modules\\BanditMilitiasRedux\\Logs\\BMR_*.log. Off by default; enable only when troubleshooting.")]
        [SettingPropertyGroup("{=BMAdvanced}Advanced")]
        public bool EnableFileLogging { get; private set; } = false;
    }
}