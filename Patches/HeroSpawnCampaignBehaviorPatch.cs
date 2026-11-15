using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Vanilla hero spawning skips militia heroes | gate: always | Prefix: skips vanilla for militia heroes
[HarmonyPatch(typeof(HeroSpawnCampaignBehavior), "OnHeroDailyTick")]
internal class HeroSpawnCampaignBehaviorPatch
{
    // Daily tick path: plain checks only.
    internal static bool Prefix(Hero hero) => hero?.IsBanditMilitiaHero() != true;
}
