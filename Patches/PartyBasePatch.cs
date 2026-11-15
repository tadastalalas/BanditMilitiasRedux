using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia party size limit raised to its current size (no speed penalty) | gate: setting:IgnoreSizePenalty | Postfix: raises the value vanilla computed
[HarmonyPatch(typeof(PartyBase), "PartySizeLimit", MethodType.Getter)]
internal class PartyBasePartySizeLimitPatch
{
    // Property-getter patch (hot): plain checks only.
    internal static void Postfix(PartyBase __instance, ref int ____cachedPartyMemberSizeLimit, ref int __result)
    {
        if (__instance is null || !ModSettings.IgnoreSizePenalty || !__instance.IsMobile || !__instance.MobileParty.IsBanditMilitiaParty()) return;
        int totalManCount = __instance.MemberRoster.TotalManCount;
        if (__result >= totalManCount) return;
        ____cachedPartyMemberSizeLimit = totalManCount;
        __result = totalManCount;
    }
}

// BMR-FEATURE: No duplicate militia hero prisoners | gate: always | Prefix: skips the vanilla add for a hero already held
[HarmonyPatch(typeof(PartyBase), nameof(PartyBase.AddPrisoner))]
internal class PartyBaseAddPrisonerPatch
{
    internal static bool Prefix(PartyBase __instance, CharacterObject element)
    {
        if (__instance is null || element is null || !element.IsBanditMilitiaCharacterObject())
            return true;

        foreach (CharacterObject prisonerHero in __instance.PrisonerHeroes)
        {
            if (prisonerHero == element)
                return false;
        }
        return true;
    }
}
