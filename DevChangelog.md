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
