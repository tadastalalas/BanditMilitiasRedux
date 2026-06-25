---
apply: always
---

# BanditMilitiasRedux, GitHub Copilot Chat

## Project Overview
- Mount & Blade II: Bannerlord mod, targets `net472` and `net6.0`, C# 12.0.
- Uses **Harmony** for patching and **MCM** for mod configuration.

---

## Code Style
- Avoid using `var` for local variables.
- Prefer expression-bodied members for simple one-liners.
- Use C# 12 collection expressions `[.. items]` instead of `new List<T>(items)` or `.ToList()`.
- Use null-conditional and null-coalescing operators (`?.`, `??`) consistently.
- Prefer `internal` access modifier over `public` unless external exposure is required.
- Keep methods short and focused on a single responsibility.

---

## Design Principles

**Single Responsibility**, each class should have one reason to change. If a class is doing two unrelated things, split it.

**Open/Closed**, prefer extension over modification when adding new behaviour. Don't reach into existing classes to bolt on unrelated logic.

**Liskov Substitution**, subclasses must be fully usable wherever their base class is expected, without breaking behaviour.

**Interface Segregation**, keep interfaces small and focused. Don't force a class to implement methods it doesn't need.

**Dependency Inversion**, high-level logic should not depend on low-level implementation details. Keep classes as independent as possible.

**No unnecessary indirection**, avoid multi-hop delegation chains (A calls B which calls C). Delegation to a single responsible owner is fine; chains beyond that are not. For example, `Globals` properties that proxy to a single owning service are acceptable.

**Global state lives in `Globals.cs`**, all constants, configuration values, and shared state that are accessed externally must be declared or exposed through `Globals.cs`. Internal-only state within a service class may stay local to that class.

---

## Mod-Specific Conventions
- `IsBanditMilitiaParty()`, `IsBanditMilitiaHero()`, `IsBanditMilitiaCharacterObject()` are the canonical checks for whether a Hero or Party belongs to this mod, always use it instead of manual type checks.
- `GetBanditMilitiaParty()` retrieves the `BanditMilitiaPartyComponent` from a party.
- All mod behaviors extend `CampaignBehaviorBase`.
- Singleton instances use the `public static T? Instance { get; private set; }` pattern.
- Logging uses `BMRLog.WriteToChat(...)` and `BMRLog.WriteToFile(...)`.

---

## Harmony Patching
- Patches live in the `/Patches` folder.
- Prefer `Postfix` patches unless a `Prefix` or `Transpiler` is strictly necessary and justified.
- Always null-check all parameters inside patches before accessing them.
- Avoid Reflection on any code we control, use it only on vanilla private members when there is no better alternative.

---

## Performance
Be mindful of code that runs frequently. The following contexts are performance-sensitive, avoid allocations, LINQ chains, or heavy logic inside them:

`Tick`, `MissionTick`, `QuarterHourlyTick`, `HourlyTick`, `HourlyTickParty`, `HourlyTickSettlement`, `HourlyTickClan`, `AiHourlyTick`, `TickPartialHourlyAi`, `OnQuarterDailyPartyTick`, `DailyTick`, `DailyTickParty`, `DailyTickTown`, `DailyTickSettlement`, `DailyTickHero`, `DailyTickClan`, `WeeklyTick`.

- Check for values being reallocated inside loops that could be allocated once outside.
- Prefer `for` loops over `foreach` on hot paths where the collection is a `List<T>`.
- Do not introduce new NuGet packages without discussing it first.

---

## Responding to Requests
- When suggesting code changes, show only the method(s) or chunk(s) that need editing, never regenerate a whole class, but always show me in which class and method the changes need to be made.
- Always include a precise explanation of what changed and why.
- Never guess, I can always provide decompiled code using dnSpy.
- I'm editing code manually, so behave accordingly.
- Don't explain everything in prolonged texts. Explain in short, precise, concrete explanations. Without telling me stories.