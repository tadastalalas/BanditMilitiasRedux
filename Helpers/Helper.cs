using System;
using System.Collections.Generic;
using System.Linq;
using BanditMilitiasRedux.Behaviours;
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static BanditMilitiasRedux.Globals;

namespace BanditMilitiasRedux.Helpers
{
    internal static class Helper
    {
        internal static readonly AccessTools.FieldRef<MobileParty, bool> IsBandit =
            AccessTools.FieldRefAccess<MobileParty, bool>("<IsBandit>k__BackingField");

        internal static readonly AccessTools.FieldRef<NameGenerator, MBList<TextObject>> GangLeaderNames =
            AccessTools.FieldRefAccess<NameGenerator, MBList<TextObject>>("_gangLeaderNames");

        internal static Settlement BestSettlementForMerge(MobileParty mergeInitiatorParty, MobileParty mergeTargetParty)
        {
            Settlement? mobilePartyHomeSettlement = mergeInitiatorParty.HomeSettlement?.IsHideout ?? false ? mergeInitiatorParty.HomeSettlement : null;
            Settlement? mergeTargetHomeSettlement = mergeTargetParty.HomeSettlement?.IsHideout ?? false ? mergeTargetParty.HomeSettlement : null;

            Settlement? bestSettlement = new[] { mobilePartyHomeSettlement, mergeTargetHomeSettlement }
                .FirstOrDefault(settlement => settlement != null && Helper.IsLandHideout(settlement));

            return bestSettlement ?? FindClosestHideout(mergeInitiatorParty.Position.ToVec2());
        }
        
        private static Settlement FindClosestHideout(Vec2 position)
        {
            Settlement? closestHideout = null;
            float hideoutDistance = float.MaxValue;
            float anyDistance = float.MaxValue;

            foreach (Settlement hideout in Hideouts)
            {
                float distanceSquared = hideout.GatePosition.ToVec2().DistanceSquared(position);
                
                if (distanceSquared < anyDistance)
                    anyDistance = distanceSquared;

                if (!IsLandHideout(hideout) || distanceSquared > hideoutDistance)
                    continue;
                
                hideoutDistance = distanceSquared;
                closestHideout = hideout;
            }
            return closestHideout;
        }

        private static bool IsLandHideout(Settlement hideout) => !hideout.StringId.StartsWith("hideout_seaside");

        internal static void TryTrashMobilePartySafelyReservingBanditLeader(MobileParty mobileParty)
        {
            Hero? leaderToReserve = mobileParty.LeaderHero;
            
            if (leaderToReserve is { IsAlive: true, IsPrisoner: false })
                ReusableHeroesBehavior.AddHeroToTheWaitingDictionary(mobileParty.LeaderHero);
                
            mobileParty.Ai?.DisableAi();
            DestroyPartyAction.Apply(null, mobileParty);
        }

        internal static void NukeEverything()
        {
            if (Settlement.CurrentSettlement == null)
                GameMenu.ExitToLast();
            Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
            PartyImageMap.Clear();
            FlushMapEvents();
            LegacyFlushBanditMilitias();
            PowerCalculationManager.GetActiveBanditMilitiaParties(true).Do(bm => TryTrashMobilePartySafelyReservingBanditLeader(bm.MobileParty));
            foreach (var hero in Hero.FindAll(hero => hero.IsBanditMilitiaHero()).ToArrayQ())
            {
                KillCharacterAction.ApplyByRemove(hero);
            }
            foreach (var hero in AllAliveBanditMilitiaHeroes.ToArrayQ())
            {
                KillCharacterAction.ApplyByRemove(hero);
            }
            AllAliveBanditMilitiaHeroes.Clear();
            InformationManager.DisplayMessage(new InformationMessage("BANDIT MILITIAS CLEARED"));
        }

        private static void LegacyFlushBanditMilitias()
        {
            var parties = Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value
                .WhereQ(m => m.IsBanditMilitiaParty()).ToListQ();
            if (parties.Count > 0)
            {
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value =
                    Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value.Except(parties).ToListQ();
                foreach (var mobileParty in parties)
                {
                    try
                    {
                        TryTrashMobilePartySafelyReservingBanditLeader(mobileParty);
                    }
                    catch (Exception) { }
                }
            }

            foreach (var settlement in Settlement.All
                         .WhereQ(s => s.Party.PrisonRoster.GetTroopRoster()
                             .AnyQ(e => e.Character.StringId.EndsWith("Bandit_Militia"))))
            {
                for (var i = settlement.Party.PrisonRoster.Count - 1; i >= 0; i--)
                try
                {
                    var prisoner = settlement.Party.PrisonRoster.GetCharacterAtIndex(i);
                    if (prisoner.StringId.EndsWith("Bandit_Militia"))
                    {
                        settlement.Party.PrisonRoster.AddToCounts(prisoner, -1);
                        if (prisoner.HeroObject is not null)
                                KillCharacterAction.ApplyByRemove(prisoner.HeroObject);
                    }
                }
                catch (Exception) { }
            }

            var leftovers = Hero.AllAliveHeroes.WhereQ(h => h.StringId.EndsWith("Bandit_Militia")).ToListQ();
            foreach (Hero hero in leftovers)
            {
                KillCharacterAction.ApplyByRemove(hero);
            }
        }

