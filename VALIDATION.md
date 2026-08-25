# DSFix v1.7.6 validation

## Reported live failure

### Symptom

With DSFix v1.7.5 installed, the campaign event feed repeatedly reports:

`System.IndexOutOfRangeException: Index was outside the bounds of the array.`

followed by:

`DistinguishedService.PromotionManager.FleeToOtherClanLord_Patch1(PromotionManager this, MapEventParty p, CharacterObject wanderer)`

`DistinguishedService.PromotionManager.MapEventEnded(MapEvent me)`

### Trigger

Distinguished Service runs its AI companion/lord flee cleanup after a map event. The affected `FleeToOtherClanLord(MapEventParty, CharacterObject)` call operates on transient map-event state after the battle has ended.

### What v1.7.2 got too narrow

The earlier report included native `TroopRoster.RemoveTroop -> AddToCountsAtIndex` frames. v1.7.2 therefore protected the exact fleeing wanderer and exact `MapEventParty.Troops` roster around that nested removal. It skips an already-satisfied removal and contains `IndexOutOfRangeException` only from that exact `RemoveTroop` call.

The new v1.7.5 runtime evidence reaches the `FleeToOtherClanLord` Harmony wrapper and then `MapEventEnded` without the `TroopRoster.RemoveTroop` frames. The current Distinguished Service 1.3.14 implementation therefore has at least one additional stale index operation inside the same flee cleanup method.

### Root-cause boundary

The exact current Nexus `DistinguishedService.dll` is not available in this repository, and the public older Distinguished Service sources do not contain the current `FleeToOtherClanLord` implementation. The evidence establishes the failing method boundary and exception type, but does not justify inventing which internal array/list access failed.

The violated compatibility invariant is precise:

> Post-map-event `FleeToOtherClanLord` cleanup must not let a stale transient index escape into `MapEventEnded` and repeatedly surface to the campaign event feed.

## v1.7.6 fix

The existing exact-roster guard remains the first-line protection. A second guard is attached to the already exact Harmony target `DistinguishedService.PromotionManager.FleeToOtherClanLord(MapEventParty, CharacterObject)`:

1. `FleePrefix` establishes the thread-local context used by the exact nested roster guard.
2. `FleeFinalizer` always restores the previous context before making any exception decision.
3. If the exception escaping this exact method is `IndexOutOfRangeException`, the finalizer logs the boundary containment and returns `null` to Harmony.
4. Any other exception is returned unchanged.
5. Calls outside this exact Distinguished Service method are unaffected.

This is intentionally not a global `IndexOutOfRangeException` suppressor and does not weaken `TroopRoster` behavior elsewhere.

## Alternative hypotheses checked

- **DSFix race/body promotion changes:** unrelated. The live stack is in `MapEventEnded -> FleeToOtherClanLord`, outside `PromoteUnit -> HeroCreator` identity initialization.
- **The original TOR summoned-agent result cast:** unrelated. That failure is an `InvalidCastException` in `DSBattleLogic.ShowBattleResults`, not this `IndexOutOfRangeException` path.
- **Only the exact `RemoveTroop` call is failing:** contradicted by the new visible stack, which no longer contains the native removal frames that motivated v1.7.2.
- **Global roster corruption workaround:** rejected. v1.7.6 does not patch array indexing, clamp arbitrary indexes, or suppress failures outside `FleeToOtherClanLord`.

## Preserved v1.7.5 invariants

The promoted TOR race/body fix remains unchanged:

- one-shot context for the exact Distinguished Service promotion;
- same-race promotions stay native;
- race/body correction is scoped to the exact wanderer template passed into `HeroCreator`;
- source-compatible age handling uses the active `HeroCreationModel`;
- temporary `_originCharacter` substitution is restored on all exits;
- corrected race/body identity persists through save/load while later intentional changes can still be saved.

The culture-accurate naming path, optional external-name-list handling, and three-target summoned-agent `ShowBattleResults` conversion patch are unchanged.

## CI validation

GitHub Actions restores and builds both DSFix assemblies as `net472` against the Bannerlord 1.3.15 reference assemblies. `tools/validate_release.py` verifies:

- release/module version consistency;
- exact `FleeToOtherClanLord` target support;
- exact wanderer/roster nested `RemoveTroop` guard;
- `FleeFinalizer` context restoration before exception handling;
- method-boundary suppression limited to `IndexOutOfRangeException`;
- propagation of all other exception types;
- existing promoted-race, save/load, naming, external-name-list, summoned-agent, and package invariants.

## Runtime boundary

The new screenshot proves v1.7.5 did not contain the live `FleeToOtherClanLord` failure and establishes the corrected method-level boundary. CI can prove the v1.7.6 patch structure and compilation. A real Bannerlord 1.3.15 + TOR WiTM 1.16 + Distinguished Service 1.3.14 battle remains the final verification that the repeated red event-feed exception is gone and that the flee cleanup leaves no visible campaign-state regression.
