using System.Collections.Generic;
using BanditMilitiasRedux.Core;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.LinQuick;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Militia heroes from a bandit encounter become the player's prisoners | gate: always | Prefix: the heroes must leave the rosters before vanilla opens the roster screen
[HarmonyPatch(typeof(BanditInteractionsCampaignBehavior), "OpenRosterScreenAfterBanditEncounter")]
internal class BanditInteractionsCampaignBehaviorPatch
{
    internal static void Prefix(MobileParty conversationParty, bool doBanditsJoinPlayerSide)
    {
        List<MobileParty> partiesToJoinPlayerSide = [];
        List<MobileParty> partiesToJoinEnemySide = [];

        if (PlayerEncounter.Current != null)
        {
            PlayerEncounter.Current.FindAllNpcPartiesWhoWillJoinEvent(partiesToJoinPlayerSide, partiesToJoinEnemySide);
            partiesToJoinEnemySide = partiesToJoinEnemySide.WhereQ(p => p.IsBanditMilitiaParty()).ToListQ();
        }
        else if (conversationParty is not null)
        {
            partiesToJoinEnemySide.Add(conversationParty);
        }

        foreach (MobileParty party in partiesToJoinEnemySide)
        {
            TakeMilitiaHeroesPrisoner(party.MemberRoster);
            TakeMilitiaHeroesPrisoner(party.PrisonRoster);
        }
    }

    private static void TakeMilitiaHeroesPrisoner(TroopRoster roster)
    {
        foreach (TroopRosterElement troop in roster.RemoveIf(t => t.Character.IsBanditMilitiaCharacterObject()))
        {
            Hero? hero = troop.Character.HeroObject;
            if (hero is null)
                continue;
            TakePrisonerAction.Apply(PartyBase.MainParty, hero);
            hero.IsKnownToPlayer = true;
        }
    }
}