        private static void FlushMapEvents()
        {
            try
            {
                var mapEvents = Traverse.Create(Campaign.Current.MapEventManager)
                    .Field<List<MapEvent>>("_mapEvents").Value;

                var eventsSnapshot = mapEvents.ToListQ();
                var mapEventsSet = new HashSet<MapEvent>(mapEvents);

                foreach (var mapEvent in eventsSnapshot)
                {
                    if (mapEvent is null || !mapEventsSet.Contains(mapEvent) || mapEvent.IsFinalized)
                        continue;

                    var hasBMParties = mapEvent.InvolvedParties
                        .AnyQ(p => p?.IsMobile == true && p.MobileParty?.IsBanditMilitiaParty() == true);

                    if (!hasBMParties)
                        continue;

                    try
                    {
                        var bmPartiesToClean = mapEvent.InvolvedParties
                            .WhereQ(p => p?.IsMobile == true && p.MobileParty?.IsBanditMilitiaParty() == true)
                            .SelectQ(p => p.MobileParty)
                            .WhereQ(m => m != null)
                            .ToListQ();

                        Traverse.Create(mapEvent).Field<MapEventState>("_state").Value = MapEventState.Wait;
                        mapEvent.FinalizeEvent();

                        foreach (var mobileParty in bmPartiesToClean)
                        {
                            if (mobileParty?.IsActive == true)
                            {
                                TryTrashMobilePartySafelyReservingBanditLeader(mobileParty);
                            }
                        }
                    }
                    catch (Exception) { }
                }
            }
            catch (Exception) { }
        }

        internal static CultureObject GetMostPrevalentFromNearbySettlements(Vec2 position)
        {
            const int arbitraryDistance = 100;
            var locatableSearchData = Settlement.StartFindingLocatablesAroundPosition(position, arbitraryDistance);
            var map = new Dictionary<CultureObject, int>();
            for (var settlement = Settlement.FindNextLocatable(ref locatableSearchData);
                 settlement != null;
                 settlement = Settlement.FindNextLocatable(ref locatableSearchData))
            {
                if (settlement.IsHideout)
                    continue;
                if (map.ContainsKey(settlement.Culture))
                    map[settlement.Culture]++;
                else
                    map.Add(settlement.Culture, 1);
            }

            if (BlackFlag is not null)
            {
                map.Remove(BlackFlag);
            }

            if (map.Count == 0)
                return MBObjectManager.Instance.GetObject<CultureObject>("empire");

            int maxValue = map.Values.Max();
            var highest = map.WhereQ(x => x.Value == maxValue).SelectQ(x => x.Key).ToListQ();
            return highest[MBRandom.RandomInt(0, highest.Count)];
        }

