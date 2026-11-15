using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BanditMilitiasRedux.Behaviors;
using BanditMilitiasRedux.Data;
using BanditMilitiasRedux.Diagnostics;
using BanditMilitiasRedux.Managers;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Conversation;
using TaleWorlds.CampaignSystem.GameMenus;
using TaleWorlds.CampaignSystem.Map;
using TaleWorlds.CampaignSystem.MapEvents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.LinQuick;
using TaleWorlds.ObjectSystem;
using static BanditMilitiasRedux.Core.Globals;

namespace BanditMilitiasRedux.Core;

internal static class Helper
{
    private const string LordPretalkToken = "lord_pretalk";
    private const string LordTokenPrefix = "lord_";
    private const string LordTokenSuffix = "_lord";
    private const string QuestIdPart = "quest";
    private const string RadagosIdPart = "radagos";
    private const string TutorialIdPart = "tutorial";
    private const string TierOneRecruitSuffix = "_tier_1";
    private const string StoryModeQuestRaiderId = "storymode_quest_raider";
    private static readonly string[] RecruitExcludedIdParts = ["regular_fighter", "veteran_borrowed_troop", "_basic_root", "_elite_root"];

    internal static readonly AccessTools.FieldRef<MobileParty, bool> IsBandit =
        AccessTools.FieldRefAccess<MobileParty, bool>("<IsBandit>k__BackingField");

    private static readonly MethodInfo CreateRandomBannerInternal = AccessTools.Method("TaleWorlds.Core.Banner:CreateRandomBannerInternal");

#if !LOWER_THAN_1_5
    // v1.5.7: the second parameter became the private enum Banner.BannerIconOrientation (None = -1).
    private static readonly object RandomBannerOrientation = Enum.ToObject(CreateRandomBannerInternal.GetParameters()[1].ParameterType, -1);
#endif

    // Null when there is neither a hideout home nor any land hideout on the map: the caller skips the merge.
    internal static Settlement? BestSettlementForMerge(MobileParty mergeInitiatorParty, MobileParty mergeTargetParty)
    {
        Settlement? mobilePartyHomeSettlement = mergeInitiatorParty.HomeSettlement?.IsHideout ?? false ? mergeInitiatorParty.HomeSettlement : null;
        Settlement? mergeTargetHomeSettlement = mergeTargetParty.HomeSettlement?.IsHideout ?? false ? mergeTargetParty.HomeSettlement : null;

        Settlement? bestSettlement = new[] { mobilePartyHomeSettlement, mergeTargetHomeSettlement }
            .FirstOrDefault(settlement => settlement != null && IsLandHideout(settlement));

        return bestSettlement ?? FindClosestHideout(mergeInitiatorParty.Position.ToVec2());
    }

    private static Settlement? FindClosestHideout(Vec2 position)
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

    private static bool IsLandHideout(Settlement hideout) => !hideout.StringId.StartsWith(Balance.SeasideHideoutPrefix);

    internal static void TryTrashMobilePartySafelyReservingBanditLeader(MobileParty? mobileParty)
    {
        if (mobileParty is null || !mobileParty.IsActive)
            return;

        if (mobileParty.MapEvent is { IsFinalized: false })
        {
            if (!DeferredTrashParties.Contains(mobileParty))
                DeferredTrashParties.Add(mobileParty);
            return;
        }

        Hero? leaderToReserve = mobileParty.LeaderHero;

        if (leaderToReserve is { IsAlive: true, IsPrisoner: false } && leaderToReserve.PartyBelongedTo == mobileParty)
            ReusableHeroesBehavior.AddHeroToTheWaitingDictionary(leaderToReserve);

        mobileParty.Ai?.DisableAi();
        DestroyPartyAction.Apply(null, mobileParty);
    }

    internal static void DrainDeferredTrashParties()
    {
        for (int i = DeferredTrashParties.Count - 1; i >= 0; i--)
        {
            MobileParty party = DeferredTrashParties[i];

            if (party is null || !party.IsActive)
            {
                DeferredTrashParties.RemoveAt(i);
                continue;
            }

            if (party.MapEvent is { IsFinalized: false })
                continue;

            DeferredTrashParties.RemoveAt(i);
            TryTrashMobilePartySafelyReservingBanditLeader(party);
        }
    }

    // Ctrl+Alt+N: asks first, because the removal cannot be undone.
    internal static void AskToNukeEverything()
    {
        if (IsNukeInquiryOpen)
            return;

        IsNukeInquiryOpen = true;
        InformationManager.ShowInquiry(new InquiryData(Texts.AreYouSure.ToString(), Texts.NukeConfirmation.ToString(), true, true,
            Texts.Yes.ToString(), Texts.No.ToString(), ConfirmNukeEverything, CancelNukeEverything), true);
    }

