## BMR v1.5.7.3

- Stuck militia heroes: `bmr.heroes` after 39 days showed 8 heroes alive, free, in no party and not waiting. They count toward the Hero Pool Cap and block new leaders;
  `ReclaimOrphanedHeroes` put them back only on the next load.
  - Vanilla: `MobileParty.RemoveParty` -> `MemberRoster.Clear()` detaches every hero still in the roster (`PartyBelongedTo = null`) without any mod handling.
    `OnMobilePartyDestroyed` fires before that, so the check sits there.
  - Found (1.5.7 log, 42 days): 23 `BMR-HERO-LEAK`, 24 militias defeated. Always the defeated militia's own leader, never a member (both suspects ruled out).
    Path: `MapEventCalculateAndCommitMapEventResultsPatch.Postfix` -> `TryTrashMobilePartySafelyReservingBanditLeader` (deferred, battle not finalized) ->
    `OnMapEventEnded` -> `DrainDeferredTrashParties` -> `DestroyPartyAction`. The leader is still in `MemberRoster` but `LeaderHero` is null, so both leader checks skip him.
    Likely 1.5.7 `Hero.SetPartyBelongedTo` -> `ChangePartyLeader(null)` (see the merge crash entry); `MobilePartyRemovePartyLeaderPatch` does not see it.
  - Fix: new `ReusableHeroesBehavior.AddPartyHeroesToTheWaitingDictionary`, called by `Helper.TryTrashMobilePartySafelyReservingBanditLeader` and
    `MilitiaBehavior.OnMobilePartyDestroyed`. All versions (shared code, no `#if`): every alive, free militia hero still in `MemberRoster` waits, not only `LeaderHero`.
  - Found with temporary diagnostics (per-hero history, `BMR-HERO-LEAK` / `BMR-HERO-STUCK`, a daily reclaim with LoggingEnabled), removed after the fix.
    1.5.7 retest, 43 days, 32 militias defeated: 0 leaks, 0 stuck.
  - `ReclaimOrphanedHeroes` (on load only) stays for saves with heroes stranded before the fix; it returns the count; new `IsStuck`.
- `bmr.stats` rewritten (`Diagnostics\ConsoleCommands.cs`):
  - Header: mod version, build target, game version, campaign day and date, settings source, logging.
    freed / killed, stuck heroes put back on load.
  - Militias: leaderless, troops, prisoners, largest vs max size, power vs limit, raiding vs cap.
  - One clan table keyed by the `Clan` object (`[StringId]` for repeated names; before, clans with equal names were merged into one row), plus a Total row.
  - Counters since load (`Diagnostics\BmrCounters.cs`, not saved, reset in `OnGameEnd`): formed, merged, split, defeated, heroes created / reused / captured /
    freed / killed, stuck heroes reclaimed on load / recovered daily.
  - Warnings: totals mismatch, stuck, leaderless, dead heroes in the list, waiting but in a party, waiting but not tracked.
- New `bmr.settings`: every setting grouped like MCM, `*` = changed from the default, source MCM or ModConfiguration.xml.
- `bmr.stats` and `bmr.settings` also write their output to the log with the new `BmrLog.Info` (always written).
- New `ModSettings.UsesMcm`, `Helper.DaysPlayed`; `BanditMilitiaManager.CountRaidingMilitias` is internal.
- Save compatibility: none. No saved field, SyncData key or definer id added.
- To test (1.5.7, 1.4.8, 1.4.7):
  - `bmr.stats` and `bmr.settings` in the console; the same text is in the log.
  - Play 30+ days on 1.5.7, 1.4.8 and 1.4.7: `bmr.stats` shows Stuck 0; after a reload "stuck heroes put back on load: 0".

- Fixed the `NullReferenceException` in `BanditMilitiaManager.ConfigureGoldAndMounts` on a merge (crash report: `TryMergeBanditParties` -> `InitializeMilitiaParty`).
  The report says line 197; the failing statement is `leaderHero.Gold =` (line 200), its IL offsets are one statement early. The new militia had no leader.
  - 1.5.7: `Hero.SetPartyBelongedTo` calls `ChangePartyLeader(null)` on the party a leader leaves. Every roster change of the hero in any party runs it
    (`OnRemovedFromParty`, `OnAddedToParty`, `OnAddedToPartyAsPrisoner`), with no check of the party he is in now.
    `PartyComponent.ChangePartyLeader(hero)` changes nothing when the hero is not in `MemberRoster`.
  - Not reproduced: no vanilla 1.5.7 or mod code takes the leader out of the new party's `MemberRoster` between `MobileParty.CreateParty` and `ChangePartyLeader`
    (read: `CreateParty`, `InitializeMobilePartyWithRosterInternal`, `TroopRoster`, `PartyBase.OnHeroAdded` / `OnHeroRemoved`, the `MobilePartyCreated`,
    `OnPartySizeChangedEvent` and `OnPartyLeaderChangedEvent` listeners). Another mod or an unknown state does it; the new log line is there to find it.
