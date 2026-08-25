## DSFix v1.7.6

Fixes the Distinguished Service `FleeToOtherClanLord` `System.IndexOutOfRangeException` still appearing in the campaign event feed with DSFix v1.7.5.

### What the new runtime evidence changed

The original report used a stack that reached Bannerlord's `TroopRoster.RemoveTroop -> AddToCountsAtIndex`, so v1.7.2 guarded that exact nested roster-removal operation.

The new v1.7.5 runtime screenshot repeatedly shows:

`System.IndexOutOfRangeException -> DistinguishedService.PromotionManager.FleeToOtherClanLord_Patch1 -> PromotionManager.MapEventEnded`

without the previously assumed `TroopRoster.RemoveTroop` frames. That establishes that the current Distinguished Service 1.3.14 flee cleanup has another stale index failure path that the exact `RemoveTroop` hook cannot contain.

### Fix

v1.7.6 keeps the existing exact wanderer/`MapEventParty.Troops` protection and adds a second, still narrowly scoped safety boundary:

- only the exact `DistinguishedService.PromotionManager.FleeToOtherClanLord(MapEventParty, CharacterObject)` method is affected;
- its Harmony finalizer restores DSFix's thread-local flee context before handling the exception;
- only `IndexOutOfRangeException` escaping that exact cleanup method is contained;
- every other exception type is propagated unchanged;
- unrelated `TroopRoster` operations remain native.

This matches the actual live failure boundary while avoiding a global array/roster exception suppressor.

The TOR summoned-agent result fix, promoted race/body identity preservation, save/load persistence, culture-accurate naming, and optional external-name-list handling remain unchanged.