    private static void ConfirmNukeEverything()
    {
        IsNukeInquiryOpen = false;
        if (Campaign.Current is not null)
            NukeEverything();
    }

    private static void CancelNukeEverything() => IsNukeInquiryOpen = false;

    private static void NukeEverything()
    {
        if (Settlement.CurrentSettlement == null)
            GameMenu.ExitToLast();

        Campaign.Current.TimeControlMode = CampaignTimeControlMode.Stop;
        PartyImageMap.Clear();
        FlushMapEvents();
        LegacyFlushBanditMilitias();

        IReadOnlyList<BanditMilitiaPartyComponent?> militias = PowerCalculationManager.GetActiveBanditMilitiaParties(true);
        for (int i = 0; i < militias.Count; i++)
            TryTrashMobilePartySafelyReservingBanditLeader(militias[i]?.MobileParty);

        foreach (Hero hero in Hero.FindAll(hero => hero.IsBanditMilitiaHero()).ToArrayQ())
            KillCharacterAction.ApplyByRemove(hero);

        foreach (Hero hero in AllAliveBanditMilitiaHeroes.ToArrayQ())
            KillCharacterAction.ApplyByRemove(hero);

        foreach (Hero hero in Hero.AllAliveHeroes.WhereQ(IsOrphanedBanditMilitiaHero).ToArrayQ())
            KillCharacterAction.ApplyByRemove(hero);

        AllAliveBanditMilitiaHeroes.Clear();
        InformationManager.DisplayMessage(new InformationMessage(Texts.MilitiasCleared.ToString()));
    }

    private static void LegacyFlushBanditMilitias()
    {
        List<MobileParty> parties = Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value
            .WhereQ(m => m.IsBanditMilitiaParty()).ToListQ();
        if (parties.Count > 0)
        {
#if LOWER_THAN_1_5
            Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value =
                Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value.Except(parties).ToListQ();
#else
            // v1.5.7: the field is an MBList, so a plain List cannot be assigned to it; remove the parties in place instead.
            Traverse.Create(Campaign.Current.CampaignObjectManager).Field<List<MobileParty>>("_partiesWithoutPartyComponent").Value.RemoveAll(parties.Contains);
#endif
            foreach (MobileParty mobileParty in parties)
            {
                try
                {
                    TryTrashMobilePartySafelyReservingBanditLeader(mobileParty);
                }
                catch (Exception ex)
                {
                    BmrLog.Error("NukeEverything: could not remove legacy party " + mobileParty.StringId, ex);
                }
            }
        }

        foreach (Settlement settlement in Settlement.All
                     .WhereQ(s => s.Party.PrisonRoster.GetTroopRoster()
                         .AnyQ(e => e.Character.StringId.EndsWith(Balance.BanditMilitiaPartyStringId))))
        {
            for (int i = settlement.Party.PrisonRoster.Count - 1; i >= 0; i--)
                try
                {
                    CharacterObject prisoner = settlement.Party.PrisonRoster.GetCharacterAtIndex(i);
                    if (prisoner.StringId.EndsWith(Balance.BanditMilitiaPartyStringId))
                    {
                        settlement.Party.PrisonRoster.AddToCounts(prisoner, -1);
                        if (prisoner.HeroObject is not null)
                            KillCharacterAction.ApplyByRemove(prisoner.HeroObject);
                    }
                }
                catch (Exception ex)
                {
                    BmrLog.Error("NukeEverything: could not remove a legacy prisoner in " + settlement.StringId, ex);
                }
        }

        List<Hero> leftovers = Hero.AllAliveHeroes.WhereQ(h => h.StringId.EndsWith(Balance.BanditMilitiaPartyStringId)).ToListQ();
        foreach (Hero hero in leftovers)
        {
            KillCharacterAction.ApplyByRemove(hero);
        }
    }

    private static void FlushMapEvents()
    {
        try
        {
            List<MapEvent> mapEvents = Traverse.Create(Campaign.Current.MapEventManager)
                .Field<List<MapEvent>>("_mapEvents").Value;

            List<MapEvent> eventsSnapshot = mapEvents.ToListQ();
            HashSet<MapEvent> mapEventsSet = new(mapEvents);

            foreach (MapEvent mapEvent in eventsSnapshot)
            {
                if (mapEvent is null || !mapEventsSet.Contains(mapEvent) || mapEvent.IsFinalized)
                    continue;

                bool hasBanditMilitiaParties = mapEvent.InvolvedParties
                    .AnyQ(p => p?.IsMobile == true && p.MobileParty?.IsBanditMilitiaParty() == true);

                if (!hasBanditMilitiaParties)
                    continue;

                try
                {
                    List<MobileParty> banditMilitiaPartiesToClean = mapEvent.InvolvedParties
                        .WhereQ(p => p?.IsMobile == true && p.MobileParty?.IsBanditMilitiaParty() == true)
                        .SelectQ(p => p.MobileParty)
                        .WhereQ(m => m != null)
                        .ToListQ();

                    Traverse.Create(mapEvent).Field<MapEventState>("_state").Value = MapEventState.Wait;
                    mapEvent.FinalizeEvent();

                    foreach (MobileParty mobileParty in banditMilitiaPartiesToClean)
                    {
                        if (mobileParty?.IsActive == true)
                            TryTrashMobilePartySafelyReservingBanditLeader(mobileParty);
                    }
                }
                catch (Exception ex)
                {
                    BmrLog.Error("NukeEverything: could not finish a map event with Bandit Militias", ex);
                }
            }
        }
        catch (Exception ex)
        {
            BmrLog.Error("NukeEverything: could not read the map events", ex);
        }
    }

    // Militia heroes are born from a bandit troop template; bandit heroes of other mods made any other way are left alone.
    private static bool IsOrphanedBanditMilitiaHero(Hero hero)
        => hero is { IsAlive: true, IsNotable: false, IsSpecial: false, IsPlayerCompanion: false }
           && hero != Hero.MainHero
           && hero.Clan?.IsBanditFaction == true
           && hero.Occupation == Occupation.Bandit
           && hero.CharacterObject is not null
           && HeroTemplates.Contains(hero.CharacterObject.OriginalCharacter)
           && !hero.CharacterObject.StringId.Contains(RadagosIdPart)
           && !hero.CharacterObject.StringId.Contains(TutorialIdPart)
           && !hero.CharacterObject.StringId.Contains(QuestIdPart);

