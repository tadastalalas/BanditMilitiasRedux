using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BanditMilitias.Helpers;
using HarmonyLib;
using Helpers;
using Microsoft.Extensions.Logging;
using SandBox.View.Map;
using SandBox.ViewModelCollection.Map;
using SandBox.ViewModelCollection.Map.Tracker;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.CampaignBehaviors;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.Extensions;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Naval;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.Localization;
using TaleWorlds.ModuleManager;
using TaleWorlds.MountAndBlade;
using TaleWorlds.ObjectSystem;
using static BanditMilitias.Globals;

// ReSharper disable CheckNamespace
// ReSharper disable InconsistentNaming  

namespace BanditMilitias
{
    internal sealed class Helper
    {
        private static ILogger _logger;
        private static ILogger Logger => _logger ??= LogFactory.Get<Helper>();
        
        private const float ReductionFactor = 0.8f;
        private const float SplitDivisor = 2;
        private const float RemovedHero = 1;

        private const int WeaponSlot0 = 0;
        private const int WeaponSlot1 = 1;
        private const int WeaponSlot2 = 2;
        private const int WeaponSlot3 = 3;
        private const int HeadArmorSlot = 5;
        private const int BodyArmorSlot = 6;
        private const int LegArmorSlot = 7;
        private const int HandArmorSlot = 8;
        private const int CapeSlot = 9;
        private const int HorseSlot = 10;
        private const int HarnessSlot = 11;

        internal static readonly AccessTools.FieldRef<MobileParty, bool> IsBandit =
            AccessTools.FieldRefAccess<MobileParty, bool>("<IsBandit>k__BackingField");

        internal static readonly AccessTools.FieldRef<NameGenerator, MBList<TextObject>> GangLeaderNames =
            AccessTools.FieldRefAccess<NameGenerator, MBList<TextObject>>("_gangLeaderNames");

        private static readonly AccessTools.StructFieldRef<EquipmentElement, ItemModifier> ItemModifier =
            AccessTools.StructFieldRefAccess<EquipmentElement, ItemModifier>("<ItemModifier>k__BackingField");

        private static readonly AccessTools.FieldRef<PartyBase, ItemRoster> ItemRoster =
            AccessTools.FieldRefAccess<PartyBase, ItemRoster>("<ItemRoster>k__BackingField");

        internal static readonly AccessTools.FieldRef<Hero, Settlement> _bornSettlement =
            AccessTools.FieldRefAccess<Hero, Settlement>("_bornSettlement");

        // ReSharper disable once StringLiteralTypo
        internal static readonly AccessTools.FieldRef<CharacterObject, bool> HiddenInEncyclopedia =
            AccessTools.FieldRefAccess<CharacterObject, bool>("<HiddenInEncyclopedia>k__BackingField");

        private static readonly AccessTools.FieldRef<MobileParty, Clan> actualClan =
            AccessTools.FieldRefAccess<MobileParty, Clan>("_actualClan");

        internal static readonly AccessTools.FieldRef<MBObjectBase, bool> IsRegistered =
            AccessTools.FieldRefAccess<MBObjectBase, bool>("<IsRegistered>k__BackingField");
        
        internal static readonly AccessTools.FieldRef<Hero, bool> HasMet =
            AccessTools.FieldRefAccess<Hero, bool>("_hasMet");
        
        internal static readonly AccessTools.FieldRef<Hero, HeroDeveloper> HeroDeveloperField =
            AccessTools.FieldRefAccess<Hero, HeroDeveloper>("_heroDeveloper");
        
        internal static readonly ConstructorInfo HeroDeveloperConstructor = AccessTools.Constructor(typeof(HeroDeveloper), [typeof(Hero)]);

        private static PartyUpgraderCampaignBehavior UpgraderCampaignBehavior;

        internal static void ReHome()
        {
            foreach (var BM in GetCachedBMs(true).WhereQ(p => p.Leader is not null))
                _bornSettlement(BM.Leader) = BM.HomeSettlement;
        }

        internal static bool TrySplitParty(MobileParty mobileParty)
        {
            if (MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent
                || mobileParty.Party.MemberRoster.TotalManCount / SplitDivisor - RemovedHero < Globals.Settings.MinPartySize
                || !mobileParty.IsBM()
                || mobileParty.IsTooBusyToMerge())
            {
                return false;
            }

            int roll = MBRandom.RandomInt(0, 101);
            if (roll > Globals.Settings.RandomSplitChance
                || mobileParty.Party.MemberRoster.TotalManCount > Math.Max(1, CalculatedMaxPartySize * ReductionFactor))
            {
                return false;
            }

            var party1 = TroopRoster.CreateDummyTroopRoster();
            var party2 = TroopRoster.CreateDummyTroopRoster();
            var prisoners1 = TroopRoster.CreateDummyTroopRoster();
            var prisoners2 = TroopRoster.CreateDummyTroopRoster();
            var inventory1 = new ItemRoster();
            var inventory2 = new ItemRoster();
            var heroes = mobileParty.Party.MemberRoster
                .RemoveIf(t => t.Character.IsHero)
                .Select(t => t.Character.HeroObject)
                .OrderByQ(h => -h.Power)
                .ToListQ();
            SplitRosters(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2);
            CreateSplitMilitias(mobileParty, party1, party2, prisoners1, prisoners2, inventory1, inventory2, heroes);
            return true;
        }

        private static void SplitRosters(MobileParty original, TroopRoster troops1, TroopRoster troops2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2)
        {
            foreach (var rosterElement in original.MemberRoster.GetTroopRoster().WhereQ(x => x.Character.HeroObject is null))
            {
                SplitRosters(troops1, troops2, rosterElement);
            }

            original.MemberRoster.Clear();

            if (original.PrisonRoster.TotalManCount > 0)
            {
                foreach (var rosterElement in original.PrisonRoster.GetTroopRoster().WhereQ(x => x.Character.HeroObject != Hero.MainHero))
                {
                    SplitRosters(prisoners1, prisoners2, rosterElement);
                }
            }
            
            original.PrisonRoster.Clear();

            foreach (var item in original.ItemRoster)
            {
                if (string.IsNullOrEmpty(item.EquipmentElement.Item?.Name?.ToString()))
                {
                    Logger.LogWarning("Bad item: " + item.EquipmentElement);
                    continue;
                }

                var half = Math.Max(1, item.Amount / 2);
                inventory1.AddToCounts(item.EquipmentElement, half);
                var remainder = item.Amount % 2;
                inventory2.AddToCounts(item.EquipmentElement, half + remainder);
            }
            
            original.ItemRoster.Clear();
        }

