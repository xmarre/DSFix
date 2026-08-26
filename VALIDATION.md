# DSFix v1.7.8 validation

## Exact supported Distinguished Service binary

The supplied target `DistinguishedService.dll` has SHA-256:

`58cfbba78db17c3f26787cf3cb97e3ae0da4c68f9604517ce7f3347275bce184`

This binary is the basis for the v1.7.8 roster fix.

## Symptom

Post-battle campaign cleanup can report:

`System.IndexOutOfRangeException: Index was outside the bounds of the array.`

Observed stacks include both:

`TroopRoster.RemoveTroop(...) -> DistinguishedService.PromotionManager.MapEventEnded(...)`

and:

`TroopRoster.RemoveTroop(...) -> DistinguishedService.PromotionManager.FleeToOtherClanLord(...) -> PromotionManager.MapEventEnded(...)`

## Trigger

Distinguished Service records wanderers associated with a party before a map event, determines which of those wanderers are missing afterward, and performs post-event cleanup for those missing heroes.

## Root cause

Inspection of the exact target binary establishes the violated invariant:

> Distinguished Service calls `TroopRoster.RemoveTroop` for a wanderer that its own before/after comparison has already established is absent from the target roster.

The target binary contains exactly five relevant four-argument calls to:

`TroopRoster.RemoveTroop(CharacterObject, int, UniqueTroopDescriptor, int)`

inside the cleanup path:

- **2** calls in `PromotionManager.MapEventEnded(MapEvent)`;
- **3** calls in `PromotionManager.FleeToOtherClanLord(MapEventParty, CharacterObject)`.

When the wanderer has no positive live count, Bannerlord's `TroopRoster.RemoveTroop` can resolve an invalid internal index and fail in `AddToCountsAtIndex`.

The target Distinguished Service code already works from copied/snapshot wanderer lists in this logic. Mutation of the same forward-iterated list is therefore not the failure mechanism for this binary.

## v1.7.8 fix

`LordPromotionRosterPatch` now patches only the two Distinguished Service methods that contain the known-invalid calls.

Two Harmony transpilers replace the exact four-argument `RemoveTroop` call instruction with:

`DSFix.LordPromotionRosterPatch.RemoveTroopIfPresent(...)`

The replacement preserves the original stack signature and original arguments. It changes only the proven failing input:

1. when the roster and troop are non-null and `numberToRemove > 0`, read `TroopRoster.GetTroopCount(troop)`;
2. return without mutation when that positive removal request targets an already-absent troop;
3. call the original Bannerlord `TroopRoster.RemoveTroop` with the original `troop`, `numberToRemove`, `UniqueTroopDescriptor`, and `xp` for every other input.

Null arguments, non-positive removal counts, present troops, and any native errors outside the already-absent positive-removal case therefore retain Bannerlord's original behavior and failure semantics.

## Structural and patch-state fail-closed behavior

The transpilers enforce the validated target shape:

- `MapEventEnded` must contain exactly **2** matching `RemoveTroop` calls;
- `FleeToOtherClanLord` must contain exactly **3** matching `RemoveTroop` calls.

A different rewrite count throws `InvalidOperationException` during patch application. Patch installation also has an all-or-nothing invariant: both target transpilers are installed inside one guarded feature scope. If either Harmony patch operation fails, DSFix explicitly unpatches its transpiler from both target methods using the current Harmony owner ID and rethrows. `_patched` is set only after both patch operations succeed.

This prevents stale partial state where one DS cleanup method is corrected and the other still contains the invalid removals.

## Removed containment machinery

The v1.7.8 implementation does not install a Harmony patch on `TroopRoster.RemoveTroop` itself. The previous compatibility machinery is removed from this code path:

- no global roster prefix;
- no global roster finalizer;
- no event participant-roster collection;
- no thread-static map-event/flee scope;
- no `IndexOutOfRangeException` suppression;
- no broad fallback around unrelated roster operations.

Only the five validated Distinguished Service call sites are changed.

## Alternative hypotheses checked

- **Forward iteration changes indexes after each removal:** ruled out for the supplied binary's relevant wanderer selection logic because it iterates copied/snapshot lists.
- **TOR race/body promotion handling causes the roster crash:** unrelated. The failure occurs in post-map-event cleanup after the roster state has already changed.
- **TOR summoned-agent result conversion causes the roster crash:** unrelated. That compatibility path rewrites `IAgentOriginBase.BattleCombatant -> PartyBase` conversions in `ShowBattleResults`.
- **Bannerlord requires a global `RemoveTroop` workaround:** unsupported. The exact five Distinguished Service callers violate the precondition, so the fix belongs at those callers.

## Preserved behavior

The change does not alter:

- TOR summoned-agent result ownership conversion;
- culture-accurate promoted names;
- optional external name-list support;
- promoted TOR race/body preservation;
- body-compatible age generation;
- save/load persistence for corrected race/body identity;
- any `TroopRoster.RemoveTroop` call outside the five validated Distinguished Service cleanup sites.

## CI validation

GitHub Actions restores and builds both DSFix assemblies as `net472` against Bannerlord 1.3.15 reference assemblies. `tools/validate_release.py` verifies:

- release/module version consistency;
- exact `MapEventEnded(MapEvent)` and `FleeToOtherClanLord(MapEventParty, CharacterObject)` target discovery;
- exact four-argument `TroopRoster.RemoveTroop` overload discovery including `UniqueTroopDescriptor`;
- expected rewrite counts of 2 and 3;
- replacement with a same-signature static helper;
- compatibility suppression limited to the non-null, positive-count, already-absent troop case;
- native `RemoveTroop` fallback for null arguments, non-positive counts, and all other non-targeted inputs;
- all-or-nothing patch installation and rollback of both transpilers on failure;
- fail-closed structural mismatch handling;
- absence of the previous global roster hook, cleanup contexts, and exception suppression;
- all existing promoted-race, save/load, naming, summoned-agent, and package invariants.

## Remaining runtime verification

Compilation and structural validation can prove the rewrite shape. A Bannerlord 1.3.15 + TOR WiTM 1.16 + the exact supplied Distinguished Service binary run remains the final verification that the campaign feed no longer receives the previously observed `RemoveTroop`/`IndexOutOfRangeException` errors.