    // Daily (looter conversion): loops only, no LINQ.
    internal static CultureObject GetMostPrevalentFromNearbySettlements(Vec2 position)
    {
        LocatableSearchData<Settlement> locatableSearchData = Settlement.StartFindingLocatablesAroundPosition(position, Balance.CultureSearchRadius);
        Dictionary<CultureObject, int> map = new();
        for (Settlement settlement = Settlement.FindNextLocatable(ref locatableSearchData);
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
            map.Remove(BlackFlag);

        if (map.Count == 0)
            return MBObjectManager.Instance.GetObject<CultureObject>(Balance.FallbackCultureId);

        int maxValue = int.MinValue;
        foreach (KeyValuePair<CultureObject, int> pair in map)
        {
            if (pair.Value > maxValue)
                maxValue = pair.Value;
        }

        List<CultureObject> highest = new(map.Count);
        foreach (KeyValuePair<CultureObject, int> pair in map)
        {
            if (pair.Value == maxValue)
                highest.Add(pair.Key);
        }
        return highest[MBRandom.RandomInt(0, highest.Count)];
    }

    internal static void InitializeGlobalModState()
    {
        ClearGlobals();
        if (Banners.Count == 0)
            CacheBanners();
        EquipmentPool.Populate();
        BlackFlag = MBObjectManager.Instance.GetObject<CultureObject>(Balance.BlackFlagCultureId);
        Looters = Clan.BanditFactions.First(c => c.StringId == Balance.LootersClanId);
        Hideouts = Settlement.All.WhereQ(s => s.IsHideout).ToListQ();
        RaidCap = CalculateRaidCap();
        HeroTemplates = CharacterObject.All
            .WhereQ(c => c.Occupation is Occupation.Bandit)
            .WhereQ(c => !c.StringId.Contains(QuestIdPart) && !c.StringId.Contains(RadagosIdPart))
            .ToListQ();

        Dictionary<string, int> stateMap = AccessTools.FieldRefAccess<ConversationManager, Dictionary<string, int>>("stateMap")(Campaign.Current.ConversationManager);
        // lord_pretalk is the shared "back to hub" token; vanilla routes it unconditionally to hero_main_options
        LordConversationTokens = [.. stateMap.Keys
            .WhereQ(k => k != LordPretalkToken && (k.Contains(LordTokenPrefix) || k.Contains(LordTokenSuffix)))
            .SelectQ(k => stateMap[k])];

        IEnumerable<CharacterObject> allRecruits = CharacterObject.All.WhereQ(c =>
            c.Level == Balance.RecruitLevel
            && c.Occupation == Occupation.Soldier
            && RecruitExcludedIdParts.All(s => !c.StringId.Contains(s))
            && !c.StringId.EndsWith(TierOneRecruitSuffix)
            && !c.Name.ToString().StartsWith(Balance.GloriousTroopNamePrefix));

        foreach (CharacterObject recruit in allRecruits)
        {
            if (Recruits.ContainsKey(recruit.Culture))
                Recruits[recruit.Culture].Add(recruit);
            else
                Recruits.Add(recruit.Culture, [recruit]);
        }

        BanditTroopsByTier = [];
        foreach (CharacterObject troop in CharacterObject.All.WhereQ(c => c.Occupation is Occupation.Bandit && c is { IsHero: false, HiddenInEncyclopedia: false } && c.StringId != StoryModeQuestRaiderId))
        {
            if (!BanditTroopsByTier.TryGetValue(troop.Tier, out Dictionary<FormationClass, List<CharacterObject>>? byFormation))
                BanditTroopsByTier[troop.Tier] = byFormation = [];

            if (!byFormation.TryGetValue(troop.DefaultFormationClass, out List<CharacterObject>? list))
                byFormation[troop.DefaultFormationClass] = list = [];

            list.Add(troop);
        }

        Villages = Settlement.All.WhereQ(s => s.IsVillage).ToListQ();

        PowerCalculationManager.DoPowerCalculations(true);
        IsGlobalModStateInitialized = true;
    }

    private static void CacheBanners()
    {
        object[] args = new object[2];
        Banners.Capacity = Math.Max(Banners.Capacity, Balance.BannerCacheSize);
        for (int i = 0; i < Balance.BannerCacheSize; i++)
        {
            args[0] = MBRandom.RandomInt(0, int.MaxValue);
#if LOWER_THAN_1_5
            args[1] = -1;
#else
            args[1] = RandomBannerOrientation;
#endif
            if (CreateRandomBannerInternal.Invoke(null, args) is Banner banner)
                Banners.Add(banner);
        }
    }

    internal static PartyBase? FirstBanditMilitiaPartyOnSide(MapEvent mapEvent, BattleSideEnum side)
    {
        return mapEvent.PartiesOnSide(side)
            .FirstOrDefaultQ(eventParty => eventParty?.Party?.IsMobile == true
                && eventParty.Party.MobileParty?.IsBanditMilitiaParty() == true)?.Party;
    }

    internal static Settlement? FindNearestTown(Vec2 anchorPos)
    {
        Settlement? nearest = null;
        float nearestDistSq = float.MaxValue;
        LocatableSearchData<Settlement> search = Settlement.StartFindingLocatablesAroundPosition(anchorPos, Balance.SettlementFindRange);
        for (Settlement settlement = Settlement.FindNextLocatable(ref search); settlement != null; settlement = Settlement.FindNextLocatable(ref search))
        {
            if (!settlement.IsTown) continue;
            float distanceSq = settlement.GatePosition.ToVec2().DistanceSquared(anchorPos);
            if (distanceSq < nearestDistSq) { nearestDistSq = distanceSq; nearest = settlement; }
        }
        return nearest;
    }

    internal static void GrantDefeatBounty(Hero defeatedLeader, float militiaStrength)
    {
        if (defeatedLeader is null || Hero.MainHero is null)
            return;

        float rewardShare = militiaStrength <= 0f
            ? 0f
            : Math.Min(1f, militiaStrength / Balance.BountyStrengthForMaxReward);

        int gold = (int)(Balance.BountyGoldMin + rewardShare * (Balance.BountyGoldMax - Balance.BountyGoldMin));
        int renown = (int)Math.Round(Balance.BountyRenownMin + rewardShare * (Balance.BountyRenownMax - Balance.BountyRenownMin));
        if (renown < Balance.BountyRenownMin)
            renown = Balance.BountyRenownMin;

        GiveGoldAction.ApplyBetweenCharacters(null, Hero.MainHero, gold, disableNotification: true);
        if (renown > 0)
            GainRenownAction.Apply(Hero.MainHero, renown);

        InformationManager.DisplayMessage(new InformationMessage(Texts.Bounty(gold, renown, defeatedLeader.Name).ToString()));
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

    internal static int CalculateRaidCap()
    {
        float baseRaidCap = Settlement.FindAll(s => s.IsVillage).CountQ() / Balance.RaidCapVillagesPerRaid;
        return Math.Max(1, Convert.ToInt32(baseRaidCap * (ModSettings.RaidCapPercent / Balance.PercentDivisor)));
    }
}
