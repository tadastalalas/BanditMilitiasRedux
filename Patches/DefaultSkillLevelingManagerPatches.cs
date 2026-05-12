using HarmonyLib;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitias.Patches
{
    public static class DefaultSkillLevelingManagerPatches
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<SubModule>();

        [HarmonyPatch(typeof(DefaultSkillLevelingManager), "OnAIPartyLootCasualties")]
        public static class OnAIPartyLootCasualtiesPatch
        {
            public static bool Prefix(Hero winnerPartyLeader, PartyBase defeatedParty)
            {
                if (winnerPartyLeader is null || winnerPartyLeader.IsDead)
                {
                    Logger.LogDebug("OnAIPartyLootCasualties skipped: winnerPartyLeader is null or dead (leader likely died mid-battle)");
                    return false;
                }

                if (defeatedParty is null || (!defeatedParty.IsSettlement && defeatedParty.MobileParty is null))
                {
                    Logger.LogDebug("OnAIPartyLootCasualties skipped: defeatedParty is null or destroyed");
                    return false;
                }

                return true;
            }
        }
    }
}