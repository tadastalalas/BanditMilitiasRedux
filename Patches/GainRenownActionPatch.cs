using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia heroes gain no renown | gate: always | Prefix: skips vanilla for militia heroes
[HarmonyPatch(typeof(GainRenownAction), "ApplyInternal")]
internal class GainRenownActionPatch
{
    internal static bool Prefix(Hero hero) => hero is null || !hero.IsBanditMilitiaHero();
}
