# Bandit Militias Redux — Feature Map

Feature → gate → files. Read this first, then open only the files listed.
Gate: `setting:<Name>` = setting read through `Core\ModSettings.cs` (MCM when loaded, else `ModConfiguration.xml`). `always` = no setting.
Tuning numbers (not settings) live in `Core\Balance.cs`; runtime state and caches in `Core\Globals.cs`.
`1.4.x only` / `1.5.7 only` = file or block inside `#if LOWER_THAN_1_5` / `#if !LOWER_THAN_1_5`.

## Core / setup

| Feature | Gate | Files |
|---|---|---|
| Harmony patches applied once per game (`OnGameStart`) and removed at its end (`OnGameEnd`) under the own Harmony id `ModInfo.HarmonyId` (never the ModuleId), settings reload, behavior registration, state reset on game end | always | `SubModule.cs` (`ApplyPatches`, `RemovePatches`), `Core\ModInfo.cs` |
| Settings: MCM when loaded, otherwise ModConfiguration.xml | always | `Core\ModSettings.cs`, `Data\MCMSettings.cs`, `Data\ModConfiguration.cs`, `_Module\ModConfiguration.xml` |
| MCM dropdown items shown in the game language | always (MCM) | `SubModule.cs`, `Core\ModSettings.cs`, `Data\MCMSettings.cs` (`LocalizeDropdowns`) |
| Random banner cache (5000 banners): every militia gets a banner, shown only with the setting on | always (shown: setting:RandomBanners) | `Core\Helper.cs` (`CacheBanners`), `Core\Globals.cs` |
| Random banners forced off when this mod loads after Calradia Expanded Kingdoms | always | `SubModule.cs` (`AdjustForLoadOrder`), `Core\ModSettings.cs` (`ForceRandomBannersOff`) |
| Settings change (MCM) → recalc raid cap, reset names, refresh markers | always | `SubModule.cs` (`OnSettingsChanged`) |
| Ctrl+Alt+N on the campaign map: asks, then removes all militia content (`AskToNukeEverything`, `NukeEverything`) | always | `SubModule.cs`, `Core\Helper.cs`, `Core\Texts.cs` |
| Tuning numbers and fixed ids | always | `Core\Balance.cs` |
| Caches (`Hideouts`, `Villages`, `RaidCap`), in-memory AI trackers, `ClearGlobals` | always | `Core\Globals.cs` |
| One-time mod state init (hideouts, villages, troops, templates, raid cap) on map screen open | always | `Core\Helper.cs` (`InitializeGlobalModState`), `Patches\MapScreenPatch.cs` |
| Canonical militia checks (`IsBanditMilitiaParty`, `IsBanditMilitiaHero`, …) | always | `Core\Extensions.cs` |
| Save type definer (party component, containers) | always | `Data\BanditMilitiasSaveableTypeDefiner.cs`, `Data\BanditMilitiaPartyComponent.cs` |
| Localization (English + 12 languages) | — | `Core\Texts.cs`, `_Module\ModuleData\Languages\` |

## Spawning, merging, splitting

| Feature | Gate | Files |
|---|---|---|
| Nearby bandit parties merge into a Bandit Militia | setting:MergeableSize, setting:CooldownHours, setting:GlobalPowerPercent | `Managers\BanditMilitiaManager.cs`, `Behaviors\MilitiaBehavior.cs`, `Patches\EncounterManagerPatch.cs` |
| No merge or split while the militia holds the player prisoner; no merge search on the way to a raid; no split in a battle | always | `Core\Extensions.cs` (`IsHoldingPlayerPrisoner`, `IsTooBusyToMerge`, `IsTooBusyToGrowOrSplit`), `Behaviors\MilitiaBehavior.cs` (`OnTickPartialHourlyAiEvent`), `Managers\BanditMilitiaManager.cs` |
| Quest and story bandit parties never merge (party id prefixes) | always | `Managers\BanditMilitiaManager.cs` (`VerbotenParties`, `IsVerbotenParty`) |
| Merge home settlement: leader's birth place, else a land hideout | always | `Core\Helper.cs` (`BestSettlementForMerge`, `FindClosestHideout`, `IsLandHideout`) |
| Militia party component (owner, leader, home, banner, avoidance, name) | always | `Data\BanditMilitiaPartyComponent.cs` |
| Leader: reuse a waiting hero, else create one (cap per clan) | setting:HeroPoolCap, setting:FemaleSpawnChance | `Managers\BanditHeroCreator.cs`, `Behaviors\ReusableHeroesBehavior.cs` |
| New militia leader stays attached: a dead, imprisoned or foreign-party leader is replaced; the leader leaves the old parties' lists first and is never his militia's prisoner; a leader lost during setup is put back (`BMR-LEADER-LOST`) | always | `Managers\BanditMilitiaManager.cs` (`UsableLeader`, `RemoveHeroFromRoster`, `EnsureLeaderAttached`) |
| Militia splits in two | setting:RandomSplitChance, setting:CooldownHours | `Managers\BanditMilitiaManager.cs` (`TrySplitBanditMilitiaParty`) |
| Power calc: max party size, global power limit, average militia power | setting:GlobalPowerPercent | `Managers\PowerCalculationManager.cs` |
| Cached list of active militias (refreshed at most hourly) | always | `Managers\PowerCalculationManager.cs` (`GetActiveBanditMilitiaParties`) |

## Growth / training

| Feature | Gate | Files |
|---|---|---|
| Daily troop growth | setting:GrowthChance, setting:GrowthPercent, setting:GlobalPowerPercent | `Managers\BanditMilitiaManager.cs` (`TryGrowMilitiaParty`) |
| Daily training, upgrades, bonus XP (not during a battle) | setting:CanTrain, setting:DailyTrainingChance, setting:UpgradeUnitsPercent, setting:MaxTrainingTier, setting:XpGift | `Managers\BanditMilitiaManager.cs` (`TrainBanditMilitiaParty`), `Behaviors\MilitiaBehavior.cs` (`OnDailyTickPartyEvent`) |
| Looters converted to culture recruits or bandit-tier troops | setting:CanTrain, setting:UseBanditOnlyTroopTrees | `Managers\BanditMilitiaManager.cs` |
| Equipment pool, cavalry count, gold and mounts | setting:MaxItemValue, setting:GoldReward | `Core\EquipmentPool.cs`, `Managers\BanditMilitiaManager.cs` (`ConfigureGoldAndMounts`) |
| Upgrade targets and upgrade permission | setting:UseBanditOnlyTroopTrees, setting:MaxTrainingTier | `Patches\PartyUpgraderCampaignBehaviorPatch.cs` (transpiler), `Patches\DefaultPartyTroopUpgradeModelPatch.cs` |

## Map AI (patrol / raid / stuck)

| Feature | Gate | Files |
|---|---|---|
| `Think`: idle → patrol near home; hideout arrival → hold; patrol → raid roll | always | `Managers\BanditMilitiaManager.cs` (`Think`) |
| Home = nearest active hideout of own bandit culture, else home settlement, else own position (land-reachable only) | always | `Managers\BanditMilitiaManager.cs` (`FindHomeSettlement`) |
| Patrol village near home (range 200, then 400), re-picked every 3 days; idle retry after 6 h | always | `Managers\BanditMilitiaManager.cs` (`TryStartHomePatrol`), `Core\Balance.cs` |
| Raid: richest village (hearths) vs distance within 200, at war, up to 3 candidates | setting:AllowPillaging, setting:PillagingChance | `Managers\BanditMilitiaManager.cs` (`FindRaidTarget`, `FindBestRaidVillage`) |
| Raid cap (counts militias raiding or marching to raid) | setting:RaidCapPercent | `Managers\BanditMilitiaManager.cs` (`CountRaidingMilitias`), `Core\Helper.cs` (`CalculateRaidCap`) |
| Raid avoidance: one roll vs highest avoidance toward owner clan | always | `Managers\BanditMilitiaManager.cs` (`IsVillageAvoided`) |
| Release militias marching to a village that got looted | always | `Behaviors\MilitiaBehavior.cs` (`OnVillageStateChanged`), `Managers\BanditMilitiaManager.cs` (`ReleaseMilitiasRaidingLootedVillage`) |
| Raid done → unlock AI of every militia in the raid and hold | always | `Behaviors\MilitiaBehavior.cs` (`OnRaidCompletedEvent`) |
| Stuck rescue (12 h within 15 units) → patrol near home; after 2 rescues hold 6 h; logs `BMR-STUCK` | always | `Managers\BanditMilitiaManager.cs` (`TryHandleStuckMilitia`), `Core\Balance.cs` |
| Partial-hourly AI: stuck check, drop peace engagements, unlock stale AI, merge | always | `Behaviors\MilitiaBehavior.cs` (`OnTickPartialHourlyAiEvent`) |
| Think on idle every AI tick (hot path) | always | `Patches\MobilePartyAiPatch.cs` (`MobilePartyAiTickInternalPatch`) |
| Vanilla land-bandit patrol AI skipped for militias | always | `Patches\AiLandBanditPatrollingBehaviorPatch.cs` |
| Militias never enter settlements (hold instead) | always | `Patches\EnterSettlementActionPatch.cs` |
| Militias are land-only (no naval navigation) | always | `Patches\MobilePartyPatch.cs` (`MobilePartyHasNavalNavigationCapabilityPatch`) |
| Engage filter: skip villagers/caravans, avoided heroes, too strong/weak targets | setting:IgnoreVillagersCaravans, setting:MaxStrongerTargetPercent, setting:MaxWeakerTargetPercent | `Patches\MobilePartyAiPatch.cs` (`MobilePartyAiSetAiBehaviorPatch`) |

## Avoidance

| Feature | Gate | Files |
|---|---|---|
| Weekly avoidance decay | always | `Managers\AvoidanceManager.cs`, `Behaviors\MilitiaBehavior.cs` (`OnDailyTickPartyEvent`) |
| Avoidance rises around a hero who destroyed a militia | always | `Managers\AvoidanceManager.cs`, `Behaviors\MilitiaBehavior.cs` (`OnMobilePartyDestroyed`) |
| Avoidance drops toward heroes the militia beat | always | `Managers\AvoidanceManager.cs`, `Patches\MapEventPatch.cs` |

## Heroes

| Feature | Gate | Files |
|---|---|---|
| Hero creation: template, name, appearance, equipment, leader training | setting:FemaleSpawnChance, setting:MaxItemValue, setting:CanTrain | `Managers\BanditHeroCreator.cs`, `Managers\HeroAppearanceService.cs`, `Core\EquipmentPool.cs` |
| Reusable hero pool (released, escaped, displaced leaders; every militia hero still listed in a removed militia); stuck heroes put back on load | setting:HeroPoolCap | `Behaviors\ReusableHeroesBehavior.cs` (`AddPartyHeroesToTheWaitingDictionary`), `Behaviors\MilitiaBehavior.cs` (`OnMobilePartyDestroyed`), `Core\Helper.cs` (`TryTrashMobilePartySafelyReservingBanditLeader`) |
| Leaders reserved for reuse on state change / leader removal | always | `Patches\HeroPatch.cs` (`HeroChangeStatePatch`), `Patches\MobilePartyPatch.cs` (`MobilePartyRemovePartyLeaderPatch`) |
| Defection: hero leaving a bandit clan stops being tracked, may become wanderer | always | `Behaviors\HeroDefectionBehavior.cs` |
| Notoriety: wins, defeats, epithets (from wins only), encyclopedia text | always | `Behaviors\NotorietyBehavior.cs`, `Patches\MapEventPatch.cs`, `Patches\EncyclopediaHeroPageVMPatch.cs` |
| Leader win reward: skill XP (Leadership, Tactics, weapon skills in use, Riding or Athletics) × enemy/own strength (0.25–3), focus point every 7 wins, attribute point every 14 | setting:CanTrain, setting:HeroXpPerWin | `Managers\HeroProgressionManager.cs` (`RewardWin`), `Patches\MapEventPatch.cs` (`MapEventFinishBattlePatch`), `Core\Balance.cs` |
| Hero XP multiplier: all vanilla skill XP of militia heroes (mod-given XP is not scaled) | setting:CanTrain, setting:HeroXpMultiplier | `Patches\HeroDeveloperPatch.cs`, `Managers\HeroProgressionManager.cs` (`AddUnscaledSkillXp`) |
| Bounty for defeating a militia leader | always | `Core\Helper.cs` (`GrantDefeatBounty`), `Patches\MapEventPatch.cs` |
| Death cleanup; execution honor XP; no vanilla Honor penalty | setting:ExecutionHonorXp | `Patches\KillCharacterActionPatch.cs`, `Patches\TraitLevelingHelperPatch.cs` |
| Executing a militia leader angers no one (1.4.x: relation model) | always | `Patches\DefaultExecutionRelationModelPatch.cs` (1.4.x only) |
| Executing a militia leader starts no blood feud, no Honor/Mercy loss (1.5.7) | always | `Patches\ExecutionCampaignBehaviorPatch.cs` (1.5.7 only) |
| Home settlement forced to birth settlement | always | `Patches\HeroPatch.cs` (`HeroUpdateHomeSettlementPatch`) |
| No vanilla hero spawning / renown / fugitive handling for militia heroes | always | `Patches\HeroSpawnCampaignBehaviorPatch.cs`, `Patches\GainRenownActionPatch.cs`, `Patches\MakeHeroFugitiveActionPatch.cs` |
| Encyclopedia: militia heroes visible; bandit clan links blocked | always | `Patches\DefaultEncyclopediaHeroPagePatch.cs`, `Patches\EncyclopediaManagerPatch.cs` |
| Relations with militia heroes use the heroes themselves | always | `Patches\DefaultDiplomacyModelPatch.cs` |

## Prisoners

| Feature | Gate | Files |
|---|---|---|
| Defeated militia leaders captured by the player, capture conversation | always | `Patches\MapEventPatch.cs` (`MapEventCalculateAndCommitMapEventResultsPatch`), `Patches\BanditInteractionsCampaignBehaviorPatch.cs` |
| Hero prisoners move with the militia on merge and split | always | `Managers\BanditMilitiaManager.cs` (`TryMergeBanditParties`, `SplitRosters`) |
| Militia heroes not freed by vanilla prisoner release (transpiler) | always | `Patches\PlayerEncounterPatch.cs` (`PlayerEncounterDoFreeOrCapturePrisonerHeroesPatch`) |
| No duplicate hero prisoners | always | `Patches\PartyBasePatch.cs` (`PartyBaseAddPrisonerPatch`) |
| No ransom offers for militia heroes | always | `Patches\RansomOfferCampaignBehaviorPatch.cs` |
| Prisoner taken/released log messages hidden | setting:RemovePrisonerMessages | `Patches\DefaultLogsCampaignBehaviorPatch.cs` |

## Dialogs / encounters

| Feature | Gate | Files |
|---|---|---|
| Leader talk; keep / release / execute captive | always | `Behaviors\DialogsBehavior.cs` |
| Skip militia conversations on encounter | setting:SkipConversations | `Patches\PlayerEncounterPatch.cs` (`PlayerEncounterDoMeetingInternalPatch`) |
| Lord conversation lines hidden for militia leaders | always | `Patches\ConversationManagerPatch.cs` |
| Militia leaders use bandit voice lines; female leaders keep female lines | setting:CheckVoiceGender | `Patches\DefaultVoiceOverModelPatch.cs` |

## Map UI

| Feature | Gate | Files |
|---|---|---|
| Party names (with / without leader) | setting:BanditMilitiaString, setting:LeaderlessBanditMilitiaString | `Data\BanditMilitiaPartyComponent.cs`, `Patches\PartyNameplateVMPatch.cs` |
| Militia banner on the map icon (1.4.x) | setting:RandomBanners | `Patches\MobilePartyVisualPatch.cs` (`MobilePartyVisualAddCharacterToPartyIconBannerPatch`, 1.4.x only) |
| Militia banner on the map icon (1.5.7) | setting:RandomBanners | `Patches\MobilePartyVisualHelperPatch.cs` (transpiler, 1.5.7 only) |
| Militia banner in battle | setting:RandomBanners | `Patches\PartyGroupAgentOriginPatch.cs` |
| Map markers for large militias | setting:ShowMilitiaMarkers, setting:TrackedSizeMinimum | `Managers\MilitiaTrackerManager.cs`, `Patches\MapTrackerProviderPatch.cs` (1.4.x only), `Patches\MapTrackerManagerPatch.cs` (1.5.7 only) |
| Militia banner on the map marker | setting:ShowMilitiaMarkers, setting:RandomBanners | `Patches\MapTrackerItemVMPatch.cs` |
| Raid messages (being raided, raid done, raiding your village) | setting:ShowRaids | `Behaviors\MilitiaBehavior.cs`, `Managers\BanditMilitiaManager.cs` (`Think`) |

## Vanilla safety / compat patches

| Feature | Gate | Files |
|---|---|---|
| Party component flags kept | always | `Patches\MobilePartyPatch.cs` (`MobilePartyUpdatePartyComponentFlagsPatch`) |
| Militias are never bandit boss parties | always | `Patches\MobilePartyPatch.cs` (`MobilePartyIsBanditBossPartyPatch`) |
| `MapFaction` null guard (owner clan → home owner → bandit faction) | always | `Patches\MobilePartyPatch.cs` (`MobilePartyMapFactionPatch`) |
| Party size limit raised to current size | setting:IgnoreSizePenalty | `Patches\PartyBasePatch.cs` (`PartyBasePartySizeLimitPatch`) |
| Relation changes with broken heroes skipped; exceptions swallowed and logged | always | `Patches\ChangeRelationActionPatch.cs` |
| AI loot-casualty skill XP null guard | always | `Patches\DefaultSkillLevelingManagerPatch.cs` |
| Militias excluded from the outlaws quest tick | always | `Patches\MerchantNeedsHelpWithOutlawsIssueQuestPatch.cs` |
| Serve as Soldier bait-run party (`SaSPublicApi.IsPartyReserved`) is not re-ordered, merged, split or engage-filtered | always (SaS installed) | `Compat\SaSCompat.cs`, `Behaviors\MilitiaBehavior.cs` (`OnTickPartialHourlyAiEvent`, `OnRaidCompletedEvent`), `Managers\BanditMilitiaManager.cs` (`TryFindAndMergeNearbyBandits`, `CanMobilePartiesMerge`, `TrySplitBanditMilitiaParty`, `ReleaseMilitiasRaidingLootedVillage`), `Patches\MobilePartyAiPatch.cs` |

## Console, diagnostics

| Feature | Gate | Files |
|---|---|---|
| Console: `bmr.stats` (versions, day, hero states, militias, clan table, counters since load, warnings), `bmr.settings`, `bmr.heroes`, `bmr.orphans`, `bmr.fix_orphans`; stats and settings also go to the log | always | `Diagnostics\ConsoleCommands.cs`, `Diagnostics\BmrCounters.cs` |
| Log file `Modules\BanditMilitiasRedux\logs\` (problems always, e.g. `BMR-LEADER-LOST`; `INFO` = command output, always; debug lines `BMR-STUCK`, `BMR-DESERTERS-SPAWN`, `BMR-MOUNT-NULLCHECK`, `BMR-REATTACH-VIOLATION`, `BMR-SAS-COMPAT` only when enabled) | setting:LoggingEnabled | `Diagnostics\BmrLog.cs`, `Core\ModInfo.cs` |

## Save data

| Data | Key / field | Files |
|---|---|---|
| Alive militia heroes | `Heroes` | `Behaviors\MilitiaBehavior.cs` |
| Reusable hero pool | `_waitingToBeReusedHeroes` | `Behaviors\ReusableHeroesBehavior.cs` |
| Notoriety | `_battlesWon`, `_firstSeen` (shown by `bmr.heroes`, no longer gives epithets), `_defeats`, `_beatenBy` | `Behaviors\NotorietyBehavior.cs` |
| Party component | SaveableField 1–7 | `Data\BanditMilitiaPartyComponent.cs` |
| Not saved (in memory): stuck tracker, patrol re-pick and idle retry times | — | `Core\Globals.cs` |
