using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Relations with militia heroes use the heroes themselves (vanilla returns null for them) | gate: always | Postfix: fills the out values vanilla left null
[HarmonyPatch(typeof(DefaultDiplomacyModel), nameof(DefaultDiplomacyModel.GetHeroesForEffectiveRelation))]
internal class DefaultDiplomacyModelPatch
{
    internal static void Postfix(Hero hero1, Hero hero2, ref Hero effectiveHero1, ref Hero effectiveHero2)
    {
        if (hero1?.IsBanditMilitiaHero() == true || hero2?.IsBanditMilitiaHero() == true)
        {
            effectiveHero1 ??= hero1!;
            effectiveHero2 ??= hero2!;
        }
    }
}
