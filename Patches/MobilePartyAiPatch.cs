using System;
using BanditMilitiasRedux.Compat;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Data;
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Engage filter, militias skip villagers/caravans, avoided heroes and too strong / too weak targets | gate: setting:IgnoreVillagersCaravans, setting:MaxStrongerTargetPercent, setting:MaxWeakerTargetPercent | Prefix: cancels the engage before vanilla sets it
[HarmonyPatch(typeof(MobilePartyAi), "SetAiBehavior")]
internal class MobilePartyAiSetAiBehaviorPatch
{
    private static readonly AccessTools.FieldRef<MobilePartyAi, MobileParty> MobilePartyFieldRef =
        AccessTools.FieldRefAccess<MobilePartyAi, MobileParty>("_mobileParty");

    internal static bool Prefix(AiBehavior newAiBehavior, IInteractablePoint interactablePoint, MobilePartyAi __instance)
    {
        if (newAiBehavior != AiBehavior.EngageParty || __instance is null)
            return true;

        MobileParty attacker = MobilePartyFieldRef(__instance);
        BanditMilitiaPartyComponent? militia = attacker?.GetBanditMilitiaParty();

        if (militia is null || SaSCompat.IsReservedBySaS(attacker))
            return true;

        PartyBase? targetPartyBase = interactablePoint as PartyBase;
        MobileParty? targetParty = targetPartyBase?.MobileParty;

        if (targetParty is null || !targetPartyBase!.IsMobile)
            return true;

        if (!FactionManager.IsAtWarAgainstFaction(attacker!.MapFaction, targetParty.MapFaction))
            return true;

        if (ModSettings.IgnoreVillagersCaravans
            && (targetParty.IsCaravan || targetParty.IsVillager))
            return false;

        if (targetParty.LeaderHero is not null
            && militia.Avoidance is not null && militia.Avoidance.TryGetValue(targetParty.LeaderHero, out float heroAvoidance)
            && MBRandom.RandomFloat * Balance.PercentDivisor < heroAvoidance)
            return false;

        float attackerStrength = attacker.GetTotalLandStrengthWithFollowers();
        float targetStrength = targetParty.GetTotalLandStrengthWithFollowers();

        if (attackerStrength <= 0f)
            return true;

        float stronger = Math.Max(attackerStrength, targetStrength);
        float weaker = Math.Min(attackerStrength, targetStrength);
        float deltaPercent = (stronger - weaker) / attackerStrength * Balance.PercentDivisor;

        int cap = targetStrength > attackerStrength
            ? ModSettings.MaxStrongerTargetPercent
            : ModSettings.MaxWeakerTargetPercent;

        return cap >= Balance.FullPercent || deltaPercent <= cap;
    }
}

// BMR-FEATURE: Idle militias think every AI tick (patrol near home / raid) | gate: always | Prefix: the militia must decide before vanilla's AI tick acts on its behavior
[HarmonyPatch(typeof(MobilePartyAi), "TickInternal")]
internal class MobilePartyAiTickInternalPatch
{
    private static readonly AccessTools.FieldRef<MobilePartyAi, MobileParty> MobilePartyFieldRef =
        AccessTools.FieldRefAccess<MobilePartyAi, MobileParty>("_mobileParty");

    // Hot path: plain checks only.
    internal static void Prefix(MobilePartyAi __instance)
    {
        if (__instance is null)
            return;

        MobileParty mobileParty = MobilePartyFieldRef(__instance);
        if (!mobileParty.IsBanditMilitiaParty())
            return;

        if (mobileParty is { DefaultBehavior: AiBehavior.Hold, TargetSettlement: null } && !SaSCompat.IsReservedBySaS(mobileParty))
            BanditMilitiaManager.Think(mobileParty);
    }
}
