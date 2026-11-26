using System;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Serialization;
using BanditMilitiasRedux.Data;
using BanditMilitiasRedux.Diagnostics;

namespace BanditMilitiasRedux.Core;

// The only place mod code reads settings from. MCM loaded -> MCM. MCM not loaded -> ModConfiguration.xml.
internal static class ModSettings
{
    private const string McmAssemblyName = "MCMv5";

    private static ModConfiguration _xml = new();
    private static XmlSerializer? _serializer;
    private static bool _useMcm;
    private static bool _randomBannersForcedOff;

    // Raised when the player changes a setting in MCM (never in XML mode: the file is read once per campaign start).
    internal static event Action? Changed;

    #region Spawning & Formation
    internal static int HeroPoolCap => _useMcm ? McmHeroPoolCap() : _xml.SpawningAndFormation.HeroPoolCap;
    internal static int MergeableSize => _useMcm ? McmMergeableSize() : _xml.SpawningAndFormation.MergeableSize;
    internal static int MinPartySize => MergeableSize * Balance.MinPartySizeMergeMultiplier;
    internal static int RandomSplitChance => _useMcm ? McmRandomSplitChance() : _xml.SpawningAndFormation.RandomSplitChance;
    #endregion

    #region Training & Growth
    internal static bool CanTrain => _useMcm ? McmCanTrain() : _xml.TrainingAndGrowth.CanTrain;
    internal static bool UseBanditOnlyTroopTrees => _useMcm ? McmUseBanditOnlyTroopTrees() : _xml.TrainingAndGrowth.UseBanditOnlyTroopTrees;
    internal static int DailyTrainingChance => _useMcm ? McmDailyTrainingChance() : _xml.TrainingAndGrowth.DailyTrainingChance;
    // Index into Balance.DifficultyXpValues / TrainingIterationsByXpGift (= MCM dropdown index).
    internal static int XpGiftIndex => ValidIndex(_useMcm ? McmXpGiftIndex() : (int)_xml.TrainingAndGrowth.XpGift, Balance.DifficultyXpValues.Length, (int)ModConfiguration.XpGiftDefault);
    internal static int UpgradeUnitsPercent => _useMcm ? McmUpgradeUnitsPercent() : _xml.TrainingAndGrowth.UpgradeUnitsPercent;
    internal static int MaxTrainingTier => _useMcm ? McmMaxTrainingTier() : _xml.TrainingAndGrowth.MaxTrainingTier;
    internal static int GrowthChance => _useMcm ? McmGrowthChance() : _xml.TrainingAndGrowth.GrowthChance;
    internal static int GrowthPercent => _useMcm ? McmGrowthPercent() : _xml.TrainingAndGrowth.GrowthPercent;
    // Multiplies every skill XP vanilla gives to militia heroes (and the level XP that comes with it).
    internal static float HeroXpMultiplier => _useMcm ? McmHeroXpMultiplier() : _xml.TrainingAndGrowth.HeroXpMultiplier;
    // Base skill XP a militia leader gets per won battle, before the enemy-strength factor.
    internal static int HeroXpPerWin => _useMcm ? McmHeroXpPerWin() : _xml.TrainingAndGrowth.HeroXpPerWin;
    #endregion

    #region Power & Balance
    internal static int GlobalPowerPercent => _useMcm ? McmGlobalPowerPercent() : _xml.PowerAndBalance.GlobalPowerPercent;
    // Index into Balance.GoldValues (= MCM dropdown index).
    internal static int GoldRewardIndex => ValidIndex(_useMcm ? McmGoldRewardIndex() : (int)_xml.PowerAndBalance.GoldReward, Balance.GoldValues.Length, (int)ModConfiguration.GoldRewardDefault);
    #endregion

    #region Behavior & AI
    internal static int MaxStrongerTargetPercent => _useMcm ? McmMaxStrongerTargetPercent() : _xml.BehaviorAndAi.MaxStrongerTargetPercent;
    internal static int MaxWeakerTargetPercent => _useMcm ? McmMaxWeakerTargetPercent() : _xml.BehaviorAndAi.MaxWeakerTargetPercent;
    internal static bool IgnoreVillagersCaravans => _useMcm ? McmIgnoreVillagersCaravans() : _xml.BehaviorAndAi.IgnoreVillagersCaravans;
    internal static int ExecutionHonorXp => _useMcm ? McmExecutionHonorXp() : _xml.BehaviorAndAi.ExecutionHonorXp;
    internal static bool AllowPillaging => _useMcm ? McmAllowPillaging() : _xml.BehaviorAndAi.AllowPillaging;
    internal static float PillagingChance => _useMcm ? McmPillagingChance() : _xml.BehaviorAndAi.PillagingChance;
    internal static int RaidCapPercent => _useMcm ? McmRaidCapPercent() : _xml.BehaviorAndAi.RaidCapPercent;
    #endregion

