using System.Collections.Generic;
using System.Diagnostics;
using SandBox.ViewModelCollection.Map;
using SandBox.ViewModelCollection.Map.Tracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;

namespace BanditMilitias
{
    public static class Globals
    {
        internal const float FindRadius = 20;
        internal const float MinDistanceFromHideout = 8;
        internal const float MergeDistanceSq = 2.25f;
        internal const float SpawnHideoutMinPlayerDistanceSq = 10000f;
        internal const float AvoidanceEffectRadiusSq = 10000f;
        internal const int AvoidanceIncreaseMin = 15;
        internal const int AvoidanceIncreaseMax = 35;
        internal const float StuckDistanceThresholdSq = 225f;
        internal const int StuckHourLimit = 12;
        internal const float EscapeMinDistanceSq = 900f;

        internal const int AdjustRadiusSq = 2500;
        internal const int SettlementFindRange = 200;
        internal const int SpawnLoopSafetyLimit = 100;

        internal static Settings Settings;
        internal static readonly Stopwatch T = new();

        internal static float CalculatedMaxPartySize => PowerCalculationService.CalculatedMaxPartySize;
        internal static float CalculatedGlobalPowerLimit => PowerCalculationService.CalculatedGlobalPowerLimit;
        internal static float GlobalMilitiaPower => PowerCalculationService.GlobalMilitiaPower;
        internal static float MilitiaPowerPercent => PowerCalculationService.MilitiaPowerPercent;
        internal static float MilitiaPartyAveragePower => PowerCalculationService.MilitiaPartyAveragePower;

        internal static Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes  => EquipmentPool.ItemTypes;
        internal static List<EquipmentElement> EquipmentItems => EquipmentPool.EquipmentItems;
        internal static List<EquipmentElement> EquipmentItemsNoBow => EquipmentPool.EquipmentItemsNoBow;
        internal static List<Equipment> BanditEquipment => EquipmentPool.BanditEquipment;
        internal static List<ItemObject> Arrows => EquipmentPool.Arrows;
        internal static List<ItemObject> Bolts => EquipmentPool.Bolts;
        internal static List<ItemObject> Mounts => EquipmentPool.Mounts;
        internal static List<ItemObject> Saddles => EquipmentPool.Saddles;
        internal static List<ItemObject> CamelSaddles => EquipmentPool.CamelSaddles;
        internal static List<ItemObject> NonCamelSaddles => EquipmentPool.NonCamelSaddles;

        internal static IReadOnlyList<ModBanditMilitiaPartyComponent> AllBMs => PowerCalculationService.GetCachedBMs();
        internal static List<Hero> Heroes = new();
        internal static List<CharacterObject> HeroTemplates = new();
        internal static int RaidCap;

        internal static Dictionary<CultureObject, List<CharacterObject>> Recruits = new();
        internal static List<CharacterObject> BasicRanged = new();
        internal static List<CharacterObject> BasicInfantry = new();
        internal static List<CharacterObject> BasicCavalry = new();
        internal static CharacterObject Giant;

        internal static Dictionary<MobileParty, BannerImageIdentifierVM> PartyImageMap = new();
        internal static readonly List<Banner> Banners = new();

        internal static List<Settlement> Hideouts;
        internal static List<Settlement> Villages;
        internal static Clan Looters;
        internal static Clan Wights;
        internal static HashSet<int> LordConversationTokens;

        internal static readonly Dictionary<MobileParty, (Vec2 LastPos, int HourCount)> StuckTracker = new();

        internal static CultureObject BlackFlag;

        internal static Dictionary<TextObject, int> DifficultyXpMap = new()
        {
            { new TextObject("{=BMXpOff}Off"),        0 },
            { new TextObject("{=BMXpNormal}Normal"), 300 },
            { new TextObject("{=BMXpHard}Hard"),     600 },
            { new TextObject("{=BMXpHardest}Hardest"), 900 },
        };

        internal static readonly int[] DifficultyXpValues = { 0, 300, 600, 900 };

        internal static Dictionary<TextObject, int> GoldMap = new()
        {
            { new TextObject("{=BMGoldLow}Low"),         250 },
            { new TextObject("{=BMGoldNormal}Normal"),   500 },
            { new TextObject("{=BMGoldRich}Rich"),       900 },
            { new TextObject("{=BMGoldRichest}Richest"), 2000 },
        };

        internal static readonly int[] GoldValues = { 250, 500, 900, 2000 };

        public static void ClearGlobals()
        {
            PowerCalculationService.Reset();
            EquipmentPool.Reset();
            Banners.Clear();
            StuckTracker.Clear();
            PartyImageMap = new();
            Recruits = new();
            RaidCap = 0;
            HeroTemplates = new();
            Hideouts = new();
            Villages = new();
        }
    }
}