        private static void SplitRosters(TroopRoster roster1, TroopRoster roster2, TroopRosterElement rosterElement)
        {
            // toss a coin (to your Witcher)
            if (rosterElement.Number == 1)
            {
                if (MBRandom.RandomInt(0, 2) == 0)
                {
                    roster1.AddToCounts(rosterElement.Character, 1);
                }
                else
                {
                    roster2.AddToCounts(rosterElement.Character, 1);
                }
            }
            else
            {
                var half = Math.Max(1, rosterElement.Number / 2);
                roster1.AddToCounts(rosterElement.Character, half);
                var remainder = rosterElement.Number % 2;
                roster2.AddToCounts(rosterElement.Character, Math.Max(1, half + remainder));
            }
        }

        private static void CreateSplitMilitias(MobileParty original, TroopRoster party1, TroopRoster party2,
            TroopRoster prisoners1, TroopRoster prisoners2, ItemRoster inventory1, ItemRoster inventory2, List<Hero> heroes)
        {
            try
            {
                if (original?.IsActive != true || heroes == null || heroes.Count == 0)
                {
                    Logger.LogWarning($"Invalid split attempt: original={original?.StringId}, heroes={heroes?.Count}");
                    return;
                }

                for (int i = heroes.Count - 1; i >= 0; i--)
                {
                    TroopRoster targetParty = i % 2 == 0 ? party1 : party2;
                    targetParty.AddToCounts(heroes[i].CharacterObject, 1, true);
                }
                
                while (party1.TotalManCount < Globals.Settings.MinPartySize && party1.Count > 0)
                {
                    // using 1, not 0 because 0 is the BM hero
                    var troop = party1.GetCharacterAtIndex(MBRandom.RandomInt(0, party1.Count));
                    if (troop == null) break;
                    if (!IsRegistered(troop))
                        Meow();
                    party1.AddToCounts(troop, 1);
                }

                while (party2.TotalManCount < Globals.Settings.MinPartySize && party2.Count > 0)
                {
                    var troop = party2.GetCharacterAtIndex(MBRandom.RandomInt(0, party2.Count));
                    if (troop == null) break;
                    if (!IsRegistered(troop))
                        Meow();
                    party2.AddToCounts(troop, 1);
                }

                if (original.HomeSettlement == null)
                {
                    Logger.LogError("Original militia has no HomeSettlement");
                    Trash(original);
                    return;
                }

                var bm1 = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(original.HomeSettlement, heroes[0], original.ActualClan));
                var bm2 = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(original.HomeSettlement, heroes.Count >= 2 ? heroes[1] : null, original.ActualClan));
                var rosters1 = new[] { party1, prisoners1 };
                var rosters2 = new[] { party2, prisoners2 };
                InitMilitia(bm1, rosters1, original.Position);
                InitMilitia(bm2, rosters2, original.Position);

