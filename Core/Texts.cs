using System.Collections.Generic;
using TaleWorlds.Localization;

namespace BanditMilitiasRedux.Core;

internal static class Texts
{
    private const string LeaderNameVariable = "LEADER_NAME";
    private const string PartyNameVariable = "PARTY_NAME";
    private const string IsBanditVariable = "IS_BANDIT";
    private const string FirstVariable = "FIRST";
    private const string EpithetVariable = "EPITHET";
    private const string VillageVariable = "VILLAGE";
    private const string PartyVariable = "PARTY";
    private const string TownVariable = "TOWN";
    private const string GoldVariable = "GOLD";
    private const string RenownVariable = "RENOWN";
    private const string LeaderVariable = "LEADER";
    private const string ClanNameVariable = "CLAN_NAME";
    private const string OldClanVariable = "OLD_CLAN";
    private const string WinsVariable = "WINS";
    private const string ManyVariable = "MANY";
    private const string DefeatsVariable = "DEFEATS";
    private const string HunterVariable = "HUNTER";
    private const string ConcatFirstVariable = "A";
    private const string ConcatSecondVariable = "B";

    #region Settings
    public static readonly TextObject ModName = new(Ids.ModName);
    public static readonly TextObject XpOff = new(Ids.XpOff);
    public static readonly TextObject XpNormal = new(Ids.XpNormal);
    public static readonly TextObject XpHard = new(Ids.XpHard);
    public static readonly TextObject XpHardest = new(Ids.XpHardest);
    public static readonly TextObject GoldLow = new(Ids.GoldLow);
    public static readonly TextObject GoldNormal = new(Ids.GoldNormal);
    public static readonly TextObject GoldRich = new(Ids.GoldRich);
    public static readonly TextObject GoldRichest = new(Ids.GoldRichest);
    public static readonly TextObject BanditMilitiaDefaultName = new(Ids.BanditMilitiaDefaultName);
    public static readonly TextObject LeaderlessBanditMilitiaDefaultName = new(Ids.LeaderlessBanditMilitiaDefaultName);
    #endregion

    #region Party names
    internal static TextObject PartyName(object leaderName, string partyName) =>
        new(Ids.PartyName, new Dictionary<string, object> { [IsBanditVariable] = 1, [LeaderNameVariable] = leaderName, [PartyNameVariable] = partyName });

    internal static TextObject LeaderWithEpithet(TextObject firstName, TextObject epithet) =>
        new(Ids.LeaderWithEpithet, new Dictionary<string, object> { [FirstVariable] = firstName, [EpithetVariable] = epithet });
    #endregion

    #region Notoriety epithets
    // Tier N uses index N-1.
    public static readonly TextObject[] NotorietyEpithets =
    [
        new(Ids.EpithetBrash), new(Ids.EpithetBold), new(Ids.EpithetCruel), new(Ids.EpithetDreaded), new(Ids.EpithetBloody),
        new(Ids.EpithetMerciless), new(Ids.EpithetButcher), new(Ids.EpithetScourge), new(Ids.EpithetTerror), new(Ids.EpithetNightmare),
    ];
    #endregion

    #region Encyclopedia
    public static readonly TextObject EncyclopediaNoClan = new(Ids.EncyclopediaNoClan);

    internal static TextObject EncyclopediaBase(TextObject clanName) => new TextObject(Ids.EncyclopediaBase).SetTextVariable(ClanNameVariable, clanName);

    internal static TextObject EncyclopediaEpithetWithWins(TextObject epithet, int wins, int many) =>
        new TextObject(Ids.EncyclopediaEpithetWithWins).SetTextVariable(EpithetVariable, epithet).SetTextVariable(WinsVariable, wins).SetTextVariable(ManyVariable, many);

    internal static TextObject EncyclopediaDefeats(int defeats, int many) =>
        new TextObject(Ids.EncyclopediaDefeats).SetTextVariable(DefeatsVariable, defeats).SetTextVariable(ManyVariable, many);

    internal static TextObject EncyclopediaFear(TextObject hunter) => new TextObject(Ids.EncyclopediaFear).SetTextVariable(HunterVariable, hunter);

    internal static TextObject EncyclopediaDefected(TextObject oldClan) => new TextObject(Ids.EncyclopediaDefected).SetTextVariable(OldClanVariable, oldClan);

    internal static TextObject Concat(TextObject first, TextObject second) =>
        new TextObject(Ids.Concat).SetTextVariable(ConcatFirstVariable, first).SetTextVariable(ConcatSecondVariable, second);
    #endregion

