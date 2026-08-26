## DSFix v1.7.8

Replaces the previous event-scoped `TroopRoster` containment with an exact root-cause rewrite for the supported Distinguished Service 1.3.14 binary.

### Exact target binary

The supplied `DistinguishedService.dll` has SHA-256:

`58cfbba78db17c3f26787cf3cb97e3ae0da4c68f9604517ce7f3347275bce184`

Inspection of that exact binary establishes the failure mechanism.

### Root cause

Distinguished Service snapshots wanderers before the map event and compares them with the post-event roster. The resulting missing-wanderer list contains heroes that are already absent from the defeated party roster. Its cleanup then calls `TroopRoster.RemoveTroop` for those same missing wanderers.

The supported binary contains five such removal sites:

- **2** in `PromotionManager.MapEventEnded(MapEvent)`;
- **3** in `PromotionManager.FleeToOtherClanLord(MapEventParty, CharacterObject)`.

This violates the roster invariant before Bannerlord enters `TroopRoster.RemoveTroop`: the requested troop has no positive live count in the target roster. Bannerlord can then resolve an invalid internal roster index and throw `IndexOutOfRangeException`.

The code already works from copied/snapshot wanderer lists, so forward iteration over a mutating list is not the cause for this binary.

### Fix

v1.7.8 removes the v1.7.7 global `TroopRoster.RemoveTroop` hook, map-event roster capture, linked thread-local cleanup contexts, and `IndexOutOfRangeException` suppression.

Two Harmony transpilers now target only the exact Distinguished Service methods above. They replace each matching four-argument `TroopRoster.RemoveTroop(CharacterObject, int, UniqueTroopDescriptor, int)` call with a DSFix helper that:

1. applies the compatibility check only for a non-null roster, non-null troop, and positive removal count;
2. checks the target roster's current `GetTroopCount` for that wanderer;
3. returns without mutation only when that positive removal request targets an already-absent wanderer;
4. invokes Bannerlord's original `RemoveTroop` with every original argument for all other inputs, preserving native behavior and failure semantics outside the proven failing case.

The transpilers require exactly **2** rewrites in `MapEventEnded` and exactly **3** in `FleeToOtherClanLord`. Any structural mismatch throws during patch application. Patch application is atomic at the feature level: if either target cannot be rewritten, DSFix removes any transpiler it already installed on the other target and rethrows, so the game never runs with only part of the five-site fix.

All other `TroopRoster.RemoveTroop` calls remain completely native. No exception type is suppressed.

The TOR summoned-agent result fix, promoted race/body identity preservation, save/load persistence, culture-accurate naming, and optional external-name-list handling are unchanged.