        internal static void InitializeGlobalModState()
        {
            ClearGlobals();
            if (Banners.Count == 0)
                SubModule.CacheBanners();
            EquipmentPool.Populate();
            BlackFlag = MBObjectManager.Instance.GetObject<CultureObject>("ad_bandit_blackflag");
            Looters = Clan.BanditFactions.First(c => c.StringId == "looters");
            Hideouts = Settlement.All.WhereQ(s => s.IsHideout).ToListQ();
            RaidCap = CalculateRaidCap();
            HeroTemplates = CharacterObject.All
                .WhereQ(c => c.Occupation is Occupation.Bandit)
                .WhereQ(c => !c.StringId.Contains("quest") && !c.StringId.Contains("radagos"))
                .ToListQ();
            Giant = MBObjectManager.Instance.GetObject<CharacterObject>("giant");

            var stateMap = AccessTools.FieldRefAccess<ConversationManager, Dictionary<string, int>>("stateMap")(Campaign.Current.ConversationManager);
            LordConversationTokens = [.. stateMap.Keys.WhereQ(k => k.Contains("lord_") || k.Contains("_lord")).SelectQ(k => stateMap[k])];
            
            var filter = new List<string>
            {
                "regular_fighter",
                "veteran_borrowed_troop",
                "_basic_root",
                "_elite_root"
            };

            var allRecruits = CharacterObject.All.WhereQ(c =>
                c.Level == 11
                && c.Occupation == Occupation.Soldier
                && filter.All(s => !c.StringId.Contains(s))
                && !c.StringId.EndsWith("_tier_1")
                && !c.Name.ToString().StartsWith("Glorious"));

            foreach (var recruit in allRecruits)
            {
                if (Recruits.ContainsKey(recruit.Culture))
                    Recruits[recruit.Culture].Add(recruit);
                else
                    Recruits.Add(recruit.Culture, [recruit]);
            }

            var availableBandits = CharacterObject.All.WhereQ(c => c.Occupation is Occupation.Bandit && c.Level <= 11 && !c.HiddenInEncyclopedia).ToListQ();
            Globals.BasicRanged = availableBandits.WhereQ(c => c.DefaultFormationClass is FormationClass.Ranged).ToListQ();
            Globals.BasicInfantry = availableBandits.WhereQ(c => c.DefaultFormationClass is FormationClass.Infantry && c.StringId != "storymode_quest_raider").ToListQ();
            Globals.BasicCavalry = availableBandits.WhereQ(c => c.DefaultFormationClass is FormationClass.Cavalry).ToListQ();
            Globals.Villages = Settlement.All.WhereQ(s => s.IsVillage).ToListQ();

            PowerCalculationManager.DoPowerCalculations(true);
            IsGlobalModStateInitialized = true;
        }

        internal static PartyBase? FirstBanditMilitiaPartyOnSide(MapEvent mapEvent, BattleSideEnum side)
        {
            return mapEvent.PartiesOnSide(side)
                .FirstOrDefaultQ(eventParty => eventParty?.Party?.IsMobile == true
                    && eventParty.Party.MobileParty?.IsBanditMilitiaParty() == true)?.Party;
        }

        internal static Settlement FindNearestTown(Vec2 anchorPos)
        {
            Settlement nearest = null;
            var nearestDistSq = float.MaxValue;
            var search = Settlement.StartFindingLocatablesAroundPosition(anchorPos, SettlementFindRange);
            for (var s = Settlement.FindNextLocatable(ref search); s != null; s = Settlement.FindNextLocatable(ref search))
            {
                if (!s.IsTown) continue;
                var d = s.GatePosition.ToVec2().DistanceSquared(anchorPos);
                if (d < nearestDistSq) { nearestDistSq = d; nearest = s; }
            }
            return nearest;
        }

        internal static Hero SelectStrongestHero(IEnumerable<Hero> heroes)
        {
            Hero? strongest = null;
            float bestPower = float.NegativeInfinity;
            foreach (var hero in heroes)
            {
                if (hero.Power < bestPower)
                    continue;
                bestPower = hero.Power;
                strongest = hero;
            }

            return strongest;
        }
        
        internal static void GrantDefeatBounty(Hero defeatedLeader, float militiaStrength)
        {
            if (defeatedLeader is null || Hero.MainHero is null)
                return;

            var t = militiaStrength <= 0f
                ? 0f
                : Math.Min(1f, militiaStrength / Globals.BountyStrengthForMaxReward);

            var gold = (int)(Globals.BountyGoldMin + t * (Globals.BountyGoldMax - Globals.BountyGoldMin));
            var renown = (int)Math.Round(Globals.BountyRenownMin + t * (Globals.BountyRenownMax - Globals.BountyRenownMin));
            if (renown < Globals.BountyRenownMin)
                renown = Globals.BountyRenownMin;

            GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, gold, disableNotification: true);
            if (renown > 0)
                GainRenownAction.Apply(Hero.MainHero, renown);

            var msg = new TextObject("{=BMRBounty}You collected a bounty of {GOLD} denars and {RENOWN} renown for {LEADER}.");
            msg.SetTextVariable("GOLD", gold);
            msg.SetTextVariable("RENOWN", renown);
            msg.SetTextVariable("LEADER", defeatedLeader.Name);
            InformationManager.DisplayMessage(new InformationMessage(msg.ToString()));
        }

        internal static bool IsHeroCapReached(Clan targetClan)
        {
            int counter = 0;
            for (int i = 0; i < AllAliveBanditMilitiaHeroes.Count; i++)
            {
                if (AllAliveBanditMilitiaHeroes[i].Clan != targetClan)
                    continue;
                
                counter++;
                if (counter >= HeroesCapPerClan)
                    return true;
            }
            return false;
        }

        public static int CalculateRaidCap()
        {
            float baseRaidCap = Settlement.FindAll(s => s.IsVillage).CountQ() / 10f;
            return Math.Max(1, Convert.ToInt32(baseRaidCap * (Settings.RaidCapPercent / 100f)));
        }
    }
}