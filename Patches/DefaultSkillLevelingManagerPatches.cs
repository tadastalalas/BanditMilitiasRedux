using HarmonyLib;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;

namespace BanditMilitias.Patches
{
    public static class DefaultSkillLevelingManagerPatches
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<SubModule>();

        // Vanilla doesn't null-check winnerPartyLeader before accessing .HeroDeveloper.
        // BM parties can lose their hero leader mid-battle, leaving LeaderHero null
        // when loot resolution calls this method. Skip XP award if there's no leader.
        [HarmonyPatch(typeof(DefaultSkillLevelingManager), "OnAIPartyLootCasualties")]
        public static class OnAIPartyLootCasualtiesPatch
        {
            public static bool Prefix(Hero winnerPartyLeader)
            {
                if (winnerPartyLeader is null)
                {
                    Logger.LogDebug("OnAIPartyLootCasualties skipped: winnerPartyLeader is null (leader likely died mid-battle)");
                    return false;
                }

                return true;
            }
        }
    }
}