- `Managers\BanditMilitiaManager.cs` (shared code, no `#if`; only vanilla members the mod already used):
  - New `EnsureLeaderAttached`, called by `InitializeMilitiaParty` after `ChangePartyLeader`. Nothing happens when `LeaderHero == leader && leader.PartyBelongedTo == militia`.
    Otherwise `BmrLog.Warn` writes `BMR-LEADER-LOST` (always: hero state, his party, his captor, his copies in the militia's member and prison roster, stack trace),
    his copies are removed from both rosters, he is added to the front of `MemberRoster` and made leader again. A second line is written when that failed.
  - `ConfigureGoldAndMounts` returns when the militia has no leader.
  - New `UsableLeader`: a recorded leader counts only when alive, not a prisoner, and `PartyBelongedTo` is null or this party. Otherwise the militia counts as leaderless
    (merge: a hero is reused or created; split: none). Used by `TryMergeBanditParties` and `TrySplitBanditMilitiaParty` instead of `LeaderHero`.
  - New `RemoveHeroFromRoster` / `HeroCountInRoster` (all copies; nothing when not listed).
  - Merge: the new leader, kept or reused, leaves both old parties' `MemberRoster` before `CreateBanditMilitiaParty`. Replaces
    `newLeader.PartyBelongedTo?.MemberRoster.RemoveTroop` (kept leader only, one copy, only the party he pointed to, `IndexOutOfRangeException` when not listed there).
    A copy left behind was removed by `RemoveParty` -> `MemberRoster.Clear()` after the merge, which cleared the new militia's leader.
  - Merge and split: the new leaders are removed from the prisoner rosters handed to the new parties (as a prisoner of his own militia the leader got `PartyBelongedTo = null`).
  - Split: the reused second leader is not handed out as a member; no split when the pool returns the first leader himself.
- Not done: no search of all parties for a leftover copy when a hero is reused (cost on the hourly AI tick). Such a copy can still leave a militia leaderless later
  (no crash; it gets a leader at its next merge).
- Version 1.5.7.2 -> 1.5.7.3 in `BanditMilitiasRedux.csproj`.
- Save compatibility: none. No saved field, SyncData key or definer id changed.
- To test (1.5.7, 1.4.8, 1.4.7):
  - Bandits + bandits, militia + bandits, militia + militia: the new militia has a leader with gold; the weaker leader waits for reuse (`bmr.heroes`).
  - Split: both militias have a leader; a captured lord stays a prisoner after a merge and a split.
  - A normal session writes no `BMR-LEADER-LOST`; "Leaderless parties" in `bmr.stats` does not grow.

- Militia hero progression (all versions, shared code, no `#if`; HeroDeveloper / StrengthRatio members read in dnSpy, everything compile-checked on 1.5.7, 1.4.8, 1.4.7):
  - New settings in Training & Growth (MCM Order 8 and 9, `ModConfiguration.xml`, `bmr.settings`): `HeroXpMultiplier` (float 1–5, default 1.5, format "0.0"),
    `HeroXpPerWin` (int 0–1000, default 200). New text ids `BMR_k3VqZ8w`, `BMR_Rf6nJ2x`, `BMR_p9TsL4c`, `BMR_Y7mWd1h` (all 13 files).
  - New `Patches\HeroDeveloperPatch.cs`: Prefix on `HeroDeveloper.AddSkillXp(SkillObject, float, bool, bool)` multiplies `rawXp` for militia heroes
    (bandit clan, `CanTrain`, multiplier above 1, `IsBanditMilitiaHero`). `rawXp` also feeds `GainRawXp`, so level XP is scaled too.
  - New `Managers\HeroProgressionManager.cs`:
    - `AddUnscaledSkillXp` sets `IsGivingUnscaledXp` so the multiplier skips mod-given XP. `BanditHeroCreator.TrainBanditLeader` uses it (150 Leadership XP unchanged).
    - `RewardWin` (gate `CanTrain`), called by `MapEventFinishBattlePatch` after `RecordWin`.
      XP = `HeroXpPerWin` × winner `MapEventSide.StrengthRatio`, clamped to `Balance.HeroWinXpRatioMin` / `Max` (0.25–3).
      `StrengthRatio` = (enemy + 10) / (own + 10) (siege-adjusted, max 10) and is set only when parties join (`RecalculateRenownAndInfluenceValuesOnPartyInvolved`), so casualties do not change it.
      Skills: Leadership, Tactics, distinct `RelevantSkill` of `BattleEquipment` Weapon0–3, Riding with a horse, else Athletics. `isAffectedByFocusFactor` true, `shouldNotify` false.
    - Every `Balance.FocusPointEveryWins` (7) wins: one focus point to the highest-skill weapon skill in use below `MaxFocusPerSkill`, then the next one, then Tactics, then Leadership; else none.
      Every `Balance.AttributePointEveryWins` (14) wins: one point to a random attribute below `MaxAttribute`; all full = none. Both use the "no unspent points" overloads.
      New wins only (the win count is checked after `RecordWin`). Focus and attribute points do not depend on `HeroXpPerWin` (0 turns off the XP only).
  - Epithets from wins only: `NotorietyBehavior.GetTier` = `CountReached(NotorietyWinThresholds, wins)`. Removed `GetTier(int, CampaignTime)`, `Balance.NotorietyDayThresholds`,
    the no-wins encyclopedia sentence (`Texts.EncyclopediaEpithet`, text id `BMREncEpithet2`, removed from all 13 files). `_firstSeen` is still saved (`bmr.heroes`).
    Party names are `[CachedData]`, so age-only epithets disappear on load.
- Save compatibility: none. No saved field, SyncData key or definer id added; `_firstSeen` kept.
- To test (1.5.7, 1.4.8, 1.4.7):
  - Logging on: a militia win writes "Win reward: ... XP per skill (strength ratio ...)" for its leader; the 7th / 14th win writes the focus / attribute line.
  - Encyclopedia of a leader with 7 wins: higher skills and a focus point; leaders with fewer than 3 wins have no epithet.
  - `bmr.settings` lists `HeroXpMultiplier` and `HeroXpPerWin`; `CanTrain` off: no win reward, no multiplier.

## BMR v1.5.7.2

- Fixed a crash after loading a save while another one was being played (`MobileParty.get_IsBanditBossParty` NRE from `DefaultMapTrackModel.CanPartyLeaveTrack`).
  - Cause: the Harmony id was the ModuleId. `Bannerlord.ModuleLoader.BanditMilitiasRedux.dll` (assembly name `BanditMilitiasRedux`) puts its own 22 patches under that same id,
    so the v1.5.7.1 `Harmony.UnpatchAll(Harmony.Id)` in `SubModule.RemovePatches` removed the loader's patches too.
    The loader then no longer forwarded `OnGameStart` for the next loaded save: no behaviors, no patches, and the vanilla boss getter threw on every militia
    (`IsBandit` true, no `BanditPartyComponent`).
  - Found with a temporary `BMR-PATCH` dump (removed): main menu 22 patches under "BanditMilitiasRedux" before our `PatchAll`, 0 after `OnGameEnd`, no `OnGameStart` for the reload.
  - `Core\ModInfo.cs`: new `HarmonyId` = `ModuleId + ".Harmony"`. `SubModule`: `new Harmony(ModInfo.HarmonyId)`. Not a saved identifier.
- Version 1.5.7.1 -> 1.5.7.2 in `BanditMilitiasRedux.csproj`.
- Save compatibility: none.
- To test (1.5.7, 1.4.8, 1.4.7): load a save, move on the map, load the same save again without restarting, move on the map: no crash, militias merge and show markers.

## BMR v1.5.7.1

- Bug hunt after the migration. All fixes are in shared code (no `#if`): the bugs are the same on 1.4.7, 1.4.8 and 1.5.7. Vanilla behavior was read in BannerlordSource1.5.7 only.
- Fixed Harmony patches stacking. `Harmony.PatchAll` ran in `SubModule.OnGameStart` on every game start and Harmony never skips patches it already applied
  (Lib.Harmony 2.4.2: `PatchInfo.Add` only appends; test: 1 / 2 / 3 `PatchAll` calls -> a postfix runs 1 / 2 / 3 times). The 4.x versions had the same call.
  - Effect after the second game start in one session: every Prefix / Postfix ran twice (`GrantDefeatBounty`, `RecordWin`, `RecordDefeat`, capture queue), every Transpiler ran on its own output.
  - `SubModule.ApplyPatches(Game)`: `PatchAll` once, remembers the game in `_patchedGame`. `SubModule.RemovePatches(Game)` (from `OnGameEnd`): `Harmony.UnpatchAll(Harmony.Id)`,
    only for that game, so an older game ending after a newer one started cannot remove the patches.
  - Patches stay in `OnGameStart`, never `OnSubModuleLoad` (human bullet guide: http://bannerlordmodding.lt/guides/human_bullet/).
  - `PatchAll` failure (`HarmonyException`) is logged with `BmrLog.Error` and rethrown. Behaviors are registered before the patches.
  - `Patches\PlayerEncounterPatch.cs` transpiler: `ThrowIfInvalid` -> logs `BMR-TRANSPILER` and returns the unchanged instructions, like the other two transpilers
    (one throwing patch class stops the whole `PatchAll`).
- Fixed split and daily training during a battle: `Extensions.IsTooBusyToGrowOrSplit` now includes `MapEvent is not null`;
  `MilitiaBehavior.OnDailyTickPartyEvent` calls `TrainBanditMilitiaParty` only when `MapEvent is null`.
- Fixed merge / split of the party that holds the player prisoner. 1.5.7 `PlayerCaptivityCampaignBehavior.CheckCaptivityChange` frees the player when the captor party
  is no longer active (`menu_captivity_end_by_party_removed`).
  - New `Extensions.IsHoldingPlayerPrisoner`. Used in `BanditMilitiaManager.CanMobilePartiesMerge`, `TrySplitBanditMilitiaParty`, the candidate filter of
    `TryFindAndMergeNearbyBandits` and `MilitiaBehavior.OnTickPartialHourlyAiEvent` (militia and plain bandit initiators).
- Fixed hero prisoners being released on a merge. 1.5.7 `MobileParty.RemoveParty` -> `ReleaseHeroPrisoners` -> `EndCaptivityAction` removes the hero from
  `PartyBelongedToAsPrisoner`, which already was the new militia.
  - `TryMergeBanditParties` clears both old `PrisonRoster`s after `MergeRosters` copied them and before `InitializeMilitiaParty` (same order `SplitRosters` uses).
- Fixed raids dropped by merges: a militia with `DefaultBehavior == RaidSettlement` no longer starts a merge search (`OnTickPartialHourlyAiEvent`),
  and `Extensions.IsTooBusyToMerge` also checks `DefaultBehavior`, so it is not picked as a merge target either.
- Fixed quest parties being mergeable. `VerbotenParties` was a `HashSet<string>` checked with `Contains(StringId)`, but vanilla ids carry a suffix
  (`"rescue_family_quest_raider_party_" + n`, `"caravan_ambush_quest_" + clan.Name`, `"company_of_trouble_" + questId`, `"ebdi_deserters_party_1"`): nothing ever matched.
  Now a `string[]` of prefixes checked by `IsVerbotenParty` (`StartsWith`, ordinal, plain loop).
- Split fixes in `TrySplitBanditMilitiaParty`:
  - Roll `RandomInt(0, 100) > RandomSplitChance` -> `>=` (`MBRandom.RandomInt(min, max)` excludes max): 0 never splits, 100 always. The 4.9.0 fix left 0 at 1%.
  - Cooldown: no split before `LastMergedOrSplitDate + CooldownHours` (was read only for merging; the setting hint and `FEATURES.md` always said it applies to splits).
- `BanditMilitiaManager.ConfigureGoldAndMounts`: `leaderHero.Gold +=` -> `=`. It was `=` before the 4.8.0 refactor; `+=` piled gold up with every merge and split.
- `MilitiaBehavior.OnRaidCompletedEvent`: unlocks (`SetDoNotMakeNewDecisions(false)` + hold) every militia on the attacker side, not only the first one.
- `MilitiaBehavior.OnVillageBeingRaidedEvent`: the condition `villageBeingRaided.Owner?.LeaderHero == Hero.MainHero` was never true (1.5.7: `SettlementComponent.Owner` is
  the settlement `PartyBase`, `PartyBase.LeaderHero` is `MobileParty?.LeaderHero`). Now `Settlement.OwnerClan == Hero.MainHero.Clan`, like the message in `Think`.
- Null guards:
  - `ReusableHeroesBehavior.TryGetReusableBanditHero`, `HasReusableBanditHero`, `AddHeroToTheWaitingDictionary`: a null clan returns instead of throwing (the pool is keyed by clan).
  - `Helper.BestSettlementForMerge` / `FindClosestHideout` return `Settlement?`; `BanditHeroCreator.ReuseOrCreateBanditHero` returns null without a home or clan;
    `TryMergeBanditParties` resolves the home before the leader leaves his party and skips the merge without one.
- `ModSettings.XpGiftIndex` / `GoldRewardIndex`: new `ValidIndex` falls back to the default index when the value is outside `Balance.DifficultyXpValues` / `Balance.GoldValues`.
- Random Banners off: `Patches\MapTrackerItemVMPatch.cs` keeps the vanilla (clan) banner on the marker and skips a militia without a banner.
  - The 5000-banner cache stays unconditional on purpose: every militia gets a banner, so the setting can be switched on mid-campaign. `FEATURES.md` row corrected.
- Ctrl+Alt+N: `SubModule.OnApplicationTick` requires `MapState` as the active state and calls the new `Helper.AskToNukeEverything` (inquiry, `Globals.IsNukeInquiryOpen`);
  `Helper.NukeEverything` is private now. `IsOrphanedBanditMilitiaHero` also requires `HeroTemplates.Contains(hero.CharacterObject.OriginalCharacter)`.
  - New text id `BMR_H6Jm3wr` (all 13 files). Vanilla ids reused with the exact vanilla text, no xml rows: `awjomtnJ` "Are you sure?", `aeouhelq` "Yes", `8OkPHu4f` "No".
- Checked, no change needed: 1.5.7 `CampaignBehaviorDataStore.BehaviorSaveData.SyncData` leaves the field untouched when the key is missing, so adding the mod to a running
  campaign does not null `AllAliveBanditMilitiaHeroes`, `_waitingToBeReusedHeroes` or the notoriety dictionaries.
- Vanilla API used for the first time in this mod (must exist on 1.4.7 / 1.4.8 too): `TaleWorlds.CampaignSystem.GameState.MapState`, `GameStateManager.ActiveState`,
  `InformationManager.ShowInquiry`, `InquiryData` (first 8 constructor parameters).
- `DevChangelog.txt` -> `DevChangelog.md` (markdown). Workspace: `.github\copilot-instructions.md` (patch lifecycle, `IsHoldingPlayerPrisoner`), `FEATURES.md` rows,
  `NexusDescription.txt` (Ctrl+Alt+N line).
- Version 1.5.7.0 -> 1.5.7.1 in `BanditMilitiasRedux.csproj` (1.5.7.0 was already released; VERSION rule).
- Save compatibility: none. No saved field, SyncData key or definer id changed.
- To test (1.5.7, 1.4.8, 1.4.7):
  - Load a save, exit to the main menu, load again: one bounty per defeated leader, wins / defeats counted once; map markers, banners and dialogs still work after the second load.
  - Merge with a captured lord in one of the bandit parties: he stays a prisoner of the new militia.
  - Get captured by a militia: it neither merges nor splits; captivity ends the vanilla way.
  - Raid: a militia marching to a village keeps going with bandit parties nearby; after a raid by two militias both patrol again; "is being raided" shows for an own village.
  - Daily Split Chance 0 (never) and 100 (always, once the cooldown passed); no split or training while in a battle.
  - Leader gold after several merges matches strength x Bandit Hero Gold Reward.
  - Ctrl+Alt+N: nothing in a battle or the encyclopedia; on the map the question appears once, No keeps everything, Yes clears.
  - Random Banners off: markers show the clan banner.

## BMR v1.5.7.0

- Migrated the mod to the multi-version standard (one code base, one DLL per game version, `Bannerlord.ModuleLoader`).
  - csproj: `Bannerlord.BuildResourcesLite.Sdk` 1.2.3.41, `netstandard2.0`, AssemblyName `$(ModuleId).v$(GameVersion)`, `BuildForWindowsStore`, `v<ver>` define.
  - Removed game HintPath references, TargetFrameworks `net472;net6`, `Nullable`, `IsExternalInit`, `Bannerlord.BuildResources`, `Harmony.Extensions`.
  - `Lib.Harmony` 2.4.3 -> 2.4.2 (2.4.3+ forbidden). Added `Bannerlord.ModuleLoader.Injector` 1.0.1.50. Kept `Bannerlord.MCM` 5.13.1, `BUTR.Harmony.Analyzer` 1.0.1.50.
  - Version 4.11.0 -> 1.5.7.0 (VERSION rule: newest game version + release count).
  - `SubModule.xml`: single "Bannerlord Module Loader" submodule; Harmony required; MCM moved from DependedModules to optional LoadBeforeThis metadata.
- Moved files to the standard layout; namespaces file-scoped, only namespace/usings changed unless noted below.
  - `Behaviours\` -> `Behaviors\` (behavior class names kept: they are data-store ids).
  - `Constructors\BanditMilitiaPartyComponent.cs`, `BanditMilitiasSaveableTypeDefiner.cs`, `MCMSettings.cs` -> `Data\`.
  - `Globals.cs` -> `Core\Globals.cs` (runtime state) + `Core\Balance.cs` (tuning numbers and fixed ids, previously inline literals).
  - `Helpers\Helper.cs`, `Extensions.cs`, `EquipmentPool.cs` -> `Core\`. `Helpers\BanditHeroCreator.cs`, `HeroAppearanceService.cs` -> `Managers\`.
  - `Helpers\ConsoleCommands.cs` -> `Diagnostics\`. `Helpers\SaSCompat.cs` -> `Compat\`. `Helpers\Logs.cs` -> `Diagnostics\BmrLog.cs` (rewritten).
  - `Managers\` kept as a listed workspace deviation.
  - `Patches\MilitiaPatches.cs` and `PrisonerPatches.cs` split into one `Patches\<VanillaClass>Patch.cs` per vanilla class; nested classes removed, all internal, feature headers added.
  - `SubModule.CacheBanners` -> `Core\Helper.cs` (private, called from `InitializeGlobalModState`).
- Removed dead code: `PatchSaSDeserters`, commented `NameGenerator` full-name patch, commented `DisperseLeaderlessMilitia` / `FillBanditParty`,
  `SubModule.ConfigDir`, `Helper.GangLeaderNames`, `Helper.SelectStrongestHero`, `Globals.Giant`, unused `Globals` proxies to `EquipmentPool`.
- Settings: MCM optional.
  - New `Core\ModSettings.cs` (only settings access; MCM read live in NoInlining methods, XML cached; `Reload` in `SubModule.OnGameStart`).
  - New `Data\ModConfiguration.cs` (defaults/ranges as consts, one section per MCM group) + `_Module\ModConfiguration.xml`.
  - Dropdowns `XpGift` / `GoldReward` stored as enums `XpGiftLevel` / `GoldRewardLevel` (words in xml, index in MCM).
  - `MCMSettings`: Id/FolderName "BanditMilitiasRedux" and property names/types kept; new `LoggingEnabled` in group "Technical settings" (GroupOrder 7).
  - `EnableChatLogging` and `EnableFileLogging` removed (players' saved values for them are dropped).
  - MCM change event now goes through `ModSettings.Changed`; dropdown localization through `ModSettings.LocalizeMcmDropdowns`.
  - Calradia Expanded Kingdoms load-order check -> `ModSettings.ForceRandomBannersOff` (also works in XML mode).
- Logging: `Logs.WriteToChat` / `WriteToFile` replaced by `BmrLog` (`Modules\BanditMilitiasRedux\logs\BanditMilitiasRedux_<date>.log`).
  - Debug lines (`BMR-STUCK`, `BMR-DESERTERS-SPAWN`, `BMR-MOUNT-NULLCHECK`, `BMR-REATTACH-VIOLATION`, `BMR-SAS-COMPAT`) only with `LoggingEnabled`; hot paths test `BmrLog.IsDebugEnabled` first.
  - Errors (`BMR-TRANSPILER`, `BMR-RELATION-EXCEPTION`, `BMR-EQUIPMENT-BUILD`, `NukeEverything` catches) always written; empty catch blocks now log.
- Texts: all strings moved to `Core\Texts.cs`; English const text = the English xml (what players saw), not the old code text.
  - `std_common_strings_xml.xml` -> `std_module_strings_xml.xml`; removed the English `language_data.xml` override.
  - New ids (all 13 files): `BMGoldRewardDesc`, `BMREncDefected`, `BMRPrisonerPretalk`, `BMRPrisonerExecuteCancel` (were used in code, missing in xml),
    `BMR_udVrtbS` / `BMR_WA9I1JU` / `BMR_2T70tyP` (Technical settings, Logging for debugging + hint),
    `BMR_EyG35bd`..`BMR_t2fWU8c` (10 notoriety epithets, were hardcoded English), `BMR_A0OadqW` "(Unknown Town)", `BMR_yquQLhT` "BANDIT MILITIAS CLEARED",
    `BMR_TOQCjcE` encyclopedia sentence joiner "{A} {B}" ("{A}{B}" in CNs, CNt, JP; replaces "{=!}{A}{B}", sentences ran together).
  - "\n" -> "{newline}" in all xml; released "{\\?}" gender closers -> "{\?}" in all 13 files (gender conditionals were broken).
  - Xml re-indented with tabs, UTF-8 BOM.
- Hot paths: removed LINQ/allocations from `TrainBanditMilitiaParty`, `ConvertLootersForTraining`, `TryGrowMilitiaParty`, `TrySplitBanditMilitiaParty`,
  `EquipmentPool.AdjustCavalryCount`, `AvoidanceManager.AdjustAvoidance` / `DecreaseAvoidance`, `PowerCalculationManager`, `Helper.GetMostPrevalentFromNearbySettlements` (reused buffers, for-i).
  - Split hero order kept identical (stable insert by Power, descending = former `OrderByDescending`).
- 1.5.7 adaptations (only in `#else` / `#if !LOWER_THAN_1_5`; 1.4.x code unchanged):
  - `MobilePartyVisual.AddCharacterToPartyIcon` lost `ref string bannerKey`: banner prefix moved to 1.4.x-only class;
    1.5.7 uses `Patches\MobilePartyVisualHelperPatch.cs` (transpiler on `SandBoxViewHelpers.MobilePartyVisualHelper.GetHumanAgentPartyVisual`, swaps `Banner.BannerCode` for the militia `BannerKey`).
  - `MapTrackerProvider` removed: `Patches\MapTrackerProviderPatch.cs` 1.4.x only; 1.5.7 `Patches\MapTrackerManagerPatch.cs` (Postfix `ShouldAutoAddTrackerForParty`);
    `MilitiaTrackerManager.Refresh` calls internal `MapTrackerManager.Refresh(ITrackableCampaignObject)` on 1.5.7.
  - `DefaultExecutionRelationModel` removed: `Patches\DefaultExecutionRelationModelPatch.cs` 1.4.x only;
    1.5.7 `Patches\ExecutionCampaignBehaviorPatch.cs` (Prefix `OnPlayerExecutedHero`: no blood feud, no Honor/Mercy penalty for militia heroes).
  - `Banner.CreateRandomBannerInternal` 2nd param is now private enum `BannerIconOrientation`: `Helper.CacheBanners` passes `Enum.ToObject(type, -1)`.
  - `HeroExecutionSceneNotificationData.CreateForInformingPlayer` gained a `CampaignTime` date param: `DialogsBehavior` passes `CampaignTime.Now`.
  - `CampaignObjectManager._partiesWithoutPartyComponent` is `MBList`: `LegacyFlushBanditMilitias` removes in place instead of assigning a `List`.
  - All other patch targets and parameter names verified against BannerlordSource1.5.7 (extra trailing params are fine: Harmony binds by name).
- Null checks added on `__instance` / args in every patch (no behavior change for valid input).
- Workspace: `.github\copilot-instructions.md` rewritten (not legacy, kept exceptions), `FEATURES.md` updated, `NexusDescription.txt` rewritten from the template.
- Save compatibility: none. SyncData keys, SaveableField ids, save definer ids and behavior class names unchanged.
- To test (1.5.7, 1.4.8, 1.4.7; with and without MCM): old save loads; merge/split/raid; map markers; militia banner on map icon and in battle;
  execute a captured leader (no relation/Honor loss, optional Honor XP); encyclopedia text (spaces, she/he) in EN, CNs, PL/RU; `ModConfiguration.xml` missing/broken/out of range.
