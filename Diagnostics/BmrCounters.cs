using TaleWorlds.CampaignSystem;

namespace BanditMilitiasRedux.Diagnostics;

// BMR-FEATURE: Militia event counters since the save was loaded, shown by bmr.stats | gate: always
// Memory only: never saved, reset on game end.
internal static class BmrCounters
{
    internal static CampaignTime? CountingSince;
    internal static int MilitiasFormed;
    internal static int MilitiasMerged;
    internal static int MilitiasSplit;
    internal static int MilitiasDefeated;
    internal static int HeroesCreated;
    internal static int HeroesReused;
    internal static int HeroesCaptured;
    internal static int HeroesFreed;
    internal static int HeroesKilled;
    internal static int StuckHeroesReclaimedOnLoad;

    // Called once the campaign map is ready after a new game or a load.
    internal static void Start() => CountingSince ??= CampaignTime.Now;

    internal static void Reset()
    {
        CountingSince = null;
        MilitiasFormed = 0;
        MilitiasMerged = 0;
        MilitiasSplit = 0;
        MilitiasDefeated = 0;
        HeroesCreated = 0;
        HeroesReused = 0;
        HeroesCaptured = 0;
        HeroesFreed = 0;
        HeroesKilled = 0;
        StuckHeroesReclaimedOnLoad = 0;
    }
}