    #region Appearance & Customization
    internal static string BanditMilitiaString => _useMcm ? McmBanditMilitiaString() : XmlTextOrDefault(_xml.AppearanceAndCustomization.BanditMilitiaString, Texts.BanditMilitiaDefaultName.ToString());
    internal static string LeaderlessBanditMilitiaString => _useMcm ? McmLeaderlessBanditMilitiaString() : XmlTextOrDefault(_xml.AppearanceAndCustomization.LeaderlessBanditMilitiaString, Texts.LeaderlessBanditMilitiaDefaultName.ToString());
    internal static bool RandomBanners => _useMcm ? McmRandomBanners() : !_randomBannersForcedOff && _xml.AppearanceAndCustomization.RandomBanners;
    internal static int FemaleSpawnChance => _useMcm ? McmFemaleSpawnChance() : _xml.AppearanceAndCustomization.FemaleSpawnChance;
    internal static bool CheckVoiceGender => _useMcm ? McmCheckVoiceGender() : _xml.AppearanceAndCustomization.CheckVoiceGender;
    #endregion

    #region UI & Notifications
    internal static bool ShowRaids => _useMcm ? McmShowRaids() : _xml.UiAndNotifications.ShowRaids;
    internal static bool SkipConversations => _useMcm ? McmSkipConversations() : _xml.UiAndNotifications.SkipConversations;
    internal static bool RemovePrisonerMessages => _useMcm ? McmRemovePrisonerMessages() : _xml.UiAndNotifications.RemovePrisonerMessages;
    internal static bool ShowMilitiaMarkers => _useMcm ? McmShowMilitiaMarkers() : _xml.UiAndNotifications.ShowMilitiaMarkers;
    internal static int TrackedSizeMinimum => _useMcm ? McmTrackedSizeMinimum() : _xml.UiAndNotifications.TrackedSizeMinimum;
    #endregion

    #region Advanced
    internal static int CooldownHours => _useMcm ? McmCooldownHours() : _xml.Advanced.CooldownHours;
    internal static int MaxItemValue => _useMcm ? McmMaxItemValue() : _xml.Advanced.MaxItemValue;
    internal static bool IgnoreSizePenalty => _useMcm ? McmIgnoreSizePenalty() : _xml.Advanced.IgnoreSizePenalty;
    #endregion

    #region Technical settings
    internal static bool LoggingEnabled => _useMcm ? McmLoggingEnabled() : _xml.TechnicalSettings.LoggingEnabled;
    #endregion

    // Where the values come from: MCM, else ModConfiguration.xml (shown by bmr.settings).
    internal static bool UsesMcm => _useMcm;

    private static XmlSerializer Serializer => _serializer ??= new XmlSerializer(typeof(ModConfiguration));

    // Called on every game start.
    internal static void Reload()
    {
        _useMcm = false;
        _xml = new ModConfiguration();

        if (IsMcmLoaded())
        {
            if (McmHasSettings())
            {
                _useMcm = true;
                McmSubscribeToChanges();
                return;
            }

            BmrLog.Warn("MCM is loaded but returned no settings for this mod. Using " + ModInfo.ConfigurationFileName + " instead.");
        }

        _xml = LoadFromFile();
    }

    // Main menu: puts the MCM dropdown items into the game language. MCM only.
    internal static void LocalizeMcmDropdowns()
    {
        if (IsMcmLoaded())
            McmLocalizeDropdowns();
    }

    // Unique random banners clash with Calradia Expanded Kingdoms when this mod loads after it.
    internal static void ForceRandomBannersOff()
    {
        _randomBannersForcedOff = true;
        if (IsMcmLoaded())
            McmForceRandomBannersOff();
    }

