using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: AI loot-casualty skill XP null guard for militia battles | gate: always | Prefix: skips vanilla when a militia side has no usable leader
[HarmonyPatch(typeof(DefaultSkillLevelingManager), "OnAIPartyLootCasualties")]
internal class DefaultSkillLevelingManagerPatch
{
    internal static bool Prefix(Hero winnerPartyLeader, PartyBase defeatedParty)
    {
        if (winnerPartyLeader?.PartyBelongedTo?.IsBanditMilitiaParty() == true
            || defeatedParty?.MobileParty?.IsBanditMilitiaParty() == true)
        {
            if (winnerPartyLeader is null || winnerPartyLeader.IsDead) return false;
            if (winnerPartyLeader.HeroDeveloper is null) return false;
            if (defeatedParty is null || (!defeatedParty.IsSettlement && defeatedParty.MobileParty is null)) return false;
        }
        return true;
    }
}
