using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia heroes never become fugitives | gate: always | Prefix: skips vanilla for militia heroes
[HarmonyPatch(typeof(MakeHeroFugitiveAction), "ApplyInternal")]
internal class MakeHeroFugitiveActionPatch
{
    internal static bool Prefix(Hero fugitive) => fugitive is null || !fugitive.IsBanditMilitiaHero();
}
