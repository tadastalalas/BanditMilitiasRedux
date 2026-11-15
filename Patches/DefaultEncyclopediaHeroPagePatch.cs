using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encyclopedia.Pages;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia heroes are visible in the encyclopedia | gate: always | Postfix: only turns a vanilla false into true
[HarmonyPatch(typeof(DefaultEncyclopediaHeroPage), nameof(DefaultEncyclopediaHeroPage.IsValidEncyclopediaItem))]
internal class DefaultEncyclopediaHeroPagePatch
{
    internal static void Postfix(object o, ref bool __result)
    {
        if (__result)
            return;

        if (o is Hero hero
            && hero.IsBanditMilitiaHero()
            && hero is { IsTemplate: false, IsReady: true }
            && !hero.CharacterObject.HiddenInEncyclopedia
            && !hero.HiddenInEncyclopedia)
        {
            __result = true;
        }
    }
}
