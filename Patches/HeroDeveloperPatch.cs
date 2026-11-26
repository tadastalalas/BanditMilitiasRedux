using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Core;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia hero XP multiplier, skill XP the game gives to militia heroes is multiplied | gate: setting:CanTrain, setting:HeroXpMultiplier | Prefix: scales rawXp before vanilla turns it into skill XP and level XP
[HarmonyPatch(typeof(HeroDeveloper), nameof(HeroDeveloper.AddSkillXp), new[] { typeof(SkillObject), typeof(float), typeof(bool), typeof(bool) })]
internal class HeroDeveloperAddSkillXpPatch
{
    // Hot path: cheapest checks first.
    internal static void Prefix(HeroDeveloper __instance, ref float rawXp)
    {
        if (HeroProgressionManager.IsGivingUnscaledXp || rawXp <= 0f)
            return;

        Hero? hero = __instance?.Hero;
        if (hero?.Clan?.IsBanditFaction != true || !ModSettings.CanTrain)
            return;

        float multiplier = ModSettings.HeroXpMultiplier;
        if (multiplier <= 1f || !hero.IsBanditMilitiaHero())
            return;

        rawXp *= multiplier;
    }
}
