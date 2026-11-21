---
apply: always
---

# BanditMilitiasRedux: workspace instructions (they override the global file)

## Start here
- Read `FEATURES.md` (repo root) first. It maps every feature to its gate (setting) and files, so open only the files you need.
- Keep `FEATURES.md` up to date in the same change set.

## Identity
- <Prefix>: BMR (Bmr in type names, `bmr_` in dialog ids). <Root> namespace and ModuleId: BanditMilitiasRedux.
- Logger: `Diagnostics\BmrLog.cs` (Debug / Warn / Error; `BmrLog.IsDebugEnabled` before building strings on hot paths).
- Settings: `Core\ModSettings.cs` only (MCM when loaded, else `ModConfiguration.xml`). Tuning numbers: `Core\Balance.cs`.
  Runtime state and caches: `Core\Globals.cs`. `Core\ModInfo.cs` holds the ModuleId, Harmony id, file and folder names and the module root path.
- Patches: `SubModule.ApplyPatches` runs `PatchAll` in `OnGameStart`, once per game; `SubModule.RemovePatches` runs `UnpatchAll(own id)`
  in `OnGameEnd`. Harmony does not skip patches it already applied (a second `PatchAll` runs every patch twice), so never call
  `PatchAll` anywhere else. Never patch in `OnSubModuleLoad` (human bullet bug). Patches are inactive before `OnGameStart`.
- Harmony id: `ModInfo.HarmonyId` = "BanditMilitiasRedux.Harmony", never the ModuleId (the module loader patches under the ModuleId;
  `UnpatchAll(ModuleId)` removed them and the next loaded save got no `OnGameStart`, no behaviors, no patches).
- Optional-mod compat: `Compat\SaSCompat.cs` (Serve as Soldier, reflection lookup, no Harmony patch).

## Layout deviations
- `Managers\`: static game-logic services used by behaviors and patches (BanditMilitiaManager, AvoidanceManager,
  PowerCalculationManager, MilitiaTrackerManager, BanditHeroCreator, HeroAppearanceService). New militia logic goes here.
- Shared helpers used everywhere live in `Core\`: `Helper.cs`, `Extensions.cs`, `EquipmentPool.cs`.
- `Data\BanditMilitiaPartyComponent.cs`: the saved party component, next to its save definer.

## Multi-version (1.5.7, 1.4.8, 1.4.7)
- 1.4.7 / 1.4.8 behavior is the reference. 1.5.7 adaptations live only in `#else` / `#if !LOWER_THAN_1_5` and must behave the same.
- 1.4.x-only files: `Patches\MapTrackerProviderPatch.cs`, `Patches\DefaultExecutionRelationModelPatch.cs`,
  the banner class in `Patches\MobilePartyVisualPatch.cs`.
- 1.5.7-only files: `Patches\MapTrackerManagerPatch.cs`, `Patches\ExecutionCampaignBehaviorPatch.cs`, `Patches\MobilePartyVisualHelperPatch.cs`.
- Other `#if LOWER_THAN_1_5` blocks: `Core\Helper.cs` (banner cache argument, legacy party list), `Managers\MilitiaTrackerManager.cs`,
  `Behaviors\DialogsBehavior.cs` (execution scene date).

## Mod-specific conventions
- `IsBanditMilitiaParty()`, `IsBanditMilitiaHero()`, `IsBanditMilitiaCharacterObject()` (`Core\Extensions.cs`) are the canonical
  checks for whether a Hero or Party belongs to this mod; always use them instead of manual type checks.
- `GetBanditMilitiaParty()` retrieves the `BanditMilitiaPartyComponent` from a party.
- A party that holds the player prisoner (`IsHoldingPlayerPrisoner()`) or is in a battle is never merged, split or otherwise
  replaced: removing the captor party frees the player, and vanilla frees hero prisoners still listed in a removed party.
- Singleton behaviors use the `public static T? Instance { get; private set; }` pattern.
- Player notifications go through `Core\Texts.cs` + `InformationManager`; diagnostics only through `BmrLog`.

## Saves
- The mod saves data: SyncData keys `Heroes` (MilitiaBehavior), `_waitingToBeReusedHeroes` (ReusableHeroesBehavior),
  `_battlesWon`, `_firstSeen`, `_defeats`, `_beatenBy` (NotorietyBehavior); `BanditMilitiaPartyComponent` SaveableFields 1–7.
- `Data\BanditMilitiasSaveableTypeDefiner.cs`: base id 4320639, party component id 4320639. Append only.

## Kept exceptions (released identifiers, never change)
- MCM Id and FolderName "BanditMilitiasRedux" (equal to the ModuleId).
- MCM property names and types in `Data\MCMSettings.cs` (players' saved values), including the two `Dropdown<string>` properties.
- SyncData keys above (they predate the `bmr_<field>` rule).
- Behavior class names (data-store ids): MilitiaBehavior, ReusableHeroesBehavior, NotorietyBehavior, DialogsBehavior, HeroDefectionBehavior.
- Text ids `BM…` / `BMR…` without a random suffix (released). New ids use `BMR_` + 7 random characters.
  `BMPower` is shared by the "Power & Balance" group and the Global Power setting.
- Dialog line ids `bmr_…` in `Behaviors\DialogsBehavior.cs`.
- Encyclopedia sentences are joined with `Texts.Concat` (`BMR_TOQCjcE`): "{A} {B}", but "{A}{B}" in CNs, CNt, JP.

## Design principles
- Single responsibility; no multi-hop delegation chains (Globals proxies to one owning service are fine).
- Prefer extension over modification; keep classes independent.
