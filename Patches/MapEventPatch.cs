using System.Collections.Generic;
using System.Linq;
using BanditMilitiasRedux.Behaviors;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Diagnostics;
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Encounters;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.LinQuick;
using static BanditMilitiasRedux.Core.Helper;

namespace BanditMilitiasRedux.Patches;

// BMR-FEATURE: Notoriety and leader progression, militia leaders on the winning side record a win and get the win reward | gate: always (reward: setting:CanTrain) | Prefix: the winners and the battle-start strength ratio must be read before vanilla finishes and cleans up the battle
[HarmonyPatch(typeof(MapEvent), "FinishBattle")]
internal class MapEventFinishBattlePatch
{
    internal static void Prefix(MapEvent __instance)
    {
        if (__instance is null || !__instance.HasWinner)
            return;

        if (!__instance.InvolvedParties.AnyQ(partyBase => partyBase?.IsMobile == true && partyBase.MobileParty?.IsBanditMilitiaParty() == true))
            return;

        NotorietyBehavior? notoriety = NotorietyBehavior.Instance;
        if (notoriety is null)
            return;

        MapEventSide winnerSide = __instance.WinningSide == BattleSideEnum.Attacker ? __instance.AttackerSide : __instance.DefenderSide;
        float strengthRatio = winnerSide.StrengthRatio;

        foreach (MapEventParty winnerParty in __instance.PartiesOnSide(__instance.WinningSide))
        {
            MobileParty? mobileParty = winnerParty?.Party?.MobileParty;
            if (mobileParty?.IsBanditMilitiaParty() != true || mobileParty.LeaderHero is not { IsAlive: true } leader)
                continue;

            notoriety.RecordWin(leader);
            HeroProgressionManager.RewardWin(leader, strengthRatio, notoriety.GetBattlesWon(leader));
        }
    }
}

// BMR-FEATURE: Defeated militias: leader captured by the player (with bounty), notoriety, avoidance, cleanup | gate: always | Prefix + Postfix: Prefix records the defeated leaders before vanilla commits results, Postfix acts on them
[HarmonyPatch(typeof(MapEvent), "CalculateAndCommitMapEventResults")]
internal class MapEventCalculateAndCommitMapEventResultsPatch
{
    internal static void Prefix(MapEvent __instance, out Dictionary<MobileParty, Hero> __state)
    {
        __state = [];

        if (__instance is null || __instance.IsNavalMapEvent || !__instance.HasWinner)
            return;

        foreach (MapEventParty mapEventParty in __instance.PartiesOnSide(__instance.DefeatedSide))
        {
            MobileParty? mobileParty = mapEventParty?.Party?.MobileParty;
            if (mobileParty?.IsBanditMilitiaParty() == true && mobileParty.LeaderHero is { IsAlive: true, IsPrisoner: false } leader)
                __state[mobileParty] = leader;
        }
    }

    internal static void Postfix(MapEvent __instance, Dictionary<MobileParty, Hero> __state)
    {
        if (__instance is null || __instance.IsNavalMapEvent || !__instance.HasWinner)
            return;

        if (!__instance.InvolvedParties.AnyQ(partyBase => partyBase.MobileParty?.IsBanditMilitiaParty() == true))
            return;

        List<MapEventParty> loserBanditMilitias = __instance.PartiesOnSide(__instance.DefeatedSide)
            .WhereQ(party => party.Party?.MobileParty is { IsActive: true } && party.Party.MobileParty.IsBanditMilitiaParty())
            .ToListQ();

        Hero? victorHero = __instance.PartiesOnSide(__instance.WinningSide)
            .FirstOrDefaultQ(party => party?.Party?.LeaderHero is { IsAlive: true })?.Party?.LeaderHero;

        foreach (MapEventParty party in loserBanditMilitias)
        {
            MobileParty? banditMilitiaParty = party?.Party?.MobileParty;
            if (banditMilitiaParty?.IsActive != true)
                continue;

            BmrCounters.MilitiasDefeated++;
            __state.TryGetValue(banditMilitiaParty, out Hero? defeatedLeader);

            if (defeatedLeader is { IsAlive: true })
                NotorietyBehavior.Instance?.RecordDefeat(defeatedLeader, victorHero);

            bool playerInvolved = __instance.InvolvedParties.AnyQ(partyBase => partyBase == PartyBase.MainParty);
            bool leaderCapturable = defeatedLeader is { IsAlive: true, IsPrisoner: false };

            if (playerInvolved && leaderCapturable)
            {
                defeatedLeader!.IsKnownToPlayer = true;
                GrantDefeatBounty(defeatedLeader, banditMilitiaParty.Party.EstimatedStrength);

                if (!TryQueuePlayerCaptureConversation(__instance, defeatedLeader))
                    TakePrisonerAction.Apply(PartyBase.MainParty, defeatedLeader);

                if (Hero.MainHero?.PartyBelongedToAsPrisoner?.MobileParty != banditMilitiaParty)
                    TryTrashMobilePartySafelyReservingBanditLeader(banditMilitiaParty);
                continue;
            }

            if (Hero.MainHero is not null && Hero.MainHero.PartyBelongedToAsPrisoner == party!.Party)
                continue;

            TryTrashMobilePartySafelyReservingBanditLeader(banditMilitiaParty);
        }

        List<MapEventParty> winnerBanditMilitias = __instance.PartiesOnSide(__instance.WinningSide)
            .WhereQ(party => party?.Party?.MobileParty is { IsActive: true } && party.Party.MobileParty.IsBanditMilitiaParty())
            .ToListQ();

        if (winnerBanditMilitias.Count == 0)
            return;

        List<Hero?> loserHeroes = __instance.PartiesOnSide(__instance.DefeatedSide)
            .SelectQ(party => party?.Party?.Owner)
            .WhereQ(hero => hero is { IsAlive: true })
            .ToListQ();

        foreach (MapEventParty party in winnerBanditMilitias.Where(party => party?.Party?.MobileParty?.IsActive == true))
            AvoidanceManager.DecreaseAvoidance(loserHeroes, party);
    }

    private static bool TryQueuePlayerCaptureConversation(MapEvent mapEvent, Hero defeatedLeader)
    {
        if (PlayerEncounter.Current is null || mapEvent.WinningSide != PartyBase.MainParty.Side)
            return false;

        defeatedLeader.PartyBelongedTo?.MemberRoster.RemoveTroop(defeatedLeader.CharacterObject);
        PlayerEncounter.Current.RosterToReceiveLootPrisoners.AddToCounts(defeatedLeader.CharacterObject, 1);
        return true;
    }
}