                bm1.GetBM().Avoidance = original.GetBM().Avoidance;
                bm2.GetBM().Avoidance = original.GetBM().Avoidance;
                Logger.LogDebug($"{original.Name}({original.StringId}) split into {bm1.Name}({bm1.StringId}) and {bm2.Name}({bm2.StringId})");
                ItemRoster(bm1.Party) = inventory1;
                ItemRoster(bm2.Party) = inventory2;
                bm1.Party.SetVisualAsDirty();
                bm2.Party.SetVisualAsDirty();
                Trash(original);
                DoPowerCalculations();
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error splitting {original?.StringId}");
                if (original?.IsActive == true) Trash(original);
            }
        }

        private static readonly List<string> verbotenParties = new()
        {
            "ebdi_deserters_party",
            "caravan_ambush_quest",
            "arzagos_banner_piece_quest_raider_party",
            "istiana_banner_piece_quest_raider_party",
            "rescue_family_quest_raider_party",
            "destroy_raiders_conspiracy_quest",
            "radagos_raider_party",
            "locate_and_rescue_traveller_quest_raider_party",
            "company_of_trouble",
            "villagers_of_landlord_needs_access_to_village_common_quest",
            //Calradia Expanded Kingdoms in 3.0.2
            "manhunter"
        };

        internal static bool IsAvailableBanditParty(MobileParty __instance)
        {
            return __instance.IsBandit
                   && __instance.CurrentSettlement is null
                   && __instance.MapEvent is null
                   && __instance.Party.MemberRoster.TotalManCount > 0
                   && !__instance.IsTooBusyToMerge()
                   && !__instance.IsUsedByAQuest()
                   && !verbotenParties.Contains(__instance.StringId);
        }

        private static bool CanMergeNow(MobileParty __instance)
        {
            return __instance.MapEvent is null
                   && __instance.Party.MemberRoster.TotalManCount > 0
                   && !__instance.IsUsedByAQuest();
        }

        internal static bool TryMergeParties(MobileParty mobileParty, MobileParty mergeTarget)
        {
            try
            {
                Logger.LogDebug($"TryMergeParties: START {mobileParty?.StringId} + {mergeTarget?.StringId}");

                if (mobileParty?.IsActive != true || mergeTarget?.IsActive != true)
                {
                    Logger.LogWarning("Merge cancelled: target parties invalid");
                    return false;
                }

                if (!CanMergeNow(mobileParty) || !CanMergeNow(mergeTarget))
                {
                    mobileParty?.SetMoveModeHold();
                    mergeTarget?.SetMoveModeHold();
                    Logger.LogDebug($"TryMergeParties: CanMergeNow failed");
                    return false;
                }

                Logger.LogDebug($"TryMergeParties: MergeRosters");
                var rosters = MergeRosters(mobileParty, mergeTarget);
                if (rosters == null || rosters.Length != 2)
                {
                    Logger.LogError("MergeRosters returned invalid rosters");
                    return false;
                }
                Logger.LogDebug($"TryMergeParties: rosters OK - members={rosters[0].TotalManCount}");

                Hero leaderHero = (mobileParty.LeaderHero?.Power ?? 0) >= (mergeTarget.LeaderHero?.Power ?? 0) ? mobileParty.LeaderHero : mergeTarget.LeaderHero;
                Logger.LogDebug($"TryMergeParties: leaderHero={leaderHero?.Name.ToString() ?? "NULL"}");

                Settlement mobilePartyHomeSettlement = mobileParty.HomeSettlement?.IsHideout ?? false ? mobileParty.HomeSettlement : null;
                Settlement mergeTargetHomeSettlement = mergeTarget.HomeSettlement?.IsHideout ?? false ? mergeTarget.HomeSettlement : null;
                Settlement bestSettlement = leaderHero?.HomeSettlement ?? (mobileParty.Party.EstimatedStrength > mergeTarget.Party.EstimatedStrength ? mobilePartyHomeSettlement ?? mergeTargetHomeSettlement : mergeTargetHomeSettlement ?? mobilePartyHomeSettlement);
                if (bestSettlement is null)
                    bestSettlement = Hideouts.OrderByQ(s => s.GatePosition.ToVec2().Distance(mobileParty.Position.ToVec2())).First();

                // Prefer a naval clan if either source party has one, so merged BMs
                // formed from sea raiders / pirates retain naval navigation capability
                var sourceClanWithShips = (mobileParty.ActualClan?.HasNavalNavigationCapability == true ? mobileParty.ActualClan : null)
                    ?? (mergeTarget.ActualClan?.HasNavalNavigationCapability == true ? mergeTarget.ActualClan : null);
                var mergedClan = sourceClanWithShips
                    ?? mobileParty.ActualClan
                    ?? mergeTarget.ActualClan
                    ?? Clan.BanditFactions.FirstOrDefault(c => c.Culture == bestSettlement.Culture)
                    ?? bestSettlement.OwnerClan;

                Logger.LogDebug($"TryMergeParties: bestSettlement={bestSettlement?.Name.ToString() ?? "NULL"}");

                Logger.LogDebug($"TryMergeParties: CreateParty");
                var bm = MobileParty.CreateParty("Bandit_Militia", new ModBanditMilitiaPartyComponent(bestSettlement, leaderHero, mergedClan));
                Logger.LogDebug($"TryMergeParties: CreateParty done, bm={bm?.StringId ?? "NULL"}, bm.IsActive={bm?.IsActive}");

                Logger.LogDebug($"TryMergeParties: ActualClan={bm.ActualClan?.Name.ToString() ?? "NULL"}");

                try
                {
                    Logger.LogDebug($"TryMergeParties: InitMilitia");
                    InitMilitia(bm, rosters, mobileParty.Position);
                    Logger.LogDebug($"TryMergeParties: InitMilitia done, bm.LeaderHero={bm.LeaderHero?.Name.ToString() ?? "NULL"}, bm.IsActive={bm.IsActive}");

                    // If the merged BM is at sea and has ships, correct DesiredAiNavigationType —
                    // InitializeMobilePartyAtPosition fires SetShortTermBehavior before ships exist,
                    // so a sea-positioned party with no ships yet gets NavigationType.None and
                    // becomes stuck. Explicitly set it to Naval now that ships are assigned.
                    if (bm.IsCurrentlyAtSea && bm.HasNavalNavigationCapability)
                        bm.DesiredAiNavigationType = MobileParty.NavigationType.Naval;
                    else if (!bm.IsCurrentlyAtSea)
                        bm.DesiredAiNavigationType = MobileParty.NavigationType.Default;

                    var calculatedAvoidance = new Dictionary<Hero, float>();
                    void CalcAverageAvoidance(ModBanditMilitiaPartyComponent BM)
                    {
                        foreach (var entry in BM.Avoidance)
                            if (!calculatedAvoidance.TryGetValue(entry.Key, out _))
                                calculatedAvoidance.Add(entry.Key, entry.Value);
                            else
                            {
                                calculatedAvoidance[entry.Key] += entry.Value;
                                calculatedAvoidance[entry.Key] /= 2;
                            }
                    }

                    if (mobileParty.PartyComponent is ModBanditMilitiaPartyComponent BM1)
                        CalcAverageAvoidance(BM1);
                    if (mergeTarget.PartyComponent is ModBanditMilitiaPartyComponent BM2)
                        CalcAverageAvoidance(BM2);

                    bm.GetBM().Avoidance = calculatedAvoidance;

                    if (Globals.Settings.TestingMode)
                    {
                        var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                        bm.Position = party.Position;
                    }

                    bm.Party.SetVisualAsDirty();
                    Logger.LogDebug($"TryMergeParties: SUCCESS - {bm.Name}({bm.StringId}) merged from {mobileParty.Name}({mobileParty.StringId}) and {mergeTarget.Name}({mergeTarget.StringId})");
                    Trash(mobileParty);
                    Trash(mergeTarget);
                    DoPowerCalculations();
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"TryMergeParties: INNER CATCH - InitMilitia or post-init failed");
                    if (mobileParty?.IsActive == true) Trash(mobileParty);
                    if (mergeTarget?.IsActive == true) Trash(mergeTarget);
                    Trash(bm);
                    throw;
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"TryMergeParties: OUTER CATCH - {mobileParty?.StringId} + {mergeTarget?.StringId}");
                try { if (mobileParty?.IsActive == true) Trash(mobileParty); } catch { }
                try { if (mergeTarget?.IsActive == true) Trash(mergeTarget); } catch { }
                return false;
            }
        }

        internal static TroopRoster[] MergeRosters(MobileParty sourceParty, MobileParty targetParty)
        {
            var outMembers = TroopRoster.CreateDummyTroopRoster();
            var outPrisoners = TroopRoster.CreateDummyTroopRoster();
            var members = new [] {sourceParty.MemberRoster, targetParty.MemberRoster};
            var prisoners = new [] { sourceParty.PrisonRoster, targetParty.PrisonRoster };

            foreach (var roster in members)
            {
                outMembers.Add(roster);
                roster.Clear();
            }

            foreach (var roster in prisoners)
            {
                outPrisoners.Add(roster);
                roster.Clear();
            }

            return [ outMembers, outPrisoners ];
        }

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
                mobileParty.IsActive = false;
                DestroyPartyAction.Apply(null, mobileParty);
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Error trashing {mobileParty.StringId}");
                try { mobileParty.IsActive = false; } catch { }
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
                RemoveBadItems(); // haven't determined if BM is causing these
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

        // deprecated with 3.9 but necessary to clean up older versions
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

            // still needed post 1.7?
            // prisoners somehow of settlements
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
                            //Debugger.Break();
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

                foreach (var mapEvent in eventsSnapshot)
                {
                    if (mapEvent is null || !mapEvents.Contains(mapEvent) || mapEvent.IsFinalized)
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
                                mobileParty.IsActive = false;
                                Logger.LogTrace($"Disabled BM party {mobileParty.StringId} after MapEvent finalization");
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
        {
            var verbotenItemsStringIds = new List<string>
            {
                "bound_adarga",
                "old_kite_sparring_shield_shoulder",
                "old_horsemans_kite_shield_shoulder",
                "old_horsemans_kite_shield",
                "banner_mid",
                "banner_big",
                "campaign_banner_small",
                "torch",
                "wooden_sword_t1",
                "wooden_sword_t2",
                "wooden_2hsword_t1",
                "practice_spear_t1",
                "horse_whip",
                "push_fork",
                "mod_banner_1",
                "mod_banner_2",
                "mod_banner_3",
                "throwing_stone",
                "ballista_projectile",
                "ballista_projectile_burning",
                "boulder",
                "pot",
                "grapeshot_stack",
                "grapeshot_fire_stack",
                "grapeshot_projectile",
                "grapeshot_fire_projectile",
                "oval_shield",
            };

            var verbotenSaddles = new List<string>
            {
                "celtic_frost",
                "saddle_of_aeneas",
                "fortunas_choice",
                "aseran_village_harness",
                "bandit_saddle_steppe",
                "bandit_saddle_desert"
            };

            Mounts = Items.All.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Horse)
                .WhereQ(i => !i.StringId.Contains("unmountable")).WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList();
            Saddles = Items.All.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.HorseHarness
                                            && !i.StringId.Contains("mule")
                                            && !verbotenSaddles.Contains(i.StringId)).WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList();
            var all = Items.All.WhereQ(i =>
                    !i.IsCraftedByPlayer
                    && i.ItemType is not (ItemObject.ItemTypeEnum.Goods
                        or ItemObject.ItemTypeEnum.Horse
                        or ItemObject.ItemTypeEnum.HorseHarness
                        or ItemObject.ItemTypeEnum.Animal
                        or ItemObject.ItemTypeEnum.Banner
                        or ItemObject.ItemTypeEnum.Book
                        or ItemObject.ItemTypeEnum.Invalid)
                    && i.ItemCategory.StringId != "garment"
                    && !i.StringId.EndsWith("blunt")
                    && !i.StringId.Contains("sparring"))
                .WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList();
            var runningCivilizedMod = AppDomain.CurrentDomain.GetAssemblies().AnyQ(a => a.FullName.Contains("Civilized"));
            if (!runningCivilizedMod)
            {
                all.RemoveAll(i => !i.IsCivilian);
            }

            all.RemoveAll(item => verbotenItemsStringIds.Contains(item.StringId));
            Arrows = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Arrows).WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList();
            Bolts = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Bolts).WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList();
            var oneHanded = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.OneHandedWeapon);
            var twoHanded = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.TwoHandedWeapon);
            var polearm = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Polearm);
            var thrown = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Thrown);
            var shields = all.WhereQ(i => i.ItemType == ItemObject.ItemTypeEnum.Shield);
            var bows = all.WhereQ(i => i.ItemType is ItemObject.ItemTypeEnum.Bow or ItemObject.ItemTypeEnum.Crossbow);
            var any = new List<ItemObject>(oneHanded.Concat(twoHanded).Concat(polearm).Concat(thrown).Concat(shields).Concat(bows).WhereQ(i => i.Value <= Globals.Settings.MaxItemValue).ToList());
            any.Do(i => EquipmentItems.Add(new EquipmentElement(i)));

            // used for armour
            foreach (ItemObject.ItemTypeEnum itemType in Enum.GetValues(typeof(ItemObject.ItemTypeEnum)))
            {
                ItemTypes[itemType] = Items.All.WhereQ(i =>
                    i.Type == itemType
                    && i.Value >= 1000
                    && i.Value <= Globals.Settings.MaxItemValue).ToList();
            }

            // front-load
            for (var i = 0; i < 10000; i++)
                BanditEquipment.Add(BuildViableEquipmentSet());
        }

        // builds a set of 4 weapons that won't include more than 1 bow or shield, nor any lack of ammo
        private static Equipment BuildViableEquipmentSet()
        {
            var gear = new Equipment();
            var haveShield = false;
            var haveBow = false;
            try
            {
                for (var slot = 0; slot < 4; slot++)
                {
                    EquipmentElement randomElement = default;
                    switch (slot)
                    {
                        case 0:
                        case 1:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                        case 2 when !gear[3].IsEmpty:
                            randomElement = EquipmentItems.WhereQ(x =>
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Bow &&
                                x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow).ToList().GetRandomElement();
                            break;
                        case 2:
                        case 3:
                            randomElement = EquipmentItems.GetRandomElement();
                            break;
                    }

                    if (randomElement.Item.HasArmorComponent)
                        ItemModifier(ref randomElement) = randomElement.Item.ArmorComponent.ItemModifierGroup?.ItemModifiers.GetRandomElementWithPredicate(i => i.PriceMultiplier > 1);

                    if (randomElement.Item.HasWeaponComponent)
                        ItemModifier(ref randomElement) = randomElement.Item.WeaponComponent.ItemModifierGroup?.ItemModifiers.GetRandomElementWithPredicate(i => i.PriceMultiplier > 1);

                    // matches here by obtaining a bow, which then stuffed ammo into [3]
                    if (slot == 3 && !gear[3].IsEmpty)
                        break;

                    if (randomElement.Item.ItemType is ItemObject.ItemTypeEnum.Bow or ItemObject.ItemTypeEnum.Crossbow)
                    {
                        if (slot < 3)
                        {
                            // try again, try harder
                            if (haveBow)
                            {
                                slot--;
                                continue;
                            }

                            haveBow = true;
                            gear[slot] = randomElement;
                            if (randomElement.Item.ItemType is ItemObject.ItemTypeEnum.Bow)
                                gear[3] = new EquipmentElement(Arrows.ToList()[MBRandom.RandomInt(0, Arrows.Count)]);
                            else if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Crossbow)
                                gear[3] = new EquipmentElement(Bolts.ToList()[MBRandom.RandomInt(0, Bolts.Count)]);
                            continue;
                        }

                        randomElement = EquipmentItems.WhereQ(x =>
                            x.Item.ItemType != ItemObject.ItemTypeEnum.Bow &&
                            x.Item.ItemType != ItemObject.ItemTypeEnum.Crossbow).ToList().GetRandomElement();
                    }

                    if (randomElement.Item.ItemType == ItemObject.ItemTypeEnum.Shield)
                    {
                        // try again, try harder
                        if (haveShield)
                        {
                            slot--;
                            continue;
                        }

                        haveShield = true;
                    }

                    gear[slot] = randomElement;
                }

                gear[5] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HeadArmor].GetRandomElement());
                gear[6] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.BodyArmor].GetRandomElement());
                gear[7] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.LegArmor].GetRandomElement());
                gear[8] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.HandArmor].GetRandomElement());
                gear[9] = new EquipmentElement(ItemTypes[ItemObject.ItemTypeEnum.Cape].GetRandomElement());
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error building equipment set.");
                Logger.LogError($"Armour loaded: {ItemTypes.Select(k => k.Value).Sum(v => v.Count)}\n\tNon-armour loaded: {EquipmentItems.Count}\n\tArrows:{Arrows.Count}\n\tBolts:{Bolts.Count}\n\tMounts: {Mounts.Count}\n\tSaddles: {Saddles.Count}");
            }

            var clone = gear.Clone();
            clone.SyncEquipments = true;
            return clone;
        }

        internal static void DoPowerCalculations(bool force = false)
        {
            if (force || LastCalculated < CampaignTime.Now.ToHours - 8)
            {
                var parties = MobileParty.All.WhereQ(p => p.LeaderHero is not null && !p.IsBM()).ToListQ();
                if (parties.Count == 0)
                {
                    Logger.LogWarning("No parties available for power calculations.");
                    return;
                }
                var medianSize = (float)parties
                    .OrderBy(p => p.MemberRoster.TotalManCount)
                    .ElementAt(parties.CountQ() / 2).MemberRoster.TotalManCount;

                CalculatedMaxPartySize = Math.Max(medianSize, Math.Max(1, MobileParty.MainParty.MemberRoster.TotalManCount) * Variance);
                LastCalculated = CampaignTime.Now.ToHours;
                CalculatedGlobalPowerLimit = parties.SumQ(p => p.Party.EstimatedStrength) * Variance;
                GlobalMilitiaPower = GetCachedBMs(true).SumQ(m => m.Party.EstimatedStrength);

                MilitiaPowerPercent = CalculatedGlobalPowerLimit > 0
                    ? GlobalMilitiaPower / CalculatedGlobalPowerLimit * 100 : 0;

                var bmCount = GetCachedBMs().CountQ();
                MilitiaPartyAveragePower = bmCount > 0 ? GlobalMilitiaPower / bmCount : 0;
            }
        }

        // leveraged to make looters convert into troop types from nearby cultures
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
            var highest = map.WhereQ(x => x.Value == maxValue).SelectQ(x => x.Key);
            var result = highest.ToList().GetRandomElement();
            return result;
        }

        internal static void PrintInstructionsAroundInsertion(List<CodeInstruction> codes, int insertPoint, int insertSize, int adjacentNum = 5)
        {
            Logger.LogTrace($"Inserting {insertSize} at {insertPoint}.");

            // in case insertPoint is near the start of the method's IL
            var adjustedAdjacent = codes.Count - adjacentNum >= 0 ? adjacentNum : Math.Max(0, codes.Count - adjacentNum);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // codes[266 - 5 + 0].opcode
                // codes[266 - 5 + 4].opcode
                Logger.LogTrace($"{codes[insertPoint - adjustedAdjacent + i].opcode,-10}{codes[insertPoint - adjustedAdjacent + i].operand}");
            }

            for (var i = 0; i < insertSize; i++)
            {
                Logger.LogTrace($"{codes[insertPoint + i].opcode,-10}{codes[insertPoint + i].operand}");
            }

            // in case insertPoint is near the end of the method's IL
            adjustedAdjacent = insertPoint + adjacentNum <= codes.Count ? adjacentNum : Math.Max(codes.Count, adjustedAdjacent);
            for (var i = 0; i < adjustedAdjacent; i++)
            {
                // 266 + 2 - 5 + 0
                // 266 + 2 - 5 + 4
                Logger.LogTrace($"{codes[insertPoint + insertSize + adjustedAdjacent + i].opcode,-10}{codes[insertPoint + insertSize + adjustedAdjacent + i].operand}");
            }
        }
        /*
        internal static void RemoveUndersizedTracker(MobileParty party)
        {
            if (!party.IsBM() || Globals.TrackerContainer is null)
                return;

            if (party.MemberRoster.TotalManCount < Globals.Settings.TrackedSizeMinimum)
            {
                var typeofTrackerContainer = AccessTools.TypeByName("TrackerContainer");
                var hasTrackerFor = AccessTools.Method(typeofTrackerContainer, "HasTrackerFor");
                if ((bool)hasTrackerFor.Invoke(Globals.TrackerContainer, new object[] { party }))
                {
                    AccessTools.Method(typeofTrackerContainer, "RemoveTracker")
                        .Invoke(Globals.TrackerContainer, new object[] { party });
                }
            }
        }

        internal static void RefreshTrackers()
        {
            if (Globals.MapTrackerProvider is null || Globals.TrackerContainer is null)
                return;

            var typeofTrackerContainer = AccessTools.TypeByName("TrackerContainer");
            var hasTrackerFor = AccessTools.Method(typeofTrackerContainer, "HasTrackerFor");
            var addTracker = AccessTools.Method(typeofTrackerContainer, "AddTracker");
            var removeTracker = AccessTools.Method(typeofTrackerContainer, "RemoveTracker");

            AccessTools.Method(typeof(MapTrackerProvider), "ResetTrackers")
                .Invoke(Globals.MapTrackerProvider, null);

            if (!Globals.Settings.Trackers)
            {
                PartyImageMap.Clear();
                return;
            }

            foreach (var bm in GetCachedBMs(true)
                .WhereQ(p => p.MobileParty?.IsActive == true
                    && p.MobileParty.MemberRoster.TotalManCount >= Globals.Settings.TrackedSizeMinimum))
            {
                if (!(bool)hasTrackerFor.Invoke(Globals.TrackerContainer, new object[] { bm.MobileParty }))
                {
                    addTracker.Invoke(Globals.TrackerContainer,
                        new object[] { new MapMobilePartyTrackItemVM(bm.MobileParty) });
                }
            }
        }
        */
        internal static int NumMountedTroops(TroopRoster troopRoster)
        {
            return troopRoster.GetTroopRoster().WhereQ(e => e.Character.Equipment[10].Item is not null).SumQ(e => e.Number);
        }

        internal static Hero CreateOrReuseHero(Settlement settlement)
        {
            if (settlement == null)
            {
                Logger.LogError("Cannot create hero: settlement is null");
                return null;
            }

            bool isReused = false;
            Hero hero = Hero.DeadOrDisabledHeroes
                .FirstOrDefault(h => h != null && h.IsBM() && !Heroes.Contains(h));

            if (hero != null)
            {
                isReused = true;
            }
            else
            {
                hero = CustomizedCreateHeroAtOccupation(settlement);
            }

            if (hero == null)
            {
                Logger.LogError("Failed to create or reuse hero");
                return null;
            }

            HasMet(hero) = false;
            hero.BornSettlement = settlement;
            hero.Clan = settlement?.OwnerClan;

            if (hero.Clan == null)
            {
                Logger.LogError($"Hero clan is null for settlement {settlement.Name}");
                return null;
            }

            hero.SupporterOf = settlement?.OwnerClan;

            string oldName = hero.Name?.ToString();
            NameGenerator.Current.GenerateHeroNameAndHeroFullName(hero, out TextObject firstName, out TextObject fullName, false);
            hero.SetName(fullName, firstName);

            // ResetEquipments() crashes in current game version (get_FirstStealthEquipment returns null).
            // For reused heroes, manually clear equipment slots before CreateHero reassigns them.
            if (isReused)
            {
                for (int i = 0; i < 12; i++)
                {
                    hero.BattleEquipment[i] = EquipmentElement.Invalid;
                    hero.CivilianEquipment[i] = EquipmentElement.Invalid;
                }
            }

            Logger.LogTrace($"{oldName} is reused as {hero}");

            HeroDeveloperField(hero) = HeroDeveloperConstructor.Invoke([hero]) as HeroDeveloper;
            hero.HeroDeveloper.InitializeHeroDeveloper();
            hero.AddDeathMark();
            hero.Initialize();
            hero.ChangeState(Hero.CharacterStates.Active);
            hero.EncyclopediaText = TextObject.GetEmpty().CopyTextObject();
            HeroAppearanceService.RandomizeAppearance(hero);
            return hero;
        }

        internal static Hero CreateHero(Settlement settlement)
        {
            var hero = CreateOrReuseHero(settlement);

            if (hero == null)
            {
                Logger.LogError($"Failed to create hero for settlement {settlement?.Name}");
                return null;
            }

            Heroes.Add(hero);

            EquipmentHelper.AssignHeroEquipmentFromEquipment(hero, BanditEquipment.GetRandomElement());

            if (Globals.Settings.CanTrain)
            {
                hero.HeroDeveloper.AddPerk(DefaultPerks.Leadership.VeteransRespect);
                hero.HeroDeveloper.AddSkillXp(DefaultSkills.Leadership, 150);
            }
            return hero;
        }

        internal static void RandomizeHeroAppearance(Hero hero)
        {
            HeroAppearanceService.RandomizeAppearance(hero);
        }

        // temporary "safety" bail-out, to be removed
        internal static void AdjustCavalryCount(TroopRoster troopRoster)
        {
            try
            {
                var safety = 0;
                while (safety++ < 200 && NumMountedTroops(troopRoster) - Convert.ToInt32(troopRoster.TotalManCount / 2) is var delta && delta > 0)
                {
                    var mountedTroops = troopRoster.GetTroopRoster().WhereQ(c =>
                        !c.Character.Equipment[10].IsEmpty
                        && !c.Character.IsHero
                        && c.Character.OriginalCharacter is null).ToListQ();
                    if (mountedTroops.Count == 0)
                        break;
                    if (safety == 200)
                    {
                        InformationManager.DisplayMessage(new InformationMessage("Bandit Militias error.  Please open a bug report and include the file cavalry.txt from the mod folder.", new Color(1, 0, 0)));
                        var output = new StringBuilder();
                        output.AppendLine($"NumMountedTroops(troopRoster) {NumMountedTroops(troopRoster)} - Convert.ToInt32(troopRoster.TotalManCount / 2) {Convert.ToInt32(troopRoster.TotalManCount / 2)}");
                        mountedTroops.Do(t => output.AppendLine($"{t.Character}: {t.Number} ({t.WoundedNumber})"));
                        File.WriteAllText(ModuleHelper.GetModuleFullPath("BanditMilitias") + "cavalry.txt", output.ToString());
                    }

                    var element = mountedTroops.GetRandomElement();
                    var count = MBRandom.RandomInt(1, delta + 1);
                    count = Math.Min(element.Number, count);
                    troopRoster.AddToCounts(element.Character, -count);
                }
            }
            catch (Exception ex)
            {
                InformationManager.DisplayMessage(new InformationMessage("Problem adjusting cavalry count, please open a bug report."));
                Logger.LogError(ex, "Error adjusting cavalry count.");
            }
        }

        private static void ConfigureMilitia(MobileParty mobileParty)
        {
            Logger.LogDebug($"ConfigureMilitia: START {mobileParty?.StringId}, LeaderHero={mobileParty?.LeaderHero?.Name.ToString() ?? "NULL"}, GetBM().Leader={mobileParty?.GetBM()?.Leader?.Name.ToString() ?? "NULL"}");

            mobileParty.LeaderHero.Gold = Convert.ToInt32(mobileParty.Party.EstimatedStrength * GoldMap.ElementAt(Globals.Settings.GoldReward.SelectedIndex).Value);
            Logger.LogDebug($"ConfigureMilitia: Gold set");

            CharacterObject leaderCharacterObject = mobileParty.GetBM().Leader.CharacterObject;
            Logger.LogDebug($"ConfigureMilitia: leaderCharacterObject={leaderCharacterObject?.StringId ?? "NULL"}");

            if (mobileParty.MemberRoster.Contains(leaderCharacterObject))
                mobileParty.MemberRoster.RemoveTroop(leaderCharacterObject);
            mobileParty.MemberRoster.AddToCounts(leaderCharacterObject, 1, true);
            Logger.LogDebug($"ConfigureMilitia: roster updated");

            if (MBRandom.RandomInt(0, 2) == 0)
            {
                var mount = Mounts.GetRandomElement();
                mobileParty.GetBM().Leader.BattleEquipment[10] = new EquipmentElement(mount);
                if (mount.HorseComponent.Monster.MonsterUsage == "camel")
                    mobileParty.GetBM().Leader.BattleEquipment[11] = new EquipmentElement(Saddles.WhereQ(saddle =>
                        saddle.StringId.Contains("camel")).ToList().GetRandomElement());
                else
                    mobileParty.GetBM().Leader.BattleEquipment[11] = new EquipmentElement(Saddles.WhereQ(saddle =>
                        !saddle.StringId.Contains("camel")).ToList().GetRandomElement());
            }
            Logger.LogDebug($"ConfigureMilitia: DONE");
        }

        internal static void TrainMilitia(MobileParty mobileParty)
        {
            try
            {
                if (!Globals.Settings.CanTrain || MilitiaPowerPercent > Globals.Settings.GlobalPowerPercent)
                    return;

                int iterations = default;
                switch (Globals.Settings.XpGift.SelectedValue)
                {
                    case "Off":
                        break;
                    case "Normal":
                        iterations = 1;
                        break;
                    case "Hard":
                        iterations = 2;
                        break;
                    case "Hardest":
                        iterations = 4;
                        break;
                }

                int number, numberToUpgrade;
                if (Globals.Settings.LooterUpgradePercent > 0)
                {
                    // upgrade any looters first, then go back over and iterate further upgrades
                    var allLooters = mobileParty.MemberRoster.GetTroopRoster().WhereQ(e => e.Character == Looters.BasicTroop).ToList();
                    if (allLooters.Any())
                    {
                        var culture = GetMostPrevalentFromNearbySettlements(mobileParty.Position.ToVec2());
                        foreach (var looter in allLooters)
                        {
                            number = looter.Number;
                            numberToUpgrade = Convert.ToInt32(number * Globals.Settings.LooterUpgradePercent / 100f);
                            if (numberToUpgrade == 0)
                                continue;

                            mobileParty.MemberRoster.AddToCounts(Globals.Looters.BasicTroop, -numberToUpgrade);
                            var recruit = Globals.Recruits[culture][MBRandom.RandomInt(0, Globals.Recruits[culture].Count)];
                            mobileParty.MemberRoster.AddToCounts(recruit, numberToUpgrade);
                        }
                    }
                }

                var troopUpgradeModel = Campaign.Current.Models.PartyTroopUpgradeModel;
                for (var i = 0; i < iterations && Globals.MilitiaPowerPercent <= Globals.Settings.GlobalPowerPercent; i++)
                {
                    var validTroops = mobileParty.MemberRoster.GetTroopRoster().WhereQ(e =>
                        e.Character.Tier < Globals.Settings.MaxTrainingTier
                        && !e.Character.IsHero
                        && !e.Character.Name.ToString().StartsWith("Glorious")
                        && troopUpgradeModel.IsTroopUpgradeable(mobileParty.Party, e.Character));
                    var validTroopsList = validTroops.ToList();
                    if (validTroopsList.Count == 0)
                        break;

                    var troopToTrain = validTroopsList.GetRandomElement();
                    number = troopToTrain.Number;
                    if (number < 1)
                    {
                        continue;
                    }

                    var minNumberToUpgrade = Convert.ToInt32(Globals.Settings.UpgradeUnitsPercent * 0.01f * number * MBRandom.RandomFloat);
                    minNumberToUpgrade = Math.Max(1, minNumberToUpgrade);
                    numberToUpgrade = Convert.ToInt32((number + 1) / 2f);
                    numberToUpgrade = numberToUpgrade > minNumberToUpgrade ? Convert.ToInt32(MBRandom.RandomInt(minNumberToUpgrade, numberToUpgrade)) : minNumberToUpgrade;
                    //Log.Debug?.Log($"^^^ {mobileParty.LeaderHero.Name} is training up to {numberToUpgrade} of {number} \"{troopToTrain.Character.Name}\".");
                    var xpGain = numberToUpgrade * DifficultyXpMap.ElementAt(Globals.Settings.XpGift.SelectedIndex).Value;
                    mobileParty.MemberRoster.AddXpToTroop(troopToTrain.Character, xpGain);
                    UpgraderCampaignBehavior ??= Campaign.Current.GetCampaignBehavior<PartyUpgraderCampaignBehavior>();
                    UpgraderCampaignBehavior.UpgradeReadyTroops(mobileParty.Party);
                    if (Globals.Settings.TestingMode)
                    {
                        var party = Hero.MainHero.PartyBelongedTo ?? Hero.MainHero.PartyBelongedToAsPrisoner.MobileParty;
                        mobileParty.Position = party.Position;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Bandit Militias is failing to configure parties!");
                Trash(mobileParty);
            }
        }

        internal static IEnumerable<ModBanditMilitiaPartyComponent> GetCachedBMs(bool forceRefresh = false)
        {
            if (forceRefresh || PartyCacheInterval < CampaignTime.Now.ToHours - 1)
            {
                PartyCacheInterval = CampaignTime.Now.ToHours;
                AllBMs = MobileParty.AllBanditParties
                    .WhereQ(m => m != null && m.IsActive && m.IsBM())
                    .SelectQ(m => m.PartyComponent as ModBanditMilitiaPartyComponent)
                    .WhereQ(c => c != null)
                    .ToListQ();
            }

            return AllBMs ?? new List<ModBanditMilitiaPartyComponent>();
        }

        internal static void InitMilitia(MobileParty militia, TroopRoster[] rosters, CampaignVec2 position)
        {
            try
            {
                //var index = Globals.MapMobilePartyTrackerVM.Trackers.FindIndexQ(tracker => tracker.TrackedParty == militia);
                //if (index >= 0)
                //    Globals.MapMobilePartyTrackerVM.Trackers.RemoveAt(index);
                if (militia.ActualClan?.HasNavalNavigationCapability == true
                    && militia.Party.Ships.Count == 0)
                {
                    foreach (var stack in militia.ActualClan.DefaultPartyTemplate.ShipHulls)
                    {
                        int count = MBRandom.RandomInt(stack.MinValue, stack.MaxValue + 1);
                        for (int i = 0; i < count; i++)
                            new Ship(stack.ShipHull) { Owner = militia.Party };
                    }
                    Logger.LogTrace($"{militia.Name}({militia.StringId}) assigned {militia.Party.Ships.Count} ships.");
                }

                militia.InitializeMobilePartyAtPosition(rosters[0], rosters[1], position);
                ConfigureMilitia(militia);
                TrainMilitia(militia);

                Logger.LogTrace($"{militia.Name}({militia.StringId})[{militia.ActualClan}] initialized with {militia.MemberRoster.TotalRegulars} troops and {militia.MemberRoster.TotalHeroes} heroes. [{militia.MemberRoster.GetTroopRoster()
                    .WhereQ(t => t.Character.IsHero)
                    .SelectQ(t => t.Character.HeroObject.Name.ToString()).Join()}]");
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to initialize militia {militia?.StringId}");
                if (militia?.IsActive == true)
                    Trash(militia);
                throw;
            }
        }

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
            foreach (var loserHero in loserHeroes)
            {
                if (mep.Party.MobileParty.GetBM().Avoidance.TryGetValue(loserHero, out _))
                    mep.Party.MobileParty.GetBM().Avoidance[loserHero] -= MilitiaBehavior.Increment;
                else
                    mep.Party.MobileParty.GetBM().Avoidance.Add(loserHero, MBRandom.RandomInt(15, 35));
            }
        }

        private static Hero CustomizedCreateHeroAtOccupation(Settlement settlement)
        {
            var maxWeight = 0;
            foreach (var characterObject in Globals.HeroTemplates)
            {
                var num = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                maxWeight += num > 0 ? num : 100;
            }

            if (maxWeight == 0)
            {
                Logger.LogWarning("No hero templates available for weighted selection.");
                return null;
            }

            var template = (CharacterObject)null;
            var num1 = settlement.RandomInt(1, maxWeight + 1);

            foreach (var characterObject in Globals.HeroTemplates)
            {
                var num2 = characterObject.GetTraitLevel(DefaultTraits.Frequency) * 10;
                num1 -= num2 > 0 ? num2 : 100;
                if (num1 < 0)
                {
                    template = characterObject;
                    break;
                }
            }

            // Fallback: RandomInt upper bound edge case can leave template null
            template ??= Globals.HeroTemplates.GetRandomElement();

            if (template is null)
            {
                Logger.LogWarning("Could not select a hero template.");
                return null;
            }

            var specialHero = HeroCreator.CreateSpecialHero(template, settlement, settlement.OwnerClan, settlement.OwnerClan);

            if (specialHero is null)
            {
                Logger.LogWarning($"CreateSpecialHero returned null for template {template} at {settlement?.Name}");
                return null;
            }

            specialHero.AddPower(MBRandom.RandomFloat * 20f);
            specialHero.ChangeState(Hero.CharacterStates.Active);
            GiveGoldAction.ApplyBetweenCharacters(null, specialHero, 10000, true);
            specialHero.SupporterOf = specialHero.Clan;
            specialHero.IsFemale = RollFemale();

            Logger.LogTrace($"Created a new hero {specialHero}");

            return specialHero;
        }

        private static bool RollFemale()
        {
            return MBRandom.RandomInt(0, 100) < Globals.Settings.FemaleSpawnChance;
        }

        internal static void InitMap()
        {
            T.Restart();
            Logger.LogTrace("MapScreen.OnInitialize");
            ClearGlobals();
            PopulateItems();
            Looters = Clan.BanditFactions.First(c => c.StringId == "looters");
            Wights = Clan.BanditFactions.FirstOrDefaultQ(c => c.StringId == "wights"); // ROT
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
                "_basic_root", // MyLittleWarband StringIds
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
            //RefreshTrackers();
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
