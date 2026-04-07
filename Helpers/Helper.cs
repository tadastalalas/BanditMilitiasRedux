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
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<Helper>();

        internal static readonly AccessTools.FieldRef<MobileParty, bool> IsBandit =
            AccessTools.FieldRefAccess<MobileParty, bool>("<IsBandit>k__BackingField");

        internal static readonly AccessTools.FieldRef<NameGenerator, MBList<TextObject>> GangLeaderNames =
            AccessTools.FieldRefAccess<NameGenerator, MBList<TextObject>>("_gangLeaderNames");

        internal static readonly AccessTools.FieldRef<Hero, Settlement> _bornSettlement =
            AccessTools.FieldRefAccess<Hero, Settlement>("_bornSettlement");

        internal static readonly AccessTools.FieldRef<CharacterObject, bool> HiddenInEncyclopedia =
            AccessTools.FieldRefAccess<CharacterObject, bool>("<HiddenInEncyclopedia>k__BackingField");

        internal static readonly AccessTools.FieldRef<MBObjectBase, bool> IsRegistered =
            AccessTools.FieldRefAccess<MBObjectBase, bool>("<IsRegistered>k__BackingField");

        /// <summary>
        /// Returns true if the settlement is a land hideout (not a seaside/naval hideout).
        /// </summary>
        internal static bool IsLandHideout(Settlement s) => !s.StringId.StartsWith("hideout_seaside");

        internal static void ReHome()
        {
            foreach (var BM in GetCachedBMs(true).WhereQ(p => p.Leader is not null))
                _bornSettlement(BM.Leader) = BM.HomeSettlement;
        }

        internal static bool TrySplitParty(MobileParty mobileParty)
            => MilitiaPartyFactory.TrySplitParty(mobileParty);

        internal static bool IsAvailableBanditParty(MobileParty __instance)
            => MilitiaPartyFactory.IsAvailableBanditParty(__instance);

        internal static bool TryMergeParties(MobileParty mobileParty, MobileParty mergeTarget)
            => MilitiaPartyFactory.TryMergeParties(mobileParty, mergeTarget);

        internal static TroopRoster[] MergeRosters(MobileParty sourceParty, MobileParty targetParty)
            => MilitiaPartyFactory.MergeRosters(sourceParty, targetParty);

        internal static void ResetUpgraderBehavior()
            => MilitiaPartyFactory.ResetUpgraderBehavior();

        internal static void Trash(MobileParty mobileParty)
        {
            if (mobileParty?.IsActive != true)
            {
                Logger.LogTrace($"Skipping trash - party already inactive: {mobileParty?.StringId}");
                return;
            }

            Logger.LogTrace($"Trashing {mobileParty.Name}({mobileParty.StringId})");
            try
            {
                mobileParty.Ai?.DisableAi();
                DestroyPartyAction.Apply(null, mobileParty);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error trashing {mobileParty.StringId}");
            }
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
                GetCachedBMs(true).Do(bm => Trash(bm.MobileParty));
                Hero.FindAll(h => h.IsBM()).ToArrayQ().Do(h => KillCharacterAction.ApplyByRemove(h));
                Heroes.ToArrayQ().Do(h => KillCharacterAction.ApplyByRemove(h));
                Heroes.Clear();
                InformationManager.DisplayMessage(new InformationMessage("BANDIT MILITIAS CLEARED"));
                // should be zero
                Logger.LogDebug($"Militias after nuke: {MobileParty.All.CountQ(m => m.IsBM())}.");
                return true;
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error during nuke.");
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
                Logger.LogTrace($">>> FLUSH {parties.Count} {Globals.Settings.BanditMilitiaString}");
                foreach (var mobileParty in parties)
                {
                    try
                    {
                        Trash(mobileParty);
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Error flushing {mobileParty}");
                        Meow();
                    }
                }
            }

            foreach (var settlement in Settlement.All
                         .WhereQ(s => s.Party.PrisonRoster.GetTroopRoster()
                             .AnyQ(e => e.Character.StringId.EndsWith("Bandit_Militia"))))
            {
                for (var i = 0; i < settlement.Party.PrisonRoster.Count; i++)
                    try
                    {
                        var prisoner = settlement.Party.PrisonRoster.GetCharacterAtIndex(i);
                        if (prisoner.StringId.EndsWith("Bandit_Militia"))
                        {
                            Logger.LogTrace($">>> FLUSH BM hero prisoner {prisoner.HeroObject?.Name} at {settlement.Name}.");
                            settlement.Party.PrisonRoster.AddToCounts(prisoner, -1);
                            KillCharacterAction.ApplyByRemove(prisoner.HeroObject);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Error flushing {settlement}");
                    }
            }

            var leftovers = Hero.AllAliveHeroes.WhereQ(h => h.StringId.EndsWith("Bandit_Militia")).ToListQ();
            foreach (Hero hero in leftovers)
            {
                Logger.LogTrace("Removing leftover hero " + hero);
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
                                Logger.LogTrace($"Trashed BM party {mobileParty.StringId} after MapEvent finalization");
                            }
                        }
                        Logger.LogTrace($"Flushed MapEvent with {bmPartiesToClean.Count} BM parties");
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Error flushing MapEvent: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error flushing MapEvent: {ex.Message}");
            }
        }

        internal static void PopulateItems()
            => EquipmentPool.Populate();

        internal static void DoPowerCalculations(bool force = false)
            => PowerCalculationService.DoPowerCalculations(force);

        internal static CultureObject GetMostPrevalentFromNearbySettlements(Vec2 position)
        {
            const int arbitraryDistance = 100;
            var locatableSearchData = Settlement.StartFindingLocatablesAroundPosition(position, arbitraryDistance);
            var map = new Dictionary<CultureObject, int>();
            for (var settlement = Settlement.FindNextLocatable(ref locatableSearchData);
                 settlement != null;
                 settlement = Settlement.FindNextLocatable(ref locatableSearchData))
            {
                if (settlement.IsHideout) continue;
                if (map.ContainsKey(settlement.Culture))
                {
                    map[settlement.Culture]++;
                }
                else
                {
                    map.Add(settlement.Culture, 1);
                }
            }

            if (BlackFlag is not null)
            {
                map.Remove(BlackFlag);
            }

            if (map.Count == 0)
            {
                Logger.LogWarning($"No settlements found near {position}, defaulting to Empire culture");
                return MBObjectManager.Instance.GetObject<CultureObject>("empire");
            }

            var maxValue = map.Values.Max();
            var highest = map.WhereQ(x => x.Value == maxValue).SelectQ(x => x.Key).ToListQ();
            return highest[MBRandom.RandomInt(0, highest.Count)];
        }

        internal static void PrintInstructionsAroundInsertion(List<CodeInstruction> codes, int insertPoint, int insertSize, int adjacentNum = 5)
        {
            Logger.LogTrace($"Inserting {insertSize} at {insertPoint}.");

            var adjustedAdjacent = codes.Count - adjacentNum >= 0 ? adjacentNum : Math.Max(0, codes.Count - adjacentNum);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                Logger.LogTrace($"{codes[insertPoint - adjustedAdjacent + i].opcode,-10}{codes[insertPoint - adjustedAdjacent + i].operand}");
            }

            for (var i = 0; i < insertSize; i++)
            {
                Logger.LogTrace($"{codes[insertPoint + i].opcode,-10}{codes[insertPoint + i].operand}");
            }

            adjustedAdjacent = insertPoint + adjacentNum <= codes.Count ? adjacentNum : Math.Max(codes.Count, adjustedAdjacent);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                Logger.LogTrace($"{codes[insertPoint + insertSize + adjustedAdjacent + i].opcode,-10}{codes[insertPoint + insertSize + adjustedAdjacent + i].operand}");
            }
        }

        internal static int NumMountedTroops(TroopRoster troopRoster)
            => EquipmentPool.NumMountedTroops(troopRoster);

        internal static Hero CreateOrReuseHero(Settlement settlement)
            => HeroFactory.CreateOrReuseHero(settlement);

        internal static Hero CreateHero(Settlement settlement)
            => HeroFactory.CreateHero(settlement);

        internal static void RandomizeHeroAppearance(Hero hero)
            => HeroFactory.RandomizeHeroAppearance(hero);

        internal static void AdjustCavalryCount(TroopRoster troopRoster)
            => EquipmentPool.AdjustCavalryCount(troopRoster);

        internal static void TrainMilitia(MobileParty mobileParty)
            => MilitiaPartyFactory.TrainMilitia(mobileParty);

        internal static IEnumerable<ModBanditMilitiaPartyComponent> GetCachedBMs(bool forceRefresh = false)
            => PowerCalculationService.GetCachedBMs(forceRefresh);

        internal static void InitMilitia(MobileParty militia, TroopRoster[] rosters, CampaignVec2 position)
            => MilitiaPartyFactory.InitMilitia(militia, rosters, position);

        internal static void LogMilitiaFormed(MobileParty mobileParty)
        {
            try
            {
                var troopString = $"{mobileParty.Party.NumberOfAllMembers} troop" + (mobileParty.Party.NumberOfAllMembers > 1 ? "s" : "");
                var strengthString = $"{Math.Round(mobileParty.Party.EstimatedStrength)} strength";
                Logger.LogTrace($"{$"New Bandit Militia led by {mobileParty.LeaderHero?.Name}",-70} | {troopString,10} | {strengthString,12} | >>> {GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100}%");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error in LogMilitiaFormed");
            }
        }

        internal static void Meow()
        {
            if (SubModule.MEOWMEOW)
            {
                Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
                Debugger.Break();
            }
        }

        internal static void DecreaseAvoidance(List<Hero> loserHeroes, MapEventParty mep)
        {
            var bm = mep.Party.MobileParty?.GetBM();
            if (bm?.Avoidance is null) return;

            foreach (var loserHero in loserHeroes)
            {
                if (bm.Avoidance.TryGetValue(loserHero, out _))
                    bm.Avoidance[loserHero] -= MilitiaBehavior.Increment;
                else
                    bm.Avoidance.Add(loserHero, MBRandom.RandomInt(15, 35));
            }
        }

        internal static void InitMap()
        {
            T.Restart();
            Logger.LogTrace("MapScreen.OnInitialize");
            ClearGlobals();
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
                .ToListQ(); // && c.StringId.StartsWith("bm_hero_")).ToListQ();
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

            DoPowerCalculations(true);
            ReHome();
            Logger.LogTrace($"InitMap took {T.ElapsedTicks / 10000F:F3}ms to finish, there are {MobileParty.All.CountQ(m => m.IsBM())} bandit militias.");
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

                InformationManager.DisplayMessage(new($"removing {item.EquipmentElement.Item.StringId}"));
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