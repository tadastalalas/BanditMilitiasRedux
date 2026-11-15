using System;
using System.Reflection;
using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
#if LOWER_THAN_1_5
using SandBox.ViewModelCollection.Map.Tracker;
#endif

namespace BanditMilitiasRedux.Managers;

// BMR-FEATURE: Militia map markers (tracker banners) | gate: setting:ShowMilitiaMarkers
internal static class MilitiaTrackerManager
{
#if LOWER_THAN_1_5
    private static readonly MethodInfo? CanAddMethod = AccessTools.Method(typeof(MapTrackerProvider), "CanAddMobileParty");
    private static readonly MethodInfo? AddMethod = AccessTools.Method(typeof(MapTrackerProvider), "AddIfEligible", [typeof(MobileParty)]);
    private static readonly MethodInfo? RemoveMethod = AccessTools.Method(typeof(MapTrackerProvider), "RemoveIfExists");

    private static readonly Func<MapTrackerProvider, MobileParty, bool>? CanAdd =
        CanAddMethod is null ? null : AccessTools.MethodDelegate<Func<MapTrackerProvider, MobileParty, bool>>(CanAddMethod);
    private static readonly Action<MapTrackerProvider, MobileParty>? AddIfEligible =
        AddMethod is null ? null : AccessTools.MethodDelegate<Action<MapTrackerProvider, MobileParty>>(AddMethod);
    private static readonly Action<MapTrackerProvider, ITrackableCampaignObject>? RemoveIfExists =
        RemoveMethod is null ? null : AccessTools.MethodDelegate<Action<MapTrackerProvider, ITrackableCampaignObject>>(RemoveMethod);

    private static MapTrackerProvider? _provider;

    internal static void SetProvider(MapTrackerProvider provider) => _provider = provider;

    internal static void Reset() => _provider = null;

    internal static void Refresh(MobileParty? party)
    {
        if (_provider is null || party is not { IsActive: true } || CanAdd is null || AddIfEligible is null || RemoveIfExists is null)
            return;

        if (CanAdd(_provider, party))
            AddIfEligible(_provider, party);
        else
            RemoveIfExists(_provider, party);
    }
#else
    // v1.5.7: MapTrackerProvider is gone; MapTrackerManager.Refresh (internal) adds or removes the tracker.
    private static readonly MethodInfo? RefreshMethod = AccessTools.Method(typeof(MapTrackerManager), "Refresh", [typeof(ITrackableCampaignObject)]);

    private static readonly Action<MapTrackerManager, ITrackableCampaignObject>? RefreshTracker =
        RefreshMethod is null ? null : AccessTools.MethodDelegate<Action<MapTrackerManager, ITrackableCampaignObject>>(RefreshMethod);

    internal static void Reset() { }

    internal static void Refresh(MobileParty? party)
    {
        MapTrackerManager? manager = Campaign.Current?.MapTrackerManager;
        if (manager is null || party is not { IsActive: true } || RefreshTracker is null)
            return;

        RefreshTracker(manager, party);
    }
#endif

    internal static bool ShouldTrack(MobileParty party) =>
        ModSettings.ShowMilitiaMarkers && party.IsBanditMilitiaParty() && party.MemberRoster.TotalManCount >= ModSettings.TrackedSizeMinimum;
}
