using BanditMilitiasRedux.Behaviors;
using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ViewModelCollection.Encyclopedia.Pages;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia leader encyclopedia text (notoriety, defeats, most feared hunter) | gate: always | Postfix: replaces the text vanilla built
[HarmonyPatch(typeof(EncyclopediaHeroPageVM), "UpdateInformationText")]
internal class EncyclopediaHeroPageVMPatch
{
    private static readonly AccessTools.FieldRef<EncyclopediaHeroPageVM, Hero> HeroField =
        AccessTools.FieldRefAccess<EncyclopediaHeroPageVM, Hero>("_hero");

    internal static void Postfix(EncyclopediaHeroPageVM __instance)
    {
        if (__instance is null)
            return;

        Hero hero = HeroField(__instance);
        if (hero is null || !hero.IsBanditMilitiaHero())
            return;

        __instance.InformationText = NotorietyBehavior.BuildEncyclopediaText(hero).ToString();
    }
}
