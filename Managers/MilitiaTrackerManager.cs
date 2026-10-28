using System;
using System.Reflection;
using BanditMilitiasRedux.Helpers;
using HarmonyLib;
using SandBox.ViewModelCollection.Map.Tracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using static BanditMilitiasRedux.Globals;

namespace BanditMilitiasRedux.Managers;

// BMR-FEATURE: Militia map markers (tracker banners) | gate: setting:ShowMilitiaMarkers
internal static class MilitiaTrackerManager
{
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

    internal static bool ShouldTrack(MobileParty party) =>
        Settings.ShowMilitiaMarkers && party.IsBanditMilitiaParty() && party.MemberRoster.TotalManCount >= Settings.TrackedSizeMinimum;

    internal static void Refresh(MobileParty? party)
    {
        if (_provider is null || party is not { IsActive: true } || CanAdd is null || AddIfEligible is null || RemoveIfExists is null)
            return;

        if (CanAdd(_provider, party))
            AddIfEligible(_provider, party);
        else
            RemoveIfExists(_provider, party);
    }
}