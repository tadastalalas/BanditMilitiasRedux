using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using Microsoft.Extensions.Logging;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using SandBox.ViewModelCollection.Map.Tracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.ObjectSystem;
using static BanditMilitias.Globals;

namespace BanditMilitias
{
    internal sealed class Helper
    {
        internal static readonly AccessTools.FieldRef<MobileParty, bool> IsBandit =
            AccessTools.FieldRefAccess<MobileParty, bool>("<IsBandit>k__BackingField");

        internal static readonly AccessTools.FieldRef<NameGenerator, MBList<TextObject>> GangLeaderNames =
            AccessTools.FieldRefAccess<NameGenerator, MBList<TextObject>>("_gangLeaderNames");

        internal static readonly AccessTools.FieldRef<Hero, Settlement> _bornSettlement =
            AccessTools.FieldRefAccess<Hero, Settlement>("_bornSettlement");

        internal static readonly AccessTools.FieldRef<CharacterObject, bool> HiddenInEncyclopedia =
            AccessTools.FieldRefAccess<CharacterObject, bool>("<HiddenInEncyclopedia>k__BackingField");

        internal static void ReHome()
        {
            foreach (var BM in PowerCalculationService.GetCachedBMs(true).WhereQ(p => p.Leader is not null))
                _bornSettlement(BM.Leader) = BM.HomeSettlement;
        }

        internal static bool IsLandHideout(Settlement s) => !s.StringId.StartsWith("hideout_seaside");

        internal static List<Settlement> FindHideoutsAwayFromMainParty()
        {
            var playerPos = MobileParty.MainParty.Position.ToVec2();
            return Settlement.All
                .WhereQ(s => s.IsHideout
                    && s.GatePosition.ToVec2().DistanceSquared(playerPos) > SpawnHideoutMinPlayerDistanceSq)
                .ToListQ();
        }

        internal static void Trash(MobileParty mobileParty)
        {
            if (mobileParty?.IsActive != true)
                return;

            try
            {
                mobileParty.Ai?.DisableAi();
                DestroyPartyAction.Apply(null, mobileParty);
            }
            catch (Exception) { }
        }

