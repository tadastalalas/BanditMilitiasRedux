using BanditMilitiasRedux.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Bandit parties that meet merge into a Bandit Militia | gate: setting:MergeableSize | Prefix: replaces the vanilla encounter with a merge
[HarmonyPatch(typeof(EncounterManager), "StartPartyEncounter")]
internal class EncounterManagerPatch
{
    internal static bool Prefix(PartyBase attackerParty, PartyBase defenderParty)
    {
        MobileParty? attacker = attackerParty?.MobileParty;
        MobileParty? defender = defenderParty?.MobileParty;
        if (attacker is null || defender is null)
            return true;

        if (!BanditMilitiaManager.CanMobilePartiesMerge(attacker, defender))
            return true;

        if (BanditMilitiaManager.IfEngagedByThePlayer(attacker, defender))
            return true;

        BanditMilitiaManager.TryMergeBanditParties(attacker, defender);
        return false;
    }
}
