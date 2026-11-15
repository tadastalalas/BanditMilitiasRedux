using System;
using BanditMilitiasRedux.Compat;
using BanditMilitiasRedux.Core;
using BanditMilitiasRedux.Data;
using BanditMilitiasRedux.Diagnostics;
using BanditMilitiasRedux.Managers;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static BanditMilitiasRedux.Core.Globals;
using static BanditMilitiasRedux.Core.Helper;

namespace BanditMilitiasRedux.Behaviors;

// BMR-FEATURE: Militia life cycle on the map: merge, grow, train, split, raid messages, stuck rescue, cleanup | gate: always
public class MilitiaBehavior : CampaignBehaviorBase
{
    // Released SyncData key (kept for save compatibility).
    private const string HeroesKey = "Heroes";

    public override void RegisterEvents()
    {
        CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, OnVillageBeingRaidedEvent);
        CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, OnRaidCompletedEvent);
        CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
        CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, OnDailyTickPartyEvent);
        CampaignEvents.TickPartialHourlyAiEvent.AddNonSerializedListener(this, OnTickPartialHourlyAiEvent);
        CampaignEvents.MapEventEnded.AddNonSerializedListener(this, OnMapEventEnded);
        CampaignEvents.HourlyTickEvent.AddNonSerializedListener(this, OnHourlyTickEvent);
        CampaignEvents.MobilePartyCreated.AddNonSerializedListener(this, OnMobilePartyCreated);
        CampaignEvents.VillageStateChanged.AddNonSerializedListener(this, OnVillageStateChanged);
    }

    // Keys: "Heroes".
    public override void SyncData(IDataStore dataStore) => dataStore.SyncData(HeroesKey, ref AllAliveBanditMilitiaHeroes);

    private static void OnVillageBeingRaidedEvent(Village villageBeingRaided)
    {
        if (villageBeingRaided.Settlement?.Party?.MapEvent is null)
            return;

        PartyBase? party = FirstBanditMilitiaPartyOnSide(villageBeingRaided.Settlement.Party.MapEvent, BattleSideEnum.Attacker);
        if (party is null)
            return;

        // The village's owner clan decides (Village.Owner is the settlement party, which has no leader hero).
        if (ModSettings.ShowRaids && Hero.MainHero?.Clan is not null && villageBeingRaided.Settlement.OwnerClan == Hero.MainHero.Clan)
            InformationManager.DisplayMessage(new InformationMessage(Texts.VillageBeingRaided(villageBeingRaided.Name, party.Name).ToString()));
    }

    private static void OnRaidCompletedEvent(BattleSideEnum winnerSide, RaidEventComponent eventComponent)
    {
        if (eventComponent.MapEvent is null)
            return;

        PartyBase? party = FirstBanditMilitiaPartyOnSide(eventComponent.MapEvent, BattleSideEnum.Attacker);
        if (party is null)
            return;

        // Every militia that took part is released, not only the first one.
        foreach (MapEventParty attacker in eventComponent.MapEvent.PartiesOnSide(BattleSideEnum.Attacker))
        {
            MobileParty? raider = attacker?.Party?.MobileParty;
            if (raider?.Ai is null || !raider.IsBanditMilitiaParty() || SaSCompat.IsReservedBySaS(raider))
                continue;

            raider.Ai.SetDoNotMakeNewDecisions(false);
            raider.SetMoveModeHold();
        }

        if (!ModSettings.ShowRaids)
            return;

        Settlement? raidedSettlement = eventComponent.MapEventSettlement;
        Vec2 anchorPos = raidedSettlement?.GatePosition.ToVec2() ?? party.Position.ToVec2();
        string townName = FindNearestTown(anchorPos)?.Name?.ToString() ?? Texts.UnknownTown.ToString();
        InformationManager.DisplayMessage(new InformationMessage(Texts.VillageRaided(raidedSettlement?.Name?.ToString() ?? string.Empty, party.Name, townName).ToString()));
    }

    private static void OnVillageStateChanged(Village village, Village.VillageStates oldState, Village.VillageStates newState, MobileParty? raiderParty)
    {
        if (newState != Village.VillageStates.Looted || village?.Settlement is null)
            return;

        BanditMilitiaManager.ReleaseMilitiasRaidingLootedVillage(village.Settlement, raiderParty);
    }

    private static void OnMobilePartyDestroyed(MobileParty destroyedMobileParty, PartyBase? destroyer)
    {
        if (destroyedMobileParty.IsBanditMilitiaParty())
        {
            StuckTracker.Remove(destroyedMobileParty);
            PatrolRepickTimes.Remove(destroyedMobileParty);
            IdleRetryTimes.Remove(destroyedMobileParty);
            PartyImageMap.Remove(destroyedMobileParty);
            if (destroyedMobileParty.LeaderHero is { IsAlive: true, IsPrisoner: false } leader && leader.PartyBelongedTo == destroyedMobileParty)
                ReusableHeroesBehavior.AddHeroToTheWaitingDictionary(leader);
        }

        if (!destroyedMobileParty.IsBanditMilitiaParty() || destroyer?.LeaderHero is null || !destroyer.LeaderHero.IsAlive)
            return;

        AvoidanceManager.AdjustAvoidanceForSurroundingBanditMilitias(destroyedMobileParty, destroyer);
    }

    private static void OnDailyTickPartyEvent(MobileParty mobileParty)
    {
        if (!mobileParty.IsBanditMilitiaParty())
            return;

        mobileParty.GetBanditMilitiaParty()?.ClearCachedName();

        if ((int)CampaignTime.Now.ToDays % CampaignTime.DaysInWeek == 0)
            AvoidanceManager.AdjustAvoidance(mobileParty);

        BanditMilitiaManager.TryGrowMilitiaParty(mobileParty);

        // Not in a battle: training changes the roster.
        if (mobileParty.MapEvent is null && MBRandom.RandomFloat <= ModSettings.DailyTrainingChance * Balance.PercentToFraction)
            BanditMilitiaManager.TrainBanditMilitiaParty(mobileParty);

        BanditMilitiaManager.TrySplitBanditMilitiaParty(mobileParty);
        MilitiaTrackerManager.Refresh(mobileParty);

        PowerCalculationManager.DoPowerCalculations();
    }

    private static void OnTickPartialHourlyAiEvent(MobileParty banditMilitiaParty)
    {
        if (SaSCompat.IsReservedBySaS(banditMilitiaParty))
            return;

        bool isMilitia = banditMilitiaParty.IsBanditMilitiaParty();

        if (!isMilitia && !BanditMilitiaManager.IsAvailableBanditParty(banditMilitiaParty))
            return;

        if (banditMilitiaParty.MapEvent is not null)
            return;

        if (banditMilitiaParty.IsUsedByAQuest()) // Can bandit militia party be used by quests at all?
            return;

        Vec2 currentMilitiaPartyPosition = banditMilitiaParty.Position.ToVec2();

        if (isMilitia)
        {
            if (BanditMilitiaManager.TryHandleStuckMilitia(banditMilitiaParty, currentMilitiaPartyPosition))
                return;

            if (banditMilitiaParty is { DefaultBehavior: AiBehavior.EngageParty, TargetParty: not null }
                && !FactionManager.IsAtWarAgainstFaction(banditMilitiaParty.MapFaction, banditMilitiaParty.TargetParty.MapFaction))
            {
                if (banditMilitiaParty.TargetParty.DefaultBehavior != AiBehavior.EngageParty || banditMilitiaParty.TargetParty.TargetParty != banditMilitiaParty)
                    banditMilitiaParty.SetMoveModeHold();
            }

            if (banditMilitiaParty.Ai?.DoNotMakeNewDecisions == true && banditMilitiaParty.DefaultBehavior != AiBehavior.RaidSettlement)
            {
                banditMilitiaParty.Ai.SetDoNotMakeNewDecisions(false);
                banditMilitiaParty.SetMoveModeHold();
                BanditMilitiaManager.Think(banditMilitiaParty);
                return;
            }
        }

        CampaignTime cooldownToMergeOrSplit = CampaignTime.Hours(ModSettings.CooldownHours);
        CampaignTime timeNow = CampaignTime.Now;

        if (isMilitia)
        {
            // No merge search during the cooldown, on the way to a raid, while holding the player prisoner
            // or while the player is closing in.
            BanditMilitiaPartyComponent partyComponent = banditMilitiaParty.GetBanditMilitiaParty()!;
            if (timeNow < partyComponent.LastMergedOrSplitDate + cooldownToMergeOrSplit
                || banditMilitiaParty.DefaultBehavior == AiBehavior.RaidSettlement
                || banditMilitiaParty.IsHoldingPlayerPrisoner()
                || MobileParty.MainParty.ShortTermBehavior == AiBehavior.EngageParty && MobileParty.MainParty.ShortTermTargetParty == banditMilitiaParty)
            {
                BanditMilitiaManager.Think(banditMilitiaParty);
                return;
            }
        }
        else if (banditMilitiaParty.IsHoldingPlayerPrisoner()
                 || MobileParty.MainParty.ShortTermBehavior == AiBehavior.EngageParty && MobileParty.MainParty.ShortTermTargetParty == banditMilitiaParty)
            return;

        BanditMilitiaManager.TryFindAndMergeNearbyBandits(banditMilitiaParty, currentMilitiaPartyPosition, timeNow, cooldownToMergeOrSplit);
    }

    private static void OnMapEventEnded(MapEvent mapEvent) => DrainDeferredTrashParties();

    private static void OnHourlyTickEvent()
    {
        if (DeferredTrashParties.Count > 0)
            DrainDeferredTrashParties();
    }

    private static void OnMobilePartyCreated(MobileParty party)
    {
        if (BmrLog.IsDebugEnabled && party.StringId.StartsWith(Balance.DesertersPartyPrefix, StringComparison.Ordinal))
            BmrLog.Debug($"BMR-DESERTERS-SPAWN: {party.StringId}\n{Environment.StackTrace}");
    }
}
