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
        // ── Constants ────────────────────────────────────────────────────────────

        internal const float MergeDistance = 1.5f;
        internal const float FindRadius = 20;
        internal const float MinDistanceFromHideout = 8;
        internal const float AvoidanceEffectRadius = 100;

        // ── Settings & Timers ────────────────────────────────────────────────────

        internal static Settings Settings;
        internal static readonly Stopwatch T = new();

        // ── Power & Size Calculations (owned by PowerCalculationService) ─────────
        // These forwarding properties keep every existing call-site compiling
        // without modification while the real state lives in one place.

        internal static float CalculatedMaxPartySize     => PowerCalculationService.CalculatedMaxPartySize;
        internal static float CalculatedGlobalPowerLimit => PowerCalculationService.CalculatedGlobalPowerLimit;
        internal static float GlobalMilitiaPower         => PowerCalculationService.GlobalMilitiaPower;
        internal static float MilitiaPowerPercent        => PowerCalculationService.MilitiaPowerPercent;
        internal static float MilitiaPartyAveragePower   => PowerCalculationService.MilitiaPartyAveragePower;

        // ── Equipment & Items (owned by EquipmentPool) ───────────────────────────

        internal static Dictionary<ItemObject.ItemTypeEnum, List<ItemObject>> ItemTypes    => EquipmentPool.ItemTypes;
        internal static List<EquipmentElement> EquipmentItems                              => EquipmentPool.EquipmentItems;
        internal static List<EquipmentElement> EquipmentItemsNoBow                         => EquipmentPool.EquipmentItemsNoBow;
        internal static List<Equipment>        BanditEquipment                             => EquipmentPool.BanditEquipment;
        internal static List<ItemObject>       Arrows                                      => EquipmentPool.Arrows;
        internal static List<ItemObject>       Bolts                                       => EquipmentPool.Bolts;
        internal static List<ItemObject>       Mounts                                      => EquipmentPool.Mounts;
        internal static List<ItemObject>       Saddles                                     => EquipmentPool.Saddles;
        internal static List<ItemObject>       CamelSaddles                                => EquipmentPool.CamelSaddles;
        internal static List<ItemObject>       NonCamelSaddles                             => EquipmentPool.NonCamelSaddles;

        // ── Party & Hero Tracking ────────────────────────────────────────────────

        internal static List<ModBanditMilitiaPartyComponent> AllBMs => PowerCalculationService.GetCachedBMs();
        internal static List<Hero>            Heroes       = new();
        internal static List<CharacterObject> HeroTemplates = new();
        internal static int RaidCap;

        // ── Character Pools ──────────────────────────────────────────────────────

        internal static Dictionary<CultureObject, List<CharacterObject>> Recruits = new();
        internal static List<CharacterObject> BasicRanged  = new();
        internal static List<CharacterObject> BasicInfantry = new();
        internal static List<CharacterObject> BasicCavalry  = new();
        internal static CharacterObject Giant;

        // ── Map & UI ─────────────────────────────────────────────────────────────

        internal static Dictionary<MobileParty, BannerImageIdentifierVM> PartyImageMap = new();
        internal static readonly List<Banner> Banners = new();
        internal static MapTrackerProvider MapTrackerProvider;
        internal static object TrackerContainer;

        // ── World Objects ────────────────────────────────────────────────────────

        internal static List<Settlement> Hideouts;
        internal static Clan Looters;
        internal static Clan Wights; // ROT
        internal static HashSet<int> LordConversationTokens;

        // ── Stuck Detection (transient – not saved, resets on load) ──────────────

        internal static readonly Dictionary<MobileParty, (Vec2 LastPos, int HourCount)> StuckTracker = new();

        // ── Compatibility ────────────────────────────────────────────────────────

        internal static CultureObject BlackFlag; // ArmsDealer compatibility

        // ── Difficulty / Gold Maps ────────────────────────────────────────────────

        internal static Dictionary<TextObject, int> DifficultyXpMap = new()
        {
            { new TextObject("{=BMXpOff}Off"),        0 },
            { new TextObject("{=BMXpNormal}Normal"), 300 },
            { new TextObject("{=BMXpHard}Hard"),     600 },
            { new TextObject("{=BMXpHardest}Hardest"), 900 },
        };

        internal static Dictionary<TextObject, int> GoldMap = new()
        {
            { new TextObject("{=BMGoldLow}Low"),         250 },
            { new TextObject("{=BMGoldNormal}Normal"),   500 },
            { new TextObject("{=BMGoldRich}Rich"),       900 },
            { new TextObject("{=BMGoldRichest}Richest"), 2000 },
        };

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public static void ClearGlobals()
        {
            PartyImageMap    = new();
            Recruits         = new();
            Banners.Clear();
            RaidCap          = 0;
            HeroTemplates    = new();
            Hideouts         = new();
            StuckTracker.Clear();

            PowerCalculationService.Reset();
            EquipmentPool.Reset();
        }
    }
}