        internal static bool Nuke()
        {
            try
            {
                if (Settlement.CurrentSettlement == null)
                    GameMenu.ExitToLast();
                Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                PartyImageMap.Clear();
                FlushMapEvents();
                LegacyFlushBanditMilitias();
                RemoveBadItems();
                PowerCalculationService.GetCachedBMs(true).Do(bm => Trash(bm.MobileParty));
                Hero.FindAll(h => h.IsBM()).ToArrayQ().Do(h => KillCharacterAction.ApplyByRemove(h));
                Heroes.ToArrayQ().Do(h => KillCharacterAction.ApplyByRemove(h));
                Heroes.Clear();
                InformationManager.DisplayMessage(new InformationMessage("BANDIT MILITIAS CLEARED."));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void LegacyFlushBanditMilitias()
        {
            var parties = Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value
                .WhereQ(m => m.IsBM()).ToListQ();
            if (parties.Count > 0)
            {
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value =
                    Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value.Except(parties).ToListQ();
                foreach (var mobileParty in parties)
                {
                    try
                    {
                        Trash(mobileParty);
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
                        .AnyQ(p => p?.IsMobile == true && p.MobileParty?.IsBM() == true);

                    if (!hasBMParties)
                        continue;

                    try
                    {
                        var bmPartiesToClean = mapEvent.InvolvedParties
                            .WhereQ(p => p?.IsMobile == true && p.MobileParty?.IsBM() == true)
                            .SelectQ(p => p.MobileParty)
                            .WhereQ(m => m != null)
                            .ToListQ();

                        Traverse.Create(mapEvent).Field<MapEventState>("_state").Value = MapEventState.Wait;
                        mapEvent.FinalizeEvent();

                        foreach (var mobileParty in bmPartiesToClean)
                        {
                            if (mobileParty?.IsActive == true)
                            {
                                Trash(mobileParty);
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

            var maxValue = map.Values.Max();
            var highest = map.WhereQ(x => x.Value == maxValue).SelectQ(x => x.Key).ToListQ();
            return highest[MBRandom.RandomInt(0, highest.Count)];
        }

        internal static void DecreaseAvoidance(List<Hero> loserHeroes, MapEventParty mep)
        {
            var bm = mep.Party.MobileParty?.GetBM();
            if (bm?.Avoidance is null) return;

            foreach (var loserHero in loserHeroes)
            {
                if (bm.Avoidance.TryGetValue(loserHero, out _))
                    bm.Avoidance[loserHero] = Math.Max(0, bm.Avoidance[loserHero] - Globals.AvoidanceIncreaseMin);
            }
        }

        internal static void InitMap()
        {
            T.Restart();
            ClearGlobals();
            if (Banners.Count == 0)
                SubModule.CacheBanners();
            EquipmentPool.Populate();
            BlackFlag = MBObjectManager.Instance.GetObject<CultureObject>("ad_bandit_blackflag");
            Looters = Clan.BanditFactions.First(c => c.StringId == "looters");
            Wights = Clan.BanditFactions.FirstOrDefaultQ(c => c.StringId == "wights");
            Hideouts = Settlement.All.WhereQ(s => s.IsHideout).ToListQ();
            RaidCap = Convert.ToInt32(Settlement.FindAll(s => s.IsVillage).CountQ() / 10f);
            HeroTemplates = CharacterObject.All
                .WhereQ(c => c.Occupation is Occupation.Bandit)
                .WhereQ(c => !c.StringId.Contains("quest") && !c.StringId.Contains("radagos"))
                .ToListQ();
            Giant = MBObjectManager.Instance.GetObject<CharacterObject>("giant");

            var stateMap = AccessTools.FieldRefAccess<ConversationManager, Dictionary<string, int>>("stateMap")(Campaign.Current.ConversationManager);
            LordConversationTokens = stateMap.Keys.WhereQ(k => k.Contains("lord_") || k.Contains("_lord")).SelectQ(k => stateMap[k]).ToHashSet();
            
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
                    Recruits.Add(recruit.Culture, new List<CharacterObject> { recruit });
            }

            var availableBandits = CharacterObject.All.WhereQ(c => c.Occupation is Occupation.Bandit && c.Level <= 11 && !c.HiddenInEncyclopedia).ToListQ();
            Globals.BasicRanged = availableBandits.WhereQ(c => c.DefaultFormationClass is FormationClass.Ranged).ToListQ();
            Globals.BasicInfantry = availableBandits.WhereQ(c => c.DefaultFormationClass is FormationClass.Infantry && c.StringId != "storymode_quest_raider").ToListQ();
            Globals.BasicCavalry = availableBandits.WhereQ(c => c.DefaultFormationClass is FormationClass.Cavalry).ToListQ();
            Globals.Villages = Settlement.All.WhereQ(s => s.IsVillage).ToListQ();

            PowerCalculationService.DoPowerCalculations(true);
            ReHome();
        }

        internal static void RemoveBadItems()
        {
            var logged = false;
            var badItems = MobileParty.MainParty.ItemRoster.WhereQ(i => !i.IsEmpty && i.EquipmentElement.Item?.Name is null).ToListQ();
            foreach (var item in badItems)
            {
                if (!logged)
                {
                    logged = true;
                    InformationManager.DisplayMessage(new("Bandit Militias found bad item(s) in player inventory:"));
                }

                InformationManager.DisplayMessage(new($"removing {item.EquipmentElement.Item?.StringId}"));
                MobileParty.MainParty.ItemRoster.Remove(item);
            }

            if (logged)
                InformationManager.DisplayMessage(new("Please save to a new spot then reload it."));
        }

        public static void RemoveMilitiaLeader(MobileParty party)
        {
            if (party.PartyComponent is ModBanditMilitiaPartyComponent bm)
            {
                bm.ForceRemoveLeader();
            }
        }
    }
}