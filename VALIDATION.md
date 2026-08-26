# DSFix v1.7.7 validation

## Reported live failure

### Symptom

With v1.7.6 installed, the campaign event feed still reports:

`System.IndexOutOfRangeException: Index was outside the bounds of the array.`

The new stack is:

`TaleWorlds.CampaignSystem.Roster.TroopRoster.RemoveTroop_Patch1(TroopRoster this, CharacterObject troop, Int32 numberToRemove, UniqueTroopDescriptor troopSeed, Int32 xo)`

`DistinguishedService.PromotionManager.MapEventEnded(MapEvent me)`

### Trigger

The failure occurs during Distinguished Service's post-map-event AI promotion/cleanup callback. The exact `RemoveTroop` call is now directly under `MapEventEnded`, outside `FleeToOtherClanLord`.

### Root cause

v1.7.2 scoped `TroopRoster.RemoveTroop` protection to an active `FleeToOtherClanLord(MapEventParty, CharacterObject)` context. v1.7.6 additionally contained `IndexOutOfRangeException` at that exact flee method boundary.

The v1.7.6 stack proves a second call path exists: `MapEventEnded` itself performs a `RemoveTroop` on transient post-battle roster state. No `_currentFlee` context exists for that direct call, so the existing `RemoveTroopFinalizer` intentionally propagates the exception.

Public Distinguished Service source confirms that `MapEventEnded(MapEvent)` iterates `MapEventParty` objects from `me.PartiesOnSide(me.WinningSide)` and reads each party's transient `p.Troops` roster. The current Nexus build contains additional cleanup not present in that older public source. The live stack establishes that one direct cleanup removal can resolve an invalid index after the map-event roster has already changed.

The violated invariant is:

> A stale direct `RemoveTroop` performed by Distinguished Service while cleaning an ended `MapEvent` must not mutate or index a roster that no longer contains the requested troop, and protection must remain limited to roster instances owned by that exact ended event.

## v1.7.7 fix

`LordPromotionRosterPatch` now establishes two independent nested cleanup contexts:

1. `FleeToOtherClanLord(MapEventParty, CharacterObject)` keeps the existing exact wanderer/roster context.
2. `MapEventEnded(MapEvent)` captures the `MapEventParty.Troops` roster instances belonging to that exact event.

The `MapEventEnded` prefix collects event-owned rosters by reference:

- first from `MapEvent.Parties`;
- then from `PartiesOnSide(...)` for every enum side as a signature-based fallback;
- only objects whose runtime type is exactly `TaleWorlds.CampaignSystem.MapEvents.MapEventParty` contribute a `Troops` roster;
- duplicate roster references are removed by `ReferenceEquals` checks.

The global `TroopRoster.RemoveTroop` Harmony hook then remains inert unless either exact cleanup context matches:

- the existing flee path requires the exact wanderer and exact captured flee roster;
- the direct map-event path requires the `TroopRoster` instance to be one of the exact roster references captured from the active `MapEvent`.

For a matched cleanup call:

- if the requested troop is already absent, the removal is skipped before Bannerlord resolves an invalid internal index;
- if native `RemoveTroop` still throws `IndexOutOfRangeException`, the finalizer contains that exception for the matched roster only;
- every other exception type propagates unchanged;
- every unrelated `TroopRoster.RemoveTroop` call remains native.

`MapEventEndedFinalizer` restores the previous thread-local map-event context on all exits. Nested `FleeToOtherClanLord` calls keep their separate linked context and restore independently.

## Alternative hypotheses checked

- **The v1.7.6 flee boundary is failing to run:** the latest stack has no `FleeToOtherClanLord` frame. This is a separate direct `MapEventEnded -> RemoveTroop` path.
- **The race/body promotion patch causes the failure:** the stack is in post-map-event roster mutation and does not enter `PromoteUnit -> HeroCreator` identity initialization.
- **The TOR summoned-agent cast fix causes the failure:** that path concerns `DSBattleLogic.ShowBattleResults` and `InvalidCastException`, not `TroopRoster.RemoveTroop`.
- **A global `RemoveTroop` exception suppressor is required:** rejected. The live evidence provides an exact owner method and exact map-event roster family, so the patch can remain reference-scoped.

## Preserved invariants

The existing compatibility behavior remains unchanged outside the failing path:

- TOR summoned-agent result ownership conversion;
- culture-accurate promoted names;
- optional external name-list support;
- promoted TOR race/body preservation;
- body-compatible age generation;
- save/load persistence for corrected race/body identity;
- exact `FleeToOtherClanLord` cleanup protection.

## CI validation

GitHub Actions restores and builds both DSFix assemblies as `net472` against Bannerlord 1.3.15 reference assemblies. `tools/validate_release.py` verifies:

- release/module version consistency;
- exact `MapEventEnded(MapEvent)` target discovery;
- exact `FleeToOtherClanLord(MapEventParty, CharacterObject)` target discovery;
- map-event roster capture from `Parties` plus `PartiesOnSide` fallback;
- exact `MapEventParty` type filtering;
- roster reference-identity matching;
- `RemoveTroop` suppression gated by the combined protected-cleanup matcher;
- propagation of unmatched exceptions and unrelated roster calls;
- linked cleanup-context restoration;
- all existing promoted-race, save/load, naming, summoned-agent, and package invariants.

## Runtime boundary

The latest screenshot establishes the missing direct `MapEventEnded -> RemoveTroop` path and explains why v1.7.6 could not match it. CI can prove target discovery, compilation, and patch scoping. A real Bannerlord 1.3.15 + TOR WiTM 1.16 + Distinguished Service 1.3.14 battle remains the final runtime verification that the repeated red `RemoveTroop_Patch1 -> MapEventEnded` error is gone.
