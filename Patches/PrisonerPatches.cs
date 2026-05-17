using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Microsoft.Extensions.Logging;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.BarterSystem.Barterables;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.LinQuick;
using static BanditMilitias.Helper;

namespace BanditMilitias.Patches
{
    public sealed class PrisonerPatches
    {
        [HarmonyPatch(typeof(MapEvent), "FinishBattle")]
        public static class MapEventFinishBattlePatch
        {
            public static void Prefix(MapEvent __instance)
            {
                if (!__instance.HasWinner) return;
                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .WhereQ(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent);
                foreach (var party in loserBMs)
                {
                    MobileParty mobileParty = party.Party.MobileParty;
                    if (mobileParty.LeaderHero?.IsDead != false && mobileParty.MemberRoster.TotalHealthyCount >= Globals.Settings.DisperseSize)
                        RemoveMilitiaLeader(mobileParty);
                }
                PowerCalculationService.DoPowerCalculations();
            }
        }

        [HarmonyPatch(typeof(MapEvent), "CalculateAndCommitMapEventResults")]
        public static class MapEventLootDefeatedPartiesPatch
        {
            public static void Prefix(MapEvent __instance)
            {
                if (!__instance.HasWinner)
                    return;

                if (__instance.InvolvedParties.AnyQ(p => p?.IsMobile == true && p.MobileParty?.IsBM() == true))
                    PowerCalculationService.DoPowerCalculations();
            }

            public static void Postfix(MapEvent __instance)
            {
                if (!__instance.HasWinner)
                    return;

                var loserBMs = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .WhereQ(p => p.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent
                    && p.Party.MobileParty.IsActive)
                    .ToListQ();

                foreach (var party in loserBMs)
                {
                    if (party?.Party?.MobileParty?.IsActive != true)
                        continue;

                    if (party.Party.MobileParty.MemberRoster.TotalHealthyCount < Globals.Settings.DisperseSize)
                    {
                        if (Hero.MainHero.PartyBelongedToAsPrisoner == party.Party)
                            continue;

                        Trash(party.Party.MobileParty);
                    }
                }

                var winnerBMs = __instance.PartiesOnSide(__instance.WinningSide)
                    .WhereQ(p => p?.Party?.MobileParty?.PartyComponent is ModBanditMilitiaPartyComponent
                        && p.Party.MobileParty.IsActive)
                    .ToListQ();

                if (!winnerBMs.Any())
                    return;

                var loserHeroes = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .SelectQ(mep => mep?.Party?.Owner)
                    .WhereQ(h => h != null && h.IsAlive)
                    .ToListQ();

                foreach (var bm in winnerBMs)
                {
                    if (bm?.Party?.MobileParty?.IsActive != true)
                        continue;

                    PartyBase party = bm.Party;

                    if (party.LeaderHero?.IsDead == true)
                    {
                        if (party.MemberRoster.Contains(party.LeaderHero.CharacterObject))
                            party.MemberRoster.RemoveTroop(party.LeaderHero.CharacterObject);
                        RemoveMilitiaLeader(party.MobileParty);
                    }

                    DecreaseAvoidance(loserHeroes, bm);
                }
            }
        }

        [HarmonyPatch(typeof(BanditInteractionsCampaignBehavior), "OpenRosterScreenAfterBanditEncounter")]
        public static class BanditsCampaignBehaviorOpenRosterScreenAfterBanditEncounterPatch
        {
            public static void Prefix(MobileParty conversationParty, bool doBanditsJoinPlayerSide)
            {
                List<MobileParty> partiesToJoinPlayerSide = new List<MobileParty>();
                List<MobileParty> partiesToJoinEnemySide = new List<MobileParty>();
                
                if (PlayerEncounter.Current != null)
                {
                    PlayerEncounter.Current.FindAllNpcPartiesWhoWillJoinEvent(partiesToJoinPlayerSide, partiesToJoinEnemySide);
                    partiesToJoinEnemySide = partiesToJoinEnemySide.WhereQ(p => p.IsBM()).ToListQ();
                }
                else
                {
                    partiesToJoinEnemySide.Add(conversationParty);
                }


                foreach (TroopRoster roster in partiesToJoinEnemySide.SelectMany(m => new[]{ m.MemberRoster, m.PrisonRoster }))
                {
                    foreach (TroopRosterElement troop in roster.RemoveIf(t => t.Character.IsBM()))
                    {
                        TakePrisonerAction.Apply(PartyBase.MainParty, troop.Character.HeroObject);
                        troop.Character.HeroObject.IsKnownToPlayer = true;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(RansomOfferCampaignBehavior), "ConsiderRansomPrisoner")]
        public static class RansomOfferCampaignBehaviorConsiderRansomPrisonerPatch
        {
            public static bool Prefix(Hero hero)
            {
                if (hero.IsBM())
                    return false;

                return true;
            }
        }

        [HarmonyPatch(typeof(TeleportHeroAction), nameof(TeleportHeroAction.ApplyImmediateTeleportToSettlement))]
        public static class TeleportHeroActionApplyImmediateTeleportToSettlementPatch
        {
            public static bool Prefix(Hero heroToBeMoved, Settlement targetSettlement)
            {
                if (heroToBeMoved.IsBM() && targetSettlement is not null)
                {
                    KillCharacterAction.ApplyByRemove(heroToBeMoved);
                    return false;
                }
                return true;
            }
        }
    }
}