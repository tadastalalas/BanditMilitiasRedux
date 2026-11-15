using System.Collections.Generic;
using BanditMilitiasRedux.Behaviors;
using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia party component flags kept (a militia always counts as bandit) | gate: always | Postfix: re-sets the flag vanilla just recalculated
[HarmonyPatch(typeof(MobileParty), "UpdatePartyComponentFlags")]
internal class MobilePartyUpdatePartyComponentFlagsPatch
{
    internal static void Postfix(MobileParty __instance)
    {
        if (__instance is not null && __instance.IsBanditMilitiaParty() && !__instance.IsBandit)
            Helper.IsBandit(__instance) = true;
    }
}

// BMR-FEATURE: Militias are never bandit boss parties | gate: always | Prefix: skips the vanilla getter for militias
[HarmonyPatch(typeof(MobileParty), "IsBanditBossParty", MethodType.Getter)]
internal class MobilePartyIsBanditBossPartyPatch
{
    // Property-getter patch (hot): plain checks only.
    internal static bool Prefix(MobileParty __instance, ref bool __result)
    {
        if (__instance is null || !__instance.IsBanditMilitiaParty())
            return true;

        __result = false;
        return false;
    }
}

// BMR-FEATURE: Militias are land-only (no naval navigation) | gate: always | Prefix: skips the vanilla getter for militias
[HarmonyPatch(typeof(MobileParty), nameof(MobileParty.HasNavalNavigationCapability), MethodType.Getter)]
internal class MobilePartyHasNavalNavigationCapabilityPatch
{
    // Property-getter patch (hot): plain checks only.
    internal static bool Prefix(MobileParty __instance, ref bool __result)
    {
        if (__instance is null || !__instance.IsBanditMilitiaParty())
            return true;

        __result = false;
        return false;
    }
}

// BMR-FEATURE: MapFaction null guard for militias (owner clan, then home owner, then bandit faction) | gate: always | Postfix: only fills a null result
[HarmonyPatch(typeof(MobileParty), "MapFaction", MethodType.Getter)]
internal class MobilePartyMapFactionPatch
{
    // Property-getter patch (hot): the fallback below only runs when vanilla returned null for a militia.
    internal static void Postfix(MobileParty __instance, ref IFaction __result)
    {
        if (__result is not null || __instance is null || !__instance.IsBanditMilitiaParty())
            return;

        __result = __instance.GetBanditMilitiaParty()?.PartyOwner?.Clan?.MapFaction
                   ?? __instance.HomeSettlement?.OwnerClan?.MapFaction
                   ?? FirstBanditFaction()!;
    }

    private static Clan? FirstBanditFaction()
    {
        foreach (Clan clan in Clan.BanditFactions)
            return clan;
        return null;
    }
}

// BMR-FEATURE: A militia leader removed from his party waits to be reused | gate: always | Prefix: the leader must be read before vanilla removes him
[HarmonyPatch(typeof(MobileParty), nameof(MobileParty.RemovePartyLeader))]
internal class MobilePartyRemovePartyLeaderPatch
{
    internal static void Prefix(MobileParty __instance)
    {
        if (__instance?.IsBanditMilitiaParty() != true)
            return;

        if (__instance.LeaderHero is { IsAlive: true, IsPrisoner: false } leader && leader.IsBanditMilitiaHero() && leader.PartyBelongedTo == __instance)
            ReusableHeroesBehavior.AddHeroToTheWaitingDictionary(leader);
    }
}
