using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CampaignBehaviors;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: No ransom offers for militia heroes; vanilla null-payer guard for leaderless clans | gate: always | Prefix: skips vanilla for militia heroes and leaderless clans
[HarmonyPatch(typeof(RansomOfferCampaignBehavior), "ConsiderRansomPrisoner")]
internal class RansomOfferCampaignBehaviorPatch
{
    internal static bool Prefix(Hero hero)
    {
        if (hero is null || hero.IsBanditMilitiaHero())
            return false;

        // Vanilla NRE guard: leaderless clans (e.g. bandit clans) produce a null ransom payer.
        return hero.Clan?.Leader is not null;
    }
}
