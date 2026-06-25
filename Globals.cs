using System.Collections.Generic;
using System.Diagnostics;
using BanditMilitiasRedux.Helpers;
using BanditMilitiasRedux.Managers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;
using TaleWorlds.Localization;

namespace BanditMilitiasRedux
{
    public static class Globals
    {
        internal static bool IsGlobalModStateInitialized;
        internal const float FindNearbyBanditsRadius = 20;
        internal const float MergeDistanceSq = 2.25f;
        internal const float MinSizeRatioForSplit = 0.8f;
        internal const float AvoidanceEffectToOtherBanditHeroesRadiusSq = 10000f;
        internal const float AvoidanceDecayToOtherBanditHeroesRadiusSq = 2500f;
        internal const int AvoidanceIncreaseMin = 15;
        internal const int AvoidanceIncreaseMax = 35;
        internal const int AvoidanceDecreaseAmount = 10;
        internal const float StuckDistanceThresholdSq = 225f;
        internal const int StuckHourLimit = 12;
        internal const float EscapeMinDistanceSq = 900f;
        internal const float ArrivedAtHideoutEpsilonSq = 0.1f;
        internal const int SettlementFindRange = 200;

        internal static MCMSettings Settings => MCMSettings.Instance;

        internal static float CalculatedMaxPartySize => PowerCalculationManager.CalculatedMaxPartySize;
        internal static float CalculatedGlobalPowerLimit => PowerCalculationManager.CalculatedGlobalPowerLimit;
        internal static float GlobalMilitiaPower => PowerCalculationManager.GlobalMilitiaPower;
        internal static float MilitiaPowerPercent => PowerCalculationManager.MilitiaPowerPercent;
        internal static float MilitiaPartyAveragePower => PowerCalculationManager.MilitiaPartyAveragePower;

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

        internal static List<Hero> AllAliveBanditMilitiaHeroes = [];
        internal static List<CharacterObject> HeroTemplates = [];
        internal static int HeroesCapPerClan => Settings.HeroPoolCap;
        internal static int RaidCap;
        
        internal static readonly List<MobileParty> NearbyBanditsBuffer = new(8);
        internal static readonly Dictionary<MobileParty, int> MountedCountBuffer = new(8);

        internal static Dictionary<CultureObject, List<CharacterObject>> Recruits = [];
        internal static List<CharacterObject> BasicRanged = [];
        internal static List<CharacterObject> BasicInfantry = [];
        internal static List<CharacterObject> BasicCavalry = [];
        internal static CharacterObject? Giant;

        internal static Dictionary<MobileParty, BannerImageIdentifierVM> PartyImageMap = [];
        internal static readonly List<Banner> Banners = [];

        internal static List<Settlement>? Hideouts;
        internal static List<Settlement>? Villages;
        internal static Clan? Looters;
        internal static HashSet<int>? LordConversationTokens;

        internal static readonly Dictionary<MobileParty, (Vec2 LastPos, int HourCount)> StuckTracker = [];

        internal static CultureObject? BlackFlag;

        public static readonly Dictionary<TextObject, int> DifficultyXpMap = new()
        {
            { new TextObject("{=BMXpOff}Off"), 0 },
            { new TextObject("{=BMXpNormal}Normal"), 300 },
            { new TextObject("{=BMXpHard}Hard"), 600 },
            { new TextObject("{=BMXpHardest}Hardest"), 900 },
        };

        internal static readonly int[] DifficultyXpValues = [0, 300, 600, 900];

        internal static readonly Dictionary<TextObject, int> GoldMap = new()
        {
            { new TextObject("{=BMGoldLow}Low"), 250 },
            { new TextObject("{=BMGoldNormal}Normal"), 500 },
            { new TextObject("{=BMGoldRich}Rich"), 900 },
            { new TextObject("{=BMGoldRichest}Richest"), 2000 },
        };

        internal static readonly int[] GoldValues = [250, 500, 900, 2000];

        // Notoriety (Item 2) — tier N is reached at index N-1
        internal static readonly int[] NotorietyWinThresholds = [3, 6, 9, 12, 15, 18, 21, 24, 27, 30];
        internal static readonly int[] NotorietyDayThresholds = [21, 42, 63, 84, 105, 126, 147, 168, 189, 210];
        internal static readonly string[] NotorietyEpithets =
        [
            "the Brash", "the Bold", "the Cruel", "the Dreaded", "the Bloody",
            "the Merciless", "the Butcher", "the Scourge", "the Terror", "the Nightmare",
        ];

        internal const float NotorietyFearMentionThreshold = 25f;

        // Bounty (Item 3)
        internal const int BountyRenownMin = 1;
        internal const int BountyRenownMax = 5;
        internal const int BountyGoldMin = 2000;
        internal const int BountyGoldMax = 10000;
        internal const float BountyStrengthForMaxReward = 300f;

        public static void ClearGlobals()
        {
            PowerCalculationManager.Reset();
            EquipmentPool.Reset();
            Banners.Clear();
            StuckTracker.Clear();
            PartyImageMap = [];
            Recruits = [];
            RaidCap = 0;
            HeroTemplates = [];
            Hideouts = [];
            Villages = [];
            LordConversationTokens = [];
        }
    }
}