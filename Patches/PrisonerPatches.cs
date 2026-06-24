using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection.Emit;
using BanditMilitiasRedux.Behaviours;
using BanditMilitiasRedux.Helpers;
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.LinQuick;
using static BanditMilitiasRedux.Helpers.Helper;

namespace BanditMilitiasRedux.Patches
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    [SuppressMessage("ReSharper", "UnusedMember.Global")]
    [SuppressMessage("ReSharper", "UnusedType.Global")]
    public sealed class PrisonerPatches
    {
        [HarmonyPatch(typeof(MapEvent), "FinishBattle")]
        public static class MapEventFinishBattlePatch
        {
            public static void Prefix(MapEvent __instance)
            {
                if (!__instance.HasWinner)
                    return;

                if (!__instance.InvolvedParties.AnyQ(partyBase => partyBase?.IsMobile == true && partyBase.MobileParty?.IsBanditMilitiaParty() == true))
                    return;

                foreach (var mobileParty in __instance.PartiesOnSide(__instance.WinningSide).Select(winnerParty => winnerParty?.Party?.MobileParty))
                {
                    if (mobileParty?.IsBanditMilitiaParty() == true && mobileParty.LeaderHero is { IsAlive: true } leader)
                        NotorietyBehavior.Instance?.RecordWin(leader);
                }
            }
        }

        [HarmonyPatch(typeof(MapEvent), "CalculateAndCommitMapEventResults")]
        public static class MapEventCalculateAndCommitMapEventResultsPatch
        {
            public static void Postfix(MapEvent __instance)
            {
                if (__instance.IsNavalMapEvent || !__instance.HasWinner)
                    return;
                
                if (!__instance.InvolvedParties.AnyQ(partyBase => partyBase.MobileParty?.IsBanditMilitiaParty() == true))
                    return;

                List<MapEventParty> loserBanditMilitias = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .WhereQ(party => party.Party?.MobileParty is { IsActive: true } && party.Party.MobileParty.IsBanditMilitiaParty())
                    .ToListQ();
                
                Hero? victorHero = __instance.PartiesOnSide(__instance.WinningSide)
                    .FirstOrDefaultQ(party => party?.Party?.LeaderHero is { IsAlive: true })?.Party?.LeaderHero;

                foreach (var party in loserBanditMilitias)
                {
                    MobileParty? BanditMilitiaParty = party?.Party?.MobileParty;
                    if (BanditMilitiaParty?.IsActive != true)
                        continue;

                    if (BanditMilitiaParty.LeaderHero is { IsAlive: true } defeatedLeader)
                        NotorietyBehavior.Instance?.RecordDefeat(defeatedLeader, victorHero);

                    bool playerInvolved = __instance.InvolvedParties.AnyQ(partyBase => partyBase == PartyBase.MainParty);
                    bool leaderCapturable = BanditMilitiaParty.LeaderHero is { IsAlive: true, IsPrisoner: false };
                    
                    switch (playerInvolved)
                    {
                        case true when leaderCapturable:
                        {
                            Hero? capturedMilitiaLeader = BanditMilitiaParty.LeaderHero;
                            TakePrisonerAction.Apply(PartyBase.MainParty, capturedMilitiaLeader);
                            capturedMilitiaLeader.IsKnownToPlayer = true;
                            Helper.GrantDefeatBounty(capturedMilitiaLeader, BanditMilitiaParty.Party.EstimatedStrength);

                            if (Hero.MainHero?.PartyBelongedToAsPrisoner?.MobileParty != BanditMilitiaParty)
                                TryTrashMobilePartySafelyReservingBanditLeader(BanditMilitiaParty);
                            continue;
                        }
                        case false when leaderCapturable:
                        {
                            var winnerLeaderParty = __instance.PartiesOnSide(__instance.WinningSide)
                                .FirstOrDefaultQ(p => p?.Party?.LeaderHero is { IsAlive: true })?.Party;

                            if (winnerLeaderParty is not null)
                            {
                                TakePrisonerAction.Apply(winnerLeaderParty, BanditMilitiaParty.LeaderHero);
                                if (Hero.MainHero?.PartyBelongedToAsPrisoner?.MobileParty != BanditMilitiaParty)
                                    TryTrashMobilePartySafelyReservingBanditLeader(BanditMilitiaParty);
                                continue;
                            }
                            break;
                        }
                    }

                    if (Hero.MainHero is not null && Hero.MainHero.PartyBelongedToAsPrisoner == party.Party)
                        continue;
                    
                    TryTrashMobilePartySafelyReservingBanditLeader(BanditMilitiaParty);
                }

                List<MapEventParty>? winnerBanditMilitias = __instance.PartiesOnSide(__instance.WinningSide)
                    .WhereQ(party => party?.Party?.MobileParty is { IsActive: true } && party.Party.MobileParty.IsBanditMilitiaParty())
                    .ToListQ();

                if (!winnerBanditMilitias.Any())
                    return;

                List<Hero?> loserHeroes = __instance.PartiesOnSide(__instance.DefeatedSide)
                    .SelectQ(party => party?.Party?.Owner)
                    .WhereQ(hero => hero is { IsAlive: true })
                    .ToListQ();

                foreach (var party in winnerBanditMilitias.Where(party => party?.Party?.MobileParty?.IsActive == true))
                {
                    AvoidanceManager.DecreaseAvoidance(loserHeroes, party);
                }
            }
        }

        [HarmonyPatch(typeof(BanditInteractionsCampaignBehavior), "OpenRosterScreenAfterBanditEncounter")]
        public static class BanditInteractionsCampaignBehaviorOpenRosterScreenAfterBanditEncounterPatch
        {
            public static void Prefix(MobileParty conversationParty, bool doBanditsJoinPlayerSide)
            {
                List<MobileParty> partiesToJoinPlayerSide = [];
                List<MobileParty> partiesToJoinEnemySide = [];
                
                if (PlayerEncounter.Current != null)
                {
                    PlayerEncounter.Current.FindAllNpcPartiesWhoWillJoinEvent(partiesToJoinPlayerSide, partiesToJoinEnemySide);
                    partiesToJoinEnemySide = partiesToJoinEnemySide.WhereQ(p => p.IsBanditMilitiaParty()).ToListQ();
                }
                else
                {
                    partiesToJoinEnemySide.Add(conversationParty);
                }

                foreach (TroopRoster roster in partiesToJoinEnemySide.SelectMany(m => new[]{ m.MemberRoster, m.PrisonRoster }))
                {
                    foreach (TroopRosterElement troop in roster.RemoveIf(t => t.Character.IsBanditMilitiaCharacterObject()))
                    {
                        var hero = troop.Character.HeroObject;
                        if (hero is null)
                            continue;
                        TakePrisonerAction.Apply(PartyBase.MainParty, hero);
                        hero.IsKnownToPlayer = true;
                    }
                }
            }
        }

        [HarmonyPatch(typeof(MakeHeroFugitiveAction), "ApplyInternal")]
        public static class MakeHeroFugitiveActionApplyInternalPatch
        {
            public static bool Prefix(Hero fugitive) => !fugitive.IsBanditMilitiaHero();
        }
        
        [HarmonyPatch(typeof(Hero), nameof(Hero.ChangeState))]
        public static class HeroChangeStateReserveBanditLeaderPatch
        {
            public static bool Prefix(Hero __instance, Hero.CharacterStates newState)
            {
                if (newState != Hero.CharacterStates.Fugitive)
                    return true;

                if (__instance is not { IsAlive: true, IsPrisoner: false }
                    || !__instance.IsBanditMilitiaHero()
                    || __instance.PartyBelongedTo?.IsBanditMilitiaParty() != true)
                    return true;

                ReusableHeroesBehavior.AddHeroToTheWaitingDictionary(__instance);
                return false;
            }
        }
        
        [HarmonyPatch(typeof(MobileParty), nameof(MobileParty.RemovePartyLeader))]
        public static class MobilePartyRemovePartyLeaderReserveBanditLeaderPatch
        {
            public static void Prefix(MobileParty __instance)
            {
                if (__instance?.IsBanditMilitiaParty() != true)
                    return;

                if (__instance.LeaderHero is { IsAlive: true, IsPrisoner: false } leader && leader.IsBanditMilitiaHero())
                    ReusableHeroesBehavior.AddHeroToTheWaitingDictionary(leader);
            }
        }

        [HarmonyPatch(typeof(RansomOfferCampaignBehavior), "ConsiderRansomPrisoner")]
        public static class RansomOfferCampaignBehaviorConsiderRansomPrisonerPatch
        {
            public static bool Prefix(Hero hero) => !hero.IsBanditMilitiaHero();
        }

        [HarmonyPatch(typeof(PlayerEncounter), "DoFreeOrCapturePrisonerHeroes")]
        public static class PlayerEncounterDoFreeOrCapturePrisonerHeroesPatch
        {
            public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
            {
                var codeMatcher = new CodeMatcher(instructions);

                CodeMatcher target = codeMatcher
                    .MatchStartForward(
                        new CodeMatch(OpCodes.Ldarg_0),
                        CodeMatch.LoadsField(AccessTools.Field("TaleWorlds.CampaignSystem.Encounters.PlayerEncounter:_capturedAlreadyPrisonerHeroes")),
                        new CodeMatch(OpCodes.Ldsfld),
                        new CodeMatch(OpCodes.Dup),
                        CodeMatch.Branches()
                    ).ThrowIfInvalid("Could not find the target at DoFreeOrCapturePrisonerHeroes");

                CodeInstruction[] insertion =
                [
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.LoadField(typeof(PlayerEncounter), "_capturedAlreadyPrisonerHeroes"),
                    CodeInstruction.LoadArgument(0),
                    CodeInstruction.LoadField(typeof(PlayerEncounter), "_mapEvent"),
                    CodeInstruction.Call(typeof(PlayerEncounterDoFreeOrCapturePrisonerHeroesPatch), nameof(UnFreeBMHeroes))
                ];

                target.Instruction.MoveLabelsTo(insertion[0]);
                target.Insert(insertion);

                return codeMatcher.Instructions();
            }

            private static void UnFreeBMHeroes(List<TroopRosterElement> freedHeroes, MapEvent mapEvent)
            {
                var bmHeroes = freedHeroes.WhereQ(t => t.Character.IsBanditMilitiaCharacterObject()).ToArrayQ();
                if (bmHeroes.Length == 0)
                    return;

                var playerMapEventParty = mapEvent.PartiesOnSide(mapEvent.PlayerSide).FirstOrDefault(p => p.Party == PartyBase.MainParty);
                var receivingLootShare = playerMapEventParty?.RosterToReceiveLootPrisoners;

                foreach (TroopRosterElement element in bmHeroes)
                {
                    if (element.Character.HeroObject.MapFaction?.IsAtWarWith(Hero.MainHero.MapFaction) == true)
                    {
                        var prisonParty = element.Character.HeroObject.PartyBelongedToAsPrisoner;
                        if (prisonParty is not null)
                        {
                            prisonParty.PrisonRoster.RemoveTroop(element.Character);
                            receivingLootShare?.AddToCounts(element.Character, 1, true, element.WoundedNumber, element.Xp, false);
                        }
                    }
                    freedHeroes.Remove(element);
                }
            }
        }

        [HarmonyPatch(typeof(PartyBase), nameof(PartyBase.AddPrisoner))]
        public static class PartyBaseAddPrisonerPatch
        {
            public static bool Prefix(PartyBase __instance, CharacterObject element)
            {
                if (!element.IsBanditMilitiaCharacterObject())
                    return true;

                return !__instance.PrisonerHeroes.Contains(element);
            }
        }
    }
}