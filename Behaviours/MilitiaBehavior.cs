using BanditMilitiasRedux.Constructors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using static BanditMilitiasRedux.Helpers.Helper;
using static BanditMilitiasRedux.Globals;
using BanditMilitiasRedux.Helpers;
using BanditMilitiasRedux.Managers;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.Localization;

namespace BanditMilitiasRedux.Behaviours
{
    public class MilitiaBehavior : CampaignBehaviorBase
    {
        public override void RegisterEvents()
        {
            CampaignEvents.VillageBeingRaided.AddNonSerializedListener(this, OnVillageBeingRaidedEvent);
            CampaignEvents.RaidCompletedEvent.AddNonSerializedListener(this, OnRaidCompletedEvent);
            CampaignEvents.MobilePartyDestroyed.AddNonSerializedListener(this, OnMobilePartyDestroyed);
            CampaignEvents.DailyTickPartyEvent.AddNonSerializedListener(this, OnDailyTickPartyEvent);
            CampaignEvents.TickPartialHourlyAiEvent.AddNonSerializedListener(this, OnTickPartialHourlyAiEvent);
        }

        public override void SyncData(IDataStore dataStore) => dataStore.SyncData("Heroes", ref AllAliveBanditMilitiaHeroes);

        private static void OnVillageBeingRaidedEvent(Village  villageBeingRaided)
        {
            if (villageBeingRaided.Settlement?.Party?.MapEvent is null)
                return;

            PartyBase? party = FirstBanditMilitiaPartyOnSide(villageBeingRaided.Settlement.Party.MapEvent, BattleSideEnum.Attacker);
            if (party is null)
                return;
            
            if (Settings.ShowRaids && villageBeingRaided.Owner?.LeaderHero == Hero.MainHero)
                InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=BMVillageBeingRaided}{villageBeingRaided.Name} is being raided by {party.Name}!").SetTextVariable("villageBeingRaided.Name", villageBeingRaided.Name).SetTextVariable("party.Name", party.Name).ToString()));
        }

        private static void OnRaidCompletedEvent(BattleSideEnum winnerSide, RaidEventComponent eventComponent)
        {
            if (eventComponent.MapEvent is null)
                return;
            
            PartyBase? party = FirstBanditMilitiaPartyOnSide(eventComponent.MapEvent, BattleSideEnum.Attacker);
            if (party is null)
                return;

            if (party.MobileParty?.Ai is not null)
            {
                party.MobileParty.Ai.SetDoNotMakeNewDecisions(false);
                party.MobileParty.SetMoveModeHold();
            }

            if (!Settings.ShowRaids)
                return;
            
            var raidedSettlement = eventComponent.MapEventSettlement;
            Vec2 anchorPos = raidedSettlement?.GatePosition.ToVec2() ?? party.Position.ToVec2();
            string townName = FindNearestTown(anchorPos).Name?.ToString() ?? "(Unknown Town)";
            InformationManager.DisplayMessage(new InformationMessage(new TextObject("{=BMVillageRaided}{raidedSettlement?.Name} raided! {party.Name} is fat with loot near {townName}!").SetTextVariable("raidedSettlement?.Name", raidedSettlement?.Name).SetTextVariable("party.Name", party.Name).SetTextVariable("townName", townName).ToString()));
        }

        private static void OnMobilePartyDestroyed(MobileParty destroyedMobileParty, PartyBase? destroyer)
        {
            if (destroyedMobileParty.IsBanditMilitiaParty())
            {
                StuckTracker.Remove(destroyedMobileParty);
                if (destroyedMobileParty.LeaderHero is { IsAlive: true, IsPrisoner: false })
                    ReusableHeroesBehavior.AddHeroToTheWaitingDictionary(destroyedMobileParty.LeaderHero);
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

            if ((int)CampaignTime.Now.ToDays % CampaignTime.DaysInWeek == 0 && Settings.AllowPillaging)
                AvoidanceManager.AdjustAvoidance(mobileParty);

            BanditMilitiaManager.TryGrowMilitiaParty(mobileParty);
            
            if (MBRandom.RandomFloat <= Settings.DailyTrainingChance * 0.01f)
                BanditMilitiaManager.TrainBanditMilitiaParty(mobileParty);

            BanditMilitiaManager.TrySplitBanditMilitiaParty(mobileParty);
            
            PowerCalculationManager.DoPowerCalculations();
        }

        private static void OnTickPartialHourlyAiEvent(MobileParty banditMilitiaParty)
        {
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
            
            CampaignTime cooldownToMergeOrSplit = CampaignTime.Hours(Settings.CooldownHours);
            CampaignTime timeNow = CampaignTime.Now;

            if (isMilitia)
            {
                BanditMilitiaPartyComponent partyComponent = banditMilitiaParty.GetBanditMilitiaParty()!;
                if (timeNow < partyComponent.LastMergedOrSplitDate + cooldownToMergeOrSplit
                    || MobileParty.MainParty.ShortTermBehavior == AiBehavior.EngageParty && MobileParty.MainParty.ShortTermTargetParty == banditMilitiaParty)
                {
                    BanditMilitiaManager.Think(banditMilitiaParty);
                    return;
                }
            }
            else if (MobileParty.MainParty.ShortTermBehavior == AiBehavior.EngageParty && MobileParty.MainParty.ShortTermTargetParty == banditMilitiaParty)
                return;
            
            BanditMilitiaManager.TryFindAndMergeNearbyBandits(banditMilitiaParty, currentMilitiaPartyPosition, timeNow, cooldownToMergeOrSplit);
        }
    }
}