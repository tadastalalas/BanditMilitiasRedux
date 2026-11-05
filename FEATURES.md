# Bandit Militias Redux — Feature Map

Feature → gate → files. Read this first, then open only the files listed.
Gate: `setting:<Name>` = MCM property in `MCMSettings.cs`, accessed via `Globals.Settings`. `always` = no setting.
Tuning numbers (not in MCM) live in `Globals.cs`.

## Core / setup

| Feature | Gate | Files |
|---|---|---|
| Harmony `PatchAll`, behavior registration (`OnGameStart`), state reset on game end | always | `SubModule.cs` |
| Random banner cache (5000 banners) | setting:RandomBanners | `SubModule.cs`, `Globals.cs` |
| Random banners forced off when this mod loads after Calradia Expanded Kingdoms | always | `SubModule.cs` |
| Settings change → recalc raid cap, reset names, refresh markers | always | `SubModule.cs` |
| Ctrl+Alt+N hotkey: remove all militia content (`NukeEverything`) | always | `SubModule.cs`, `Helpers\Helper.cs` |
| Global constants, caches (`Hideouts`, `Villages`, `RaidCap`), in-memory AI trackers, `ClearGlobals` | always | `Globals.cs` |
| One-time mod state init (hideouts, villages, troops, templates, raid cap) | always | `Helpers\Helper.cs` |
| Save type definer (party component) | always | `BanditMilitiasSaveableTypeDefiner.cs`, `Constructors\BanditMilitiaPartyComponent.cs` |

## Spawning, merging, splitting

| Feature | Gate | Files |
|---|---|---|
| Nearby bandit parties merge into a Bandit Militia | setting:MergeableSize, setting:CooldownHours, setting:GlobalPowerPercent | `Managers\BanditMilitiaManager.cs`, `Behaviours\MilitiaBehavior.cs` |
| Merge home settlement: leader's birth place, else a land hideout | always | `Helpers\Helper.cs` (`BestSettlementForMerge`, `FindClosestHideout`, `IsLandHideout`) |
| Militia party component (owner, leader, home, banner, avoidance) | always | `Constructors\BanditMilitiaPartyComponent.cs` |
| Leader: reuse a waiting hero, else create one (cap per clan) | setting:HeroPoolCap, setting:FemaleSpawnChance | `Helpers\BanditHeroCreator.cs`, `Behaviours\ReusableHeroesBehavior.cs` |
| Militia splits in two | setting:RandomSplitChance, setting:CooldownHours | `Managers\BanditMilitiaManager.cs` |
| Leaderless militia disperses | always | `Managers\BanditMilitiaManager.cs` |
| Power calc: max party size, global power limit, average militia power | setting:GlobalPowerPercent | `Managers\PowerCalculationManager.cs` |
| Cached list of active militias (refreshed at most hourly) | always | `Managers\PowerCalculationManager.cs` (`GetActiveBanditMilitiaParties`) |

## Growth / training

| Feature | Gate | Files |
|---|---|---|
| Daily troop growth | setting:GrowthChance, setting:GrowthPercent, setting:GlobalPowerPercent | `Managers\BanditMilitiaManager.cs` (`TryGrowMilitiaParty`) |
| Daily training, upgrades, bonus XP | setting:CanTrain, setting:DailyTrainingChance, setting:UpgradeUnitsPercent, setting:MaxTrainingTier, setting:XpGift | `Managers\BanditMilitiaManager.cs` |
| Looters converted to culture recruits or bandit-tier troops | setting:CanTrain, setting:UseBanditOnlyTroopTrees | `Managers\BanditMilitiaManager.cs` |
| Equipment pool, cavalry count, gold and mounts | setting:MaxItemValue | `Helpers\EquipmentPool.cs`, `Managers\BanditMilitiaManager.cs` |

## Map AI (patrol / raid / stuck)

| Feature | Gate | Files |
|---|---|---|
| `Think`: idle → patrol near home; hideout arrival → hold; patrol → raid roll | always | `Managers\BanditMilitiaManager.cs` (`Think`) |
| Home = nearest active hideout of own bandit culture, else home settlement, else own position (land-reachable only) | always | `Managers\BanditMilitiaManager.cs` (`FindHomeSettlement`) |
| Patrol village near home (range 200, then 400), re-picked every 3 days; idle retry after 6 h | always | `Managers\BanditMilitiaManager.cs` (`TryStartHomePatrol`), `Globals.cs` |
| Raid: richest village (hearths) vs distance within 200, at war, up to 3 candidates | setting:AllowPillaging, setting:PillagingChance | `Managers\BanditMilitiaManager.cs` (`FindRaidTarget`, `FindBestRaidVillage`) |
| Raid cap (counts militias raiding or marching to raid) | setting:RaidCapPercent | `Managers\BanditMilitiaManager.cs` (`CountRaidingMilitias`), `Helpers\Helper.cs` (`CalculateRaidCap`) |
| Raid avoidance: one roll vs highest avoidance toward owner clan | always | `Managers\BanditMilitiaManager.cs` (`IsVillageAvoided`) |
| Release militias marching to a village that got looted | always | `Behaviours\MilitiaBehavior.cs` (`OnVillageStateChanged`), `Managers\BanditMilitiaManager.cs` (`ReleaseMilitiasRaidingLootedVillage`) |
| Raid done → unlock AI and hold | always | `Behaviours\MilitiaBehavior.cs` (`OnRaidCompletedEvent`) |
| Stuck rescue (12 h within 15 units) → patrol near home; after 2 rescues hold 6 h; logs `BMR-STUCK` | always | `Managers\BanditMilitiaManager.cs` (`TryHandleStuckMilitia`), `Globals.cs` |
| Partial-hourly AI: stuck check, drop peace engagements, unlock stale AI, merge | always | `Behaviours\MilitiaBehavior.cs` (`OnTickPartialHourlyAiEvent`) |
| Think on idle every AI tick (hot path) | always | `Patches\MilitiaPatches.cs` (`MobilePartyAiTickInternalPatch`) |
| Vanilla land-bandit patrol AI skipped for militias | always | `Patches\MilitiaPatches.cs` (`AiBanditPatrollingBehaviorAiHourlyTickPatch`) |
| Militias never enter settlements (hold instead) | always | `Patches\MilitiaPatches.cs` (`EnterSettlementActionApplyForPartyPatch`) |
| Militias are land-only (no naval navigation) | always | `Patches\MilitiaPatches.cs` (`MobilePartyHasNavalNavigationCapabilityPatch`) |
| Engage filter: skip villagers/caravans, avoided heroes, too strong/weak targets | setting:IgnoreVillagersCaravans, setting:MaxStrongerTargetPercent, setting:MaxWeakerTargetPercent | `Patches\MilitiaPatches.cs` (`MobilePartyAiSetAiBehaviorPatch`) |

## Avoidance

| Feature | Gate | Files |
|---|---|---|
| Weekly avoidance decay | always | `Managers\AvoidanceManager.cs`, `Behaviours\MilitiaBehavior.cs` (`OnDailyTickPartyEvent`) |
| Avoidance rises around a hero who destroyed a militia | always | `Managers\AvoidanceManager.cs`, `Behaviours\MilitiaBehavior.cs` (`OnMobilePartyDestroyed`) |
| Avoidance drops toward heroes the militia beat | always | `Managers\AvoidanceManager.cs`, `Patches\PrisonerPatches.cs` |

## Heroes

| Feature | Gate | Files |
|---|---|---|
| Hero creation: template, name, appearance, equipment, leader training | setting:FemaleSpawnChance, setting:MaxItemValue | `Helpers\BanditHeroCreator.cs`, `Helpers\HeroAppearanceService.cs`, `Helpers\EquipmentPool.cs` |
| Reusable hero pool (released, escaped, displaced leaders) | setting:HeroPoolCap | `Behaviours\ReusableHeroesBehavior.cs`, `Patches\PrisonerPatches.cs` |
| Leaders reserved for reuse on state change / leader removal | always | `Patches\PrisonerPatches.cs` (`HeroChangeStateReserveBanditLeaderPatch`, `MobilePartyRemovePartyLeaderReserveBanditLeaderPatch`) |
| Defection: hero leaving a bandit clan stops being tracked, may become wanderer | always | `Behaviours\HeroDefectionBehavior.cs` |
| Notoriety: wins, defeats, survival days, epithets, encyclopedia text | always | `Behaviours\NotorietyBehavior.cs`, `Patches\PrisonerPatches.cs`, `Patches\MilitiaPatches.cs` (`EncyclopediaHeroPageUpdateInformationTextPatch`) |
| Bounty for defeating a militia leader | setting:GoldReward | `Helpers\Helper.cs` (`GrantDefeatBounty`), `Patches\PrisonerPatches.cs` |
| Death cleanup; execution honor XP | setting:ExecutionHonorXp | `Patches\MilitiaPatches.cs` (`KillCharacterActionApplyInternalPatch`, `TraitLevelingHelperAddPlayerTraitXpPatch`, `ExecutionRelationModelGetRelationChangePatch`) |
| Home settlement forced to birth settlement | always | `Patches\MilitiaPatches.cs` (`HeroUpdateHomeSettlementPatch`) |
| No vanilla hero spawning / renown / fugitive handling for militia heroes | always | `Patches\MilitiaPatches.cs` (`HeroSpawnCampaignBehaviorOnHeroDailyTickPatch`, `GainRenownActionApplyInternalPatch`), `Patches\PrisonerPatches.cs` (`MakeHeroFugitiveActionApplyInternalPatch`) |
| Encyclopedia: militia heroes visible; bandit clan links blocked | always | `Patches\MilitiaPatches.cs` (`EncyclopediaHeroVisibilityPatch`, `EncyclopediaBanditClanLinkPatch`) |

## Prisoners

| Feature | Gate | Files |
|---|---|---|
| Defeated militia leaders captured by the player, capture conversation | always | `Patches\PrisonerPatches.cs` (`MapEventCalculateAndCommitMapEventResultsPatch`, `BanditInteractionsCampaignBehaviorOpenRosterScreenAfterBanditEncounterPatch`) |
| Militia heroes not freed by vanilla prisoner release (transpiler) | always | `Patches\PrisonerPatches.cs` (`PlayerEncounterDoFreeOrCapturePrisonerHeroesPatch`) |
| No duplicate hero prisoners | always | `Patches\PrisonerPatches.cs` (`PartyBaseAddPrisonerPatch`) |
| No ransom offers for militia heroes | always | `Patches\PrisonerPatches.cs` (`RansomOfferCampaignBehaviorConsiderRansomPrisonerPatch`) |
| Prisoner taken/released log messages hidden | setting:RemovePrisonerMessages | `Patches\MilitiaPatches.cs` (`DefaultLogsCampaignBehaviorOnPrisonerTakenPatch`, `DefaultLogsCampaignBehaviorOnHeroPrisonerReleasedPatch`) |

## Dialogs / encounters

| Feature | Gate | Files |
|---|---|---|
| Leader talk; keep / release / execute captive | setting:ExecutionHonorXp | `Behaviours\DialogsBehavior.cs` |
| Skip militia conversations on encounter | setting:SkipConversations | `Patches\MilitiaPatches.cs` (`EncounterManagerStartPartyEncounterPatch`, `PlayerEncounterDoMeetingInternalPatch`, `ConversationManagerGetSentenceMatchPatch`) |
| Female leaders use female voice lines | setting:CheckVoiceGender | `Patches\MilitiaPatches.cs` (`DefaultVoiceOverModelGetSoundPathForCharacterPatch`) |

## Map UI

| Feature | Gate | Files |
|---|---|---|
| Party names (with / without leader) | setting:BanditMilitiaString, setting:LeaderlessBanditMilitiaString | `Constructors\BanditMilitiaPartyComponent.cs`, `Patches\MilitiaPatches.cs` (`PartyNameplateVMRefreshDynamicPropertiesPatch`) |
| Militia banners on map icon and in battle | setting:RandomBanners | `Patches\MilitiaPatches.cs` (`MobilePartyVisualAddCharacterToPartyIconPatch`, `PartyGroupAgentOriginBannerGetterPatch`) |
| Map markers for large militias | setting:ShowMilitiaMarkers, setting:TrackedSizeMinimum | `Managers\MilitiaTrackerManager.cs`, `Patches\MilitiaPatches.cs` (`MapScreenOnInitializePatch`, `MapTrackerProviderCtorPatch`, `MapTrackerProviderCanAddMobilePartyPatch`, `MobilePartyTrackItemVMUpdatePropertiesPatch`) |
| Raid messages (being raided, raid done, raiding your village) | setting:ShowRaids | `Behaviours\MilitiaBehavior.cs`, `Managers\BanditMilitiaManager.cs` (`Think`) |

## Vanilla safety / compat patches

| Feature | Gate | Files |
|---|---|---|
| Party component flags kept | always | `Patches\MilitiaPatches.cs` (`MobilePartyUpdatePartyComponentFlagsPatch`) |
| Militias are never bandit boss parties | always | `Patches\MilitiaPatches.cs` (`MobilePartyIsBanditBossPartyPatch`) |
| `MapFaction` null guard (owner clan → home owner → bandit faction) | always | `Patches\MilitiaPatches.cs` (`MobilePartyMapFactionNullGuardPatch`) |
| Party size limit raised to current size | setting:IgnoreSizePenalty | `Patches\MilitiaPatches.cs` (`MobilePartyPartySizeLimitGetterPatch`) |
| Upgrade targets and upgrade permission | setting:UseBanditOnlyTroopTrees, setting:MaxTrainingTier | `Patches\MilitiaPatches.cs` (`PartyUpgraderCampaignBehaviorGetPossibleUpgradeTargetsPatch` transpiler, `DefaultPartyTroopUpgradeModelCanPartyUpgradeTroopToTargetPatch`) |
| Relation changes with broken heroes skipped; exceptions swallowed and logged | always | `Patches\MilitiaPatches.cs` (`ChangeRelationActionApplyInternalPatch`, `DefaultDiplomacyModelGetHeroesForEffectiveRelationPatch`) |
| AI loot-casualty skill XP null guard | always | `Patches\MilitiaPatches.cs` (`OnAIPartyLootCasualtiesPatch`) |
| Militias excluded from the outlaws quest tick | always | `Patches\MilitiaPatches.cs` (`MerchantNeedsHelpWithOutlawsIssueQuestHourlyTickParty`) |
| Disabled (commented out): militia hero full-name patch | — | `Patches\MilitiaPatches.cs` (`NameGenerator.GenerateHeroFullName`) |
| Unused: `PatchSaSDeserters` (not applied anywhere) | — | `Patches\MilitiaPatches.cs` |

## Console, settings, diagnostics

| Feature | Gate | Files |
|---|---|---|
| Console: `bmr.stats`, `bmr.heroes`, `bmr.orphans`, `bmr.fix_orphans` | always | `Helpers\ConsoleCommands.cs` |
| MCM settings (Spawning, Training, Power, AI, Appearance, UI, Advanced) | always | `MCMSettings.cs` |
| Chat log | setting:EnableChatLogging | `Helpers\Logs.cs` |
| File log (`Modules\BanditMilitiasRedux\logs\`); tags `BMR-STUCK`, `BMR-DESERTERS-SPAWN`, `BMR-MOUNT-NULLCHECK`, `BMR-RELATION-EXCEPTION` | setting:EnableFileLogging | `Helpers\Logs.cs`, `Managers\BanditMilitiaManager.cs`, `Behaviours\MilitiaBehavior.cs`, `Patches\MilitiaPatches.cs` |

## Save data

| Data | Key / field | Files |
|---|---|---|
| Alive militia heroes | `Heroes` | `Behaviours\MilitiaBehavior.cs` |
| Reusable hero pool | `_waitingToBeReusedHeroes` | `Behaviours\ReusableHeroesBehavior.cs` |
| Notoriety | `_battlesWon`, `_firstSeen`, `_defeats`, `_beatenBy` | `Behaviours\NotorietyBehavior.cs` |
| Party component | SaveableField 1–7 | `Constructors\BanditMilitiaPartyComponent.cs` |
| Not saved (in memory): stuck tracker, patrol re-pick and idle retry times | — | `Globals.cs` |