    #region Notifications
    public static readonly TextObject UnknownTown = new(Ids.UnknownTown);
    public static readonly TextObject MilitiasCleared = new(Ids.MilitiasCleared);
    public static readonly TextObject NukeConfirmation = new(Ids.NukeConfirmation);
    public static readonly TextObject AreYouSure = new(Ids.AreYouSure);
    public static readonly TextObject Yes = new(Ids.Yes);
    public static readonly TextObject No = new(Ids.No);

    internal static TextObject VillageBeingRaided(TextObject village, TextObject party) =>
        new TextObject(Ids.VillageBeingRaided).SetTextVariable(VillageVariable, village).SetTextVariable(PartyVariable, party);

    internal static TextObject VillageRaided(string village, TextObject party, string town) =>
        new TextObject(Ids.VillageRaided).SetTextVariable(VillageVariable, village).SetTextVariable(PartyVariable, party).SetTextVariable(TownVariable, town);

    internal static TextObject PlayerVillageBeingRaided(TextObject party, TextObject village, string town) =>
        new TextObject(Ids.PlayerVillageBeingRaided).SetTextVariable(PartyVariable, party).SetTextVariable(VillageVariable, village).SetTextVariable(TownVariable, town);

    internal static TextObject Bounty(int gold, int renown, TextObject leader) =>
        new TextObject(Ids.Bounty).SetTextVariable(GoldVariable, gold).SetTextVariable(RenownVariable, renown).SetTextVariable(LeaderVariable, leader);
    #endregion

    internal static class Ids
    {
        #region Settings
        public const string ModName = "{=BMModName}Bandit Militias Redux";

        public const string GroupSpawning = "{=BMSpawning}Spawning & Formation";
        public const string HeroPoolCap = "{=BMHeroPoolCap}Bandit Hero Pool Cap (per clan)";
        public const string HeroPoolCapHint = "{=BMHeroPoolCapDesc}Maximum number of bandit militia heroes per bandit clan kept alive in the recycle pool. Released, escaped, and imprisoned BM heroes are reused up to this cap; killed/executed heroes are removed permanently and new heroes are being created.";
        public const string MergeableSize = "{=BMMergeSize}Minimum Size to Merge";
        public const string MergeableSizeHint = "{=BMMergeSizeDesc}Bandit parties smaller than this will not merge into a Bandit Militia.";
        public const string RandomSplitChance = "{=BMSplit}Daily Split Chance %";
        public const string RandomSplitChanceHint = "{=BMSplitDesc}How likely every day Bandit Militias is to split when large enough.";

        public const string GroupTraining = "{=BMTraining}Training & Growth";
        public const string CanTrain = "{=BMTrain}Enable Militia Training";
        public const string CanTrainHint = "{=BMTrainDesc}Bandit heroes will train their militias.";
        public const string UseBanditOnlyTroopTrees = "{=BMBanditOnly}Use Bandit-Only Troop Trees";
        public const string UseBanditOnlyTroopTreesHint = "{=BMBanditOnlyDesc}Militias only ever upgrade into other bandit troops, never into regular faction troops. Compatible with mods that add or extend bandit troop trees. If no further bandit troop is available at a tier, training simply stops there.";
        public const string DailyTrainingChance = "{=BMDailyTrain}Daily Training Chance";
        public const string DailyTrainingChanceHint = "{=BMDailyTrainDesc}Each day they might train further.";
        public const string XpGift = "{=BMXpBoost}Militia XP Boost";
        public const string XpGiftHint = "{=BMXpBoostDesc}Hardest grants enough XP to significantly upgrade troops.  Off grants no bonus XP.";
        public const string XpOff = "{=BMXpOff}Off";
        public const string XpNormal = "{=BMXpNormal}Normal";
        public const string XpHard = "{=BMXpHard}Hard";
        public const string XpHardest = "{=BMXpHardest}Hardest";
        public const string UpgradeUnitsPercent = "{=BMUpgrade}Upgrade % of Troops per Training";
        public const string UpgradeUnitsPercentHint = "{=BMUpgradeDesc}At most this percentage of troops will be upgraded each time training occurs. Looters are included.";
        public const string MaxTrainingTier = "{=BMTier}Max Troop Tier from Training";
        public const string MaxTrainingTierHint = "{=BMTierDesc}Training will never upgrade troops beyond this tier.";
        public const string GrowthChance = "{=BMGrowChance}Growth Chance Percent";
        public const string GrowthChanceHint = "{=BMGrowChanceDesc}Chance per day that the militia will gain more troops (0 for off).";
        public const string GrowthPercent = "{=BMGrowPercent}Growth Percent";
        public const string GrowthPercentHint = "{=BMGrowPercentDesc}Grow each troop type by this percent.";
        public const string HeroXpMultiplier = "{=BMR_k3VqZ8w}Militia Hero XP Multiplier";
        public const string HeroXpMultiplierHint = "{=BMR_Rf6nJ2x}Multiplies all skill experience that bandit militia heroes gain in the game (1 = vanilla). Needs Enable Militia Training.";
        public const string HeroXpPerWin = "{=BMR_p9TsL4c}Militia Leader XP per Win";
        public const string HeroXpPerWinHint = "{=BMR_Y7mWd1h}Skill experience a militia leader gains for each won battle in Leadership, Tactics, Riding or Athletics and the skills of the weapons in use. Scaled by enemy strength (0.25x to 3x). Every 7 wins also give a focus point and every 14 wins an attribute point. Needs Enable Militia Training. 0 turns off the experience only.";

