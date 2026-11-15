using BanditMilitiasRedux.Behaviors;
using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: A militia hero's home settlement stays his birth settlement | gate: always | Postfix: overwrites what vanilla picked
[HarmonyPatch(typeof(Hero), "UpdateHomeSettlement")]
internal class HeroUpdateHomeSettlementPatch
{
    internal static void Postfix(Hero __instance, ref Settlement ____homeSettlement, Settlement ____bornSettlement)
    {
        if (__instance is null || !__instance.IsBanditMilitiaHero())
            return;

        ____homeSettlement = ____bornSettlement;
    }
}

// BMR-FEATURE: A militia leader who would turn fugitive waits to be reused instead | gate: always | Prefix: skips the vanilla state change
[HarmonyPatch(typeof(Hero), nameof(Hero.ChangeState))]
internal class HeroChangeStatePatch
{
    internal static bool Prefix(Hero __instance, Hero.CharacterStates newState)
    {
        if (newState != Hero.CharacterStates.Fugitive)
            return true;

        if (__instance is not { IsAlive: true, IsPrisoner: false }
            || !__instance.IsBanditMilitiaHero()
            || __instance.PartyBelongedTo?.IsBanditMilitiaParty() != true)
            return true;

        ReusableHeroesBehavior.AddHeroToTheWaitingDictionary(__instance);
        return false;
    }
}