    private static string XmlTextOrDefault(string? value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value!;

    // A dropdown index outside its table (a broken saved value) falls back to the default.
    private static int ValidIndex(int index, int count, int fallback) => index >= 0 && index < count ? index : fallback;

    private static void RaiseChanged() => Changed?.Invoke();

    private static bool IsMcmLoaded()
    {
        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        for (int i = 0; i < assemblies.Length; i++)
        {
            if (assemblies[i].GetName().Name == McmAssemblyName) return true;
        }

        return false;
    }

    #region MCM access
    // Every method that touches MCMSettings must be NoInlining and may only be called when MCM is loaded.
    // Otherwise the game tries to load MCM's classes and crashes when MCM is not installed.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmHasSettings() => MCMSettings.Instance != null;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void McmSubscribeToChanges()
    {
        MCMSettings.OnSettingsChanged -= RaiseChanged;
        MCMSettings.OnSettingsChanged += RaiseChanged;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void McmLocalizeDropdowns() => MCMSettings.Instance?.LocalizeDropdowns();

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void McmForceRandomBannersOff()
    {
        MCMSettings? settings = MCMSettings.Instance;
        if (settings is not null)
            settings.RandomBanners = false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmHeroPoolCap() => MCMSettings.Instance?.HeroPoolCap ?? ModConfiguration.HeroPoolCapDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmMergeableSize() => MCMSettings.Instance?.MergeableSize ?? ModConfiguration.MergeableSizeDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmRandomSplitChance() => MCMSettings.Instance?.RandomSplitChance ?? ModConfiguration.RandomSplitChanceDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmCanTrain() => MCMSettings.Instance?.CanTrain ?? ModConfiguration.CanTrainDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmUseBanditOnlyTroopTrees() => MCMSettings.Instance?.UseBanditOnlyTroopTrees ?? ModConfiguration.UseBanditOnlyTroopTreesDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmDailyTrainingChance() => MCMSettings.Instance?.DailyTrainingChance ?? ModConfiguration.DailyTrainingChanceDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmXpGiftIndex() => MCMSettings.Instance?.XpGiftIndex ?? (int)ModConfiguration.XpGiftDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmUpgradeUnitsPercent() => MCMSettings.Instance?.UpgradeUnitsPercent ?? ModConfiguration.UpgradeUnitsPercentDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmMaxTrainingTier() => MCMSettings.Instance?.MaxTrainingTier ?? ModConfiguration.MaxTrainingTierDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmGrowthChance() => MCMSettings.Instance?.GrowthChance ?? ModConfiguration.GrowthChanceDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmGrowthPercent() => MCMSettings.Instance?.GrowthPercent ?? ModConfiguration.GrowthPercentDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float McmHeroXpMultiplier() => MCMSettings.Instance?.HeroXpMultiplier ?? ModConfiguration.HeroXpMultiplierDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmHeroXpPerWin() => MCMSettings.Instance?.HeroXpPerWin ?? ModConfiguration.HeroXpPerWinDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmGlobalPowerPercent() => MCMSettings.Instance?.GlobalPowerPercent ?? ModConfiguration.GlobalPowerPercentDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmGoldRewardIndex() => MCMSettings.Instance?.GoldRewardIndex ?? (int)ModConfiguration.GoldRewardDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmMaxStrongerTargetPercent() => MCMSettings.Instance?.MaxStrongerTargetPercent ?? ModConfiguration.MaxStrongerTargetPercentDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmMaxWeakerTargetPercent() => MCMSettings.Instance?.MaxWeakerTargetPercent ?? ModConfiguration.MaxWeakerTargetPercentDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmIgnoreVillagersCaravans() => MCMSettings.Instance?.IgnoreVillagersCaravans ?? ModConfiguration.IgnoreVillagersCaravansDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmExecutionHonorXp() => MCMSettings.Instance?.ExecutionHonorXp ?? ModConfiguration.ExecutionHonorXpDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmAllowPillaging() => MCMSettings.Instance?.AllowPillaging ?? ModConfiguration.AllowPillagingDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static float McmPillagingChance() => MCMSettings.Instance?.PillagingChance ?? ModConfiguration.PillagingChanceDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmRaidCapPercent() => MCMSettings.Instance?.RaidCapPercent ?? ModConfiguration.RaidCapPercentDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string McmBanditMilitiaString() => MCMSettings.Instance?.BanditMilitiaString ?? ModConfiguration.BanditMilitiaStringDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string McmLeaderlessBanditMilitiaString() => MCMSettings.Instance?.LeaderlessBanditMilitiaString ?? ModConfiguration.LeaderlessBanditMilitiaStringDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmRandomBanners() => MCMSettings.Instance?.RandomBanners ?? ModConfiguration.RandomBannersDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmFemaleSpawnChance() => MCMSettings.Instance?.FemaleSpawnChance ?? ModConfiguration.FemaleSpawnChanceDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmCheckVoiceGender() => MCMSettings.Instance?.CheckVoiceGender ?? ModConfiguration.CheckVoiceGenderDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmShowRaids() => MCMSettings.Instance?.ShowRaids ?? ModConfiguration.ShowRaidsDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmSkipConversations() => MCMSettings.Instance?.SkipConversations ?? ModConfiguration.SkipConversationsDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmRemovePrisonerMessages() => MCMSettings.Instance?.RemovePrisonerMessages ?? ModConfiguration.RemovePrisonerMessagesDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmShowMilitiaMarkers() => MCMSettings.Instance?.ShowMilitiaMarkers ?? ModConfiguration.ShowMilitiaMarkersDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmTrackedSizeMinimum() => MCMSettings.Instance?.TrackedSizeMinimum ?? ModConfiguration.TrackedSizeMinimumDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmCooldownHours() => MCMSettings.Instance?.CooldownHours ?? ModConfiguration.CooldownHoursDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int McmMaxItemValue() => MCMSettings.Instance?.MaxItemValue ?? ModConfiguration.MaxItemValueDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmIgnoreSizePenalty() => MCMSettings.Instance?.IgnoreSizePenalty ?? ModConfiguration.IgnoreSizePenaltyDefault;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool McmLoggingEnabled() => MCMSettings.Instance?.LoggingEnabled ?? ModConfiguration.LoggingEnabledDefault;
    #endregion

    #region ModConfiguration.xml
    private static ModConfiguration LoadFromFile()
    {
        string path = Path.Combine(ModInfo.ModuleRoot, ModInfo.ConfigurationFileName);
        ModConfiguration configuration = new();

        if (!File.Exists(path))
        {
            BmrLog.Warn(ModInfo.ConfigurationFileName + " was missing. Created it with the default settings.");
            TryWrite(path, configuration);
            return configuration;
        }

        try
        {
            configuration = Read(path);
            if (!HasEverySetting(path, configuration))
            {
                BmrLog.Warn(ModInfo.ConfigurationFileName + " had missing settings. Added them with their default values.");
                TryWrite(path, configuration);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or XmlException)
        {
            BmrLog.Warn(ModInfo.ConfigurationFileName + " was broken and was replaced with the default settings. Reason: " + (ex.InnerException?.Message ?? ex.Message));
            configuration = new ModConfiguration();
            TryWrite(path, configuration);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BmrLog.Warn(ModInfo.ConfigurationFileName + " could not be opened. Using the default settings. Reason: " + ex.Message);
            configuration = new ModConfiguration();
        }

        // Out-of-range values are corrected in memory only; the file keeps what the player typed.
        configuration.Clamp();
        return configuration;
    }

    private static ModConfiguration Read(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Serializer.Deserialize(stream) as ModConfiguration ?? new ModConfiguration();
    }

    private static void TryWrite(string path, ModConfiguration configuration)
    {
        try
        {
            XmlWriterSettings writerSettings = new() { Indent = true, Encoding = Encoding.UTF8 };
            using XmlWriter writer = XmlWriter.Create(path, writerSettings);
            Serializer.Serialize(writer, configuration, NoNamespaces());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            BmrLog.Warn(ModInfo.ConfigurationFileName + " could not be written. The settings are kept in memory for this session. Reason: " + ex.Message);
        }
    }

    private static bool HasEverySetting(string path, ModConfiguration configuration)
    {
        XDocument complete = new();
        using (XmlWriter writer = complete.CreateWriter())
        {
            Serializer.Serialize(writer, configuration, NoNamespaces());
        }

        XElement? fileRoot = XDocument.Load(path).Root;
        return fileRoot != null && complete.Root != null && ContainsEveryElement(fileRoot, complete.Root);
    }

    private static bool ContainsEveryElement(XElement actual, XElement expected)
    {
        foreach (XElement expectedChild in expected.Elements())
        {
            XElement? actualChild = actual.Element(expectedChild.Name);
            if (actualChild == null || !ContainsEveryElement(actualChild, expectedChild)) return false;
        }

        return true;
    }

    private static XmlSerializerNamespaces NoNamespaces()
    {
        XmlSerializerNamespaces namespaces = new();
        namespaces.Add(string.Empty, string.Empty);
        return namespaces;
    }
    #endregion
}
