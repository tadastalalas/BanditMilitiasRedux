using System.Collections.Generic;
using BanditMilitiasRedux.Managers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Core.ViewModelCollection.ImageIdentifiers;
using TaleWorlds.Library;

namespace BanditMilitiasRedux.Core;

// Runtime state and caches shared across the mod. Tuning numbers live in Balance, settings in ModSettings.
internal static class Globals
{
    internal static bool IsGlobalModStateInitialized;

    internal static float CalculatedMaxPartySize => PowerCalculationManager.CalculatedMaxPartySize;
    internal static float CalculatedGlobalPowerLimit => PowerCalculationManager.CalculatedGlobalPowerLimit;
    internal static float GlobalMilitiaPower => PowerCalculationManager.GlobalMilitiaPower;
    internal static float MilitiaPowerPercent => PowerCalculationManager.MilitiaPowerPercent;
    internal static float MilitiaPartyAveragePower => PowerCalculationManager.MilitiaPartyAveragePower;

    internal static List<ItemObject> Mounts => EquipmentPool.Mounts;
    internal static List<ItemObject> CamelSaddles => EquipmentPool.CamelSaddles;
    internal static List<ItemObject> NonCamelSaddles => EquipmentPool.NonCamelSaddles;
    internal static readonly List<MobileParty> DeferredTrashParties = [];

    internal static List<Hero> AllAliveBanditMilitiaHeroes = [];
    internal static List<CharacterObject> HeroTemplates = [];
    internal static int HeroesCapPerClan => ModSettings.HeroPoolCap;
    internal static int RaidCap;

    internal static readonly List<MobileParty> NearbyBanditsBuffer = new(Balance.NearbyBuffersCapacity);
    internal static readonly Dictionary<MobileParty, int> MountedCountBuffer = new(Balance.NearbyBuffersCapacity);

    internal static Dictionary<CultureObject, List<CharacterObject>> Recruits = [];
    internal static Dictionary<int, Dictionary<FormationClass, List<CharacterObject>>> BanditTroopsByTier = [];

    internal static Dictionary<MobileParty, BannerImageIdentifierVM> PartyImageMap = [];
    internal static readonly List<Banner> Banners = [];

    internal static List<Settlement>? Hideouts;
    internal static List<Settlement>? Villages;
    internal static Clan? Looters;
    internal static HashSet<int>? LordConversationTokens;
    internal static Hero? SuppressedExecutionVictim;
    internal static bool IsNukeInquiryOpen;

    internal static readonly Dictionary<MobileParty, (Vec2 LastPos, int HourCount, int Rescues)> StuckTracker = [];
    internal static readonly Dictionary<MobileParty, CampaignTime> PatrolRepickTimes = [];
    internal static readonly Dictionary<MobileParty, CampaignTime> IdleRetryTimes = [];
    internal static readonly List<Settlement> RaidTargetRejectBuffer = new(Balance.RaidTargetAttempts);

    internal static CultureObject? BlackFlag;

    internal static void ClearGlobals()
    {
        PowerCalculationManager.Reset();
        EquipmentPool.Reset();
        Banners.Clear();
        StuckTracker.Clear();
        PatrolRepickTimes.Clear();
        IdleRetryTimes.Clear();
        RaidTargetRejectBuffer.Clear();
        DeferredTrashParties.Clear();
        PartyImageMap = [];
        Recruits = [];
        RaidCap = 0;
        IsNukeInquiryOpen = false;
        HeroTemplates = [];
        Hideouts = [];
        Villages = [];
        LordConversationTokens = [];
        BanditTroopsByTier = [];
    }
}