        // The group and its first setting share the released id BMPower.
        public const string GroupPower = "{=BMPower}Global Power";
        public const string GlobalPowerPercent = "{=BMPower}Global Power";
        public const string GlobalPowerPercentHint = "{=BMPowerDesc}Caps the total combat strength of all Bandit Militias combined as a percentage of all other parties in the world. Higher values allow more and stronger BMs but can noticeably impact world balance.";
        public const string GoldReward = "{=BMGoldReward}Bandit Hero Gold Reward";
        public const string GoldRewardHint = "{=BMGoldRewardDesc}How much gold the player receives for defeating a Bandit Militia hero.";
        public const string GoldLow = "{=BMGoldLow}Low";
        public const string GoldNormal = "{=BMGoldNormal}Normal";
        public const string GoldRich = "{=BMGoldRich}Rich";
        public const string GoldRichest = "{=BMGoldRichest}Richest";

        public const string GroupBehavior = "{=BMAIBehavior}Behavior & AI";
        public const string MaxStrongerTargetPercent = "{=BMStronger}Max Stronger Target %";
        public const string MaxStrongerTargetPercentHint = "{=BMStrongerDesc}BMs will not engage parties stronger than themselves by more than this %. 100 means attack regardless of how strong the target is.";
        public const string MaxWeakerTargetPercent = "{=BMWeaker}Ignore Weaker Parties";
        public const string MaxWeakerTargetPercentHint = "{=BMWeakerDesc}10 means any party 10% weaker will be ignored.{newline}100 attacks without restriction.";
        public const string IgnoreVillagersCaravans = "{=BMIgnore}Ignore Villagers/Caravans";
        public const string IgnoreVillagersCaravansHint = "{=BMIgnoreDesc}They won't be attacked by BMs.";
        public const string ExecutionHonorXp = "{=BMExecHonorXp}Honor XP per Executed BM Leader";
        public const string ExecutionHonorXpHint = "{=BMExecHonorXpDesc}Honor trait XP the player gains for executing a Bandit Militia leader. 0 disables.";
        public const string AllowPillaging = "{=BMPillage}Allow Pillaging";
        public const string AllowPillagingHint = "{=BMPillageDesc}Allow pillaging.";
        public const string PillagingChance = "{=BMPillageChance}Pillaging Chance";
        public const string PillagingChanceHint = "{=BMPillageChanceDesc}The chance of Bandit Militias AI to consider raiding a village. It triggers once per in-game hour for every bandit militia party, so a smaller value is advised.";
        public const string RaidCapPercent = "{=BMRaidCapScale}Raid Cap Scale %";
        public const string RaidCapPercentHint = "{=BMRaidCapScaleDesc}Scales the maximum number of simultaneous village raids. The baseline is auto-calculated as (total villages ÷ 10). 100% = default. Lower = fewer raids at once, Higher = more raids at once.";

        public const string GroupAppearance = "{=BMAppearance}Appearance & Customization";
        public const string BanditMilitiaString = "{=BMStringSetting}Bandit Militia";
        public const string BanditMilitiaStringHint = "{=BMStringSettingDesc}What to name a Bandit Militia.";
        public const string BanditMilitiaDefaultName = "{=BMStringSettingDefault}Bandit Militia";
        public const string LeaderlessBanditMilitiaString = "{=BMLeaderlessStringSetting}Leaderless Bandit Militia";
        public const string LeaderlessBanditMilitiaStringHint = "{=BMLeaderlessStringSettingDesc}What to name a Bandit Militia with no leader.";
        public const string LeaderlessBanditMilitiaDefaultName = "{=BMLeaderlessStringSettingDefault}Leaderless Bandit Militia";
        public const string RandomBanners = "{=BMBanners}Random Banners";
        public const string RandomBannersHint = "{=BMBannersDesc}Bandit Militias will have unique banners, or the default bandit clan banners.";
        public const string FemaleSpawnChance = "{=BMGenderSlider}Female Leader Chance %";
        public const string FemaleSpawnChanceHint = "{=BMGenderSliderDesc}Chance for a Bandit Militia leader to be female. 0 = all male, 100 = all female.";
        public const string CheckVoiceGender = "{=BMCheckVoiceGender}Check Voice Gender";
        public const string CheckVoiceGenderHint = "{=BMCheckVoiceGenderDesc}Double-check whether the bandit voice lines match the leader's gender. Some official voice lines use male voices but do not specify gender, so female bandit leaders will speak with male voices if this option is disabled.";

        public const string GroupUi = "{=BMUI}UI & Notifications";
        public const string ShowRaids = "{=BMRaidNotices}Village Raid Notices";
        public const string ShowRaidsHint = "{=BMRaidNoticesDesc}When your fiefs are raided you'll see a banner message.";
        public const string SkipConversations = "{=BMSkipConversations}Skip Encounter Conversations";
        public const string SkipConversationsHint = "{=BMSkipConversationsDesc}Skip conversations when encountering Bandit Militias. Bribery will be unavailable if enabled.";
        public const string RemovePrisonerMessages = "{=BMRemovePrisonerMessages}Remove prisoner messages";
        public const string RemovePrisonerMessagesHint = "{=BMRemovePrisonerMessagesDesc}Remove the messages of Bandit Militia Heroes being taken or released as prisoners.";
        public const string ShowMilitiaMarkers = "{=BMMarkers}Militia Map Markers";
        public const string ShowMilitiaMarkersHint = "{=BMMarkersDesc}Shows all Bandit Militias on the map.";
        public const string TrackedSizeMinimum = "{=BMTrackSize}Minimum BM Size To Track";
        public const string TrackedSizeMinimumHint = "{=BMTrackSizeDesc}Any smaller BMs won't be tracked.";

        public const string GroupAdvanced = "{=BMAdvanced}Advanced";
        public const string CooldownHours = "{=BMCooldown}Merge/Split Cooldown (Hours)";
        public const string CooldownHoursHint = "{=BMCooldownDesc}A Bandit Militia cannot merge or split again until this many in-game hours have passed.";
        public const string MaxItemValue = "{=BMMaxValue}Max Item Value";
        public const string MaxItemValueHint = "{=BMMaxValueDesc}Limit the per-piece value of equipment given to the Heroes.{newline}Mostly for when other mods give you Hero loot.";
        public const string IgnoreSizePenalty = "{=BMIgnoreSizePenalty}Ignore Party Size Speed Penalty";
        public const string IgnoreSizePenaltyHint = "{=BMIgnoreSizePenaltyDesc}Bandit Militias move at full speed regardless of party size.";

        public const string GroupTechnical = "{=BMR_udVrtbS}Technical settings";
        public const string LoggingEnabled = "{=BMR_WA9I1JU}Logging for debugging";
        public const string LoggingEnabledHint = "{=BMR_2T70tyP}Logging for debugging (English only). [Default: disabled]";
        #endregion

        #region Party names
        public const string PartyName = "{=BMPartyName}{LEADER_NAME}'s {PARTY_NAME}";
        public const string LeaderWithEpithet = "{=BMRLeaderEpithet}{FIRST} {EPITHET}";
        #endregion

        #region Notoriety epithets
        public const string EpithetBrash = "{=BMR_EyG35bd}the Brash";
        public const string EpithetBold = "{=BMR_w4Wuqbo}the Bold";
        public const string EpithetCruel = "{=BMR_IPpCaSQ}the Cruel";
        public const string EpithetDreaded = "{=BMR_cbE581S}the Dreaded";
        public const string EpithetBloody = "{=BMR_XLdOUHp}the Bloody";
        public const string EpithetMerciless = "{=BMR_pPyyAyk}the Merciless";
        public const string EpithetButcher = "{=BMR_GnYKrqu}the Butcher";
        public const string EpithetScourge = "{=BMR_DRyTg8m}the Scourge";
        public const string EpithetTerror = "{=BMR_WC77kKY}the Terror";
        public const string EpithetNightmare = "{=BMR_t2fWU8c}the Nightmare";
        #endregion

        #region Encyclopedia
        public const string EncyclopediaBase = "{=BMREncBase}{LEADER.FIRSTNAME} is a brigand chief leading a band of cutthroats that raids under the name of the {CLAN_NAME}.";
        public const string EncyclopediaNoClan = "{=BMREncNoClan}masterless brigands";
        public const string EncyclopediaEpithetWithWins = "{=BMREncEpithet1}Across the land {?LEADER.GENDER}she{?}he{\\?} is spoken of as {LEADER.FIRSTNAME} {EPITHET}, victor of {WINS} {?MANY}battles{?}battle{\\?}.";
        public const string EncyclopediaDefeats = "{=BMREncDefeats}{?LEADER.GENDER}She{?}He{\\?} has been beaten in battle {DEFEATS} {?MANY}times{?}time{\\?}.";
        public const string EncyclopediaFear = "{=BMREncFear}Above all others, {?LEADER.GENDER}she{?}he{\\?} fears {HUNTER}.";
        public const string EncyclopediaDefected = "{=BMREncDefected}{LEADER.FIRSTNAME} once led a band of cutthroats that raided under the name of the {OLD_CLAN}. {?LEADER.GENDER}She{?}He{\\?} has since turned {?LEADER.GENDER}her{?}his{\\?} back on that life and now sells {?LEADER.GENDER}her{?}his{\\?} sword to whoever will pay for it.";
        // Joins encyclopedia sentences: "{A} {B}"; languages without spaces between sentences translate it as "{A}{B}".
        public const string Concat = "{=BMR_TOQCjcE}{A} {B}";
        #endregion

        #region Dialogs
        public const string Who = "{=BMRWho}Who are you, and who do you ride for?";
        public const string WhoAnswer = "{=BMRWhoAnswer}They call me {LEADER_NAME}. We answer to no lord and no king — only to coin and the open road.";
        public const string CaptiveIntro = "{=BMRCaptiveIntro}So it ends. Do what you will with me.";
        public const string CaptiveKeep = "{=BMRCaptiveKeep}You are my prisoner now.";
        public const string CaptiveRelease = "{=BMRCaptiveRelease}Get out of my sight.";
        public const string CaptiveExecute = "{=BMRCaptiveExecute}Do not expect mercy. Off with your head!";
        public const string CaptiveExecuteAnswer = "{=BMRCaptiveExecuteAnswer}Then be quick about it. My lads will remember your face — and they'll come looking for you.";
        public const string CaptiveExecuteConfirm = "{=BMRCaptiveExecuteConfirm}I care not. Prepare to die!";
        public const string CaptiveExecuteCancel = "{=BMRCaptiveExecuteCancel}Fine then. You are my prisoner now.";
        public const string PrisonerPretalk = "{=BMRPrisonerPretalk}What else do you want from me?";
        public const string PrisonerExecuteCancel = "{=BMRPrisonerExecuteCancel}On second thought... not today.";
        #endregion

        #region Notifications
        public const string VillageBeingRaided = "{=BMVillageBeingRaided}{VILLAGE} is being raided by {PARTY}!";
        public const string VillageRaided = "{=BMVillageRaided}{VILLAGE} raided! {PARTY} is fat with loot near {TOWN}!";
        public const string PlayerVillageBeingRaided = "{=BMPlayerVillageBeingRaided}{PARTY} is raiding your village {VILLAGE} near {TOWN}!";
        public const string Bounty = "{=BMRBounty}You collected a bounty of {GOLD} denars and {RENOWN} renown for {LEADER}.";
        public const string UnknownTown = "{=BMR_A0OadqW}(Unknown Town)";
        public const string MilitiasCleared = "{=BMR_yquQLhT}BANDIT MILITIAS CLEARED";
        public const string NukeConfirmation = "{=BMR_H6Jm3wr}This removes every Bandit Militia and all of their leaders from this campaign. It cannot be undone. Use it only before you disable the mod.";
        // Vanilla ids with the exact vanilla English text: the game supplies the translations, no xml rows.
        public const string AreYouSure = "{=awjomtnJ}Are you sure?";
        public const string Yes = "{=aeouhelq}Yes";
        public const string No = "{=8OkPHu4f}No";
        #endregion
    }
}
