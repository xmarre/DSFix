# DSFix v1.7.3 validation

## v1.7.3 promoted-name startup failure

A Bannerlord 1.3.15 + TOR WiTM 1.16 runtime reported:

`System.MissingMethodException: get_using_extern_namelist()`

from:

`DSFix.LoreNamePatch.FindExternalNamesGetter -> DSFix.LoreNamePatch.TryPatch -> DSFix.SubModule.TryPatchLoadedTargets`

### Root cause

`LoreNamePatch` treated `DistinguishedService.PromotionManager.get_using_extern_namelist()` as a mandatory target before applying any of the TOR promoted-name patches. Some Distinguished Service builds do not expose that getter. Because target discovery threw before Harmony patching began, the complete `LoreNamePatch` set was rejected even though the required promotion, first-name, and suffix targets were available.

The external-name-list hook is not required for DSFix's direct source-culture naming enforcement. `DSFix.InBattleNaming` independently captures the promoted troop culture/title, assigns a culture-appropriate first name, patches `GetNameSuffix`, and enforces the corrected name before the skill inquiry.

### v1.7.3 fix

- `FindExternalNamesGetter` now returns `null` when the getter is absent instead of throwing `MissingMethodException`.
- Harmony patches `get_using_extern_namelist()` only when the method actually exists.
- `PromoteUnit`, `NameGenerator.GenerateHeroFirstName`, and `GetNameSuffix` remain mandatory and continue to fail closed on ambiguous/missing targets.
- Startup logging distinguishes an unavailable optional external-list hook from a failed promoted-name patch set.
- Release validation asserts that the getter remains optional.

This keeps compatibility strict around the actual naming entry points while avoiding a false hard dependency on one Distinguished Service implementation detail.

## Reported v1.7.2 lord-promotion failure

`System.IndexOutOfRangeException` originates in `TaleWorlds.CampaignSystem.Roster.TroopRoster.AddToCountsAtIndex`, called by `TroopRoster.RemoveTroop`, from `DistinguishedService.PromotionManager.FleeToOtherClanLord(MapEventParty, CharacterObject)` during `PromotionManager.MapEventEnded`.

## Symptom

The battle ends, Distinguished Service runs its lord-promotion/flee path, and a roster removal reaches an invalid native `TroopRoster` array index.

## Trigger established by the stack

The failing operation is the `TroopRoster.RemoveTroop` performed from `FleeToOtherClanLord` against an ended `MapEventParty` roster. That roster is already in the post-battle/map-event teardown lifecycle, where its contents may have been depleted or structurally changed before Distinguished Service performs its flee cleanup.

## Root cause boundary

The stack proves that Distinguished Service passes a roster-removal request whose resolved native roster index is no longer valid when `AddToCountsAtIndex` executes. The supplied material does not include the exact running `DistinguishedService.dll`, so the evidence does **not** distinguish among all states that can produce that invalid index (for example, an already-absent/depleted wanderer versus another stale descriptor/index state inside the ended map-event roster).

The compatibility invariant is still precise: the flee cleanup must not crash when its request targets the exact wanderer and exact ended map-event roster after that roster has already changed. The desired postcondition of this cleanup is that the wanderer is absent from that roster.

## Fix

The compatibility patch establishes a thread-local context only for the exact `PromotionManager.FleeToOtherClanLord(MapEventParty, CharacterObject)` call and captures both its `wanderer` argument and `MapEventParty.Troops` roster.

1. The global hook on the exact four-argument `TroopRoster.RemoveTroop(CharacterObject, ...)` overload is inert unless both the troop reference and roster reference match that captured flee context.
2. For that exact pair, DSFix first checks whether the troop is still present. If it is already absent, the redundant removal is skipped because the intended postcondition is already satisfied.
3. If native removal for that exact pair still raises `IndexOutOfRangeException` (including a stale index/descriptor state or a roster change after preflight), DSFix suppresses only that exception and lets the remainder of Distinguished Service's flee logic continue.
4. Other troops, other rosters, other `RemoveTroop` calls, and other exception types retain native behavior.

## Alternative hypotheses checked

A TOR summoned-agent cast failure is not this reported path: the stack reaches `PromotionManager.FleeToOtherClanLord` and native roster mutation, not `DSBattleLogic.ShowBattleResults`. The established summoned-agent transpiler remains separate and unchanged in purpose.

A global `TroopRoster` corruption workaround was rejected. The patch does not clamp indices, mutate roster internals, or swallow `IndexOutOfRangeException` for unrelated roster operations.

## Compatibility preservation

v1.7.3 retains the v1.7.2 module layout and dependencies, the validated summoned-agent conversion patch, the exact-target lord-promotion roster guard, source-culture promoted naming, pre-inquiry name enforcement, and `DSFix.log` diagnostics.

The v1.7.1 `Stack<T>` metadata workaround remains unnecessary because the rebuilt source does not use `Stack<T>` for promotion context storage.

## CI validation

GitHub Actions restores and builds both DSFix assemblies as `net472` against the Bannerlord 1.3.15 reference assemblies, then validates required compatibility hooks, exact flee-target scoping, optional external-name getter handling, the absence of the v1.7.1 `Stack<T>` regression, and release-package structure.

## Remaining runtime uncertainty

The Bannerlord process and the reporter's exact Distinguished Service/TOR runtime cannot be executed in CI. CI validates compilation, patch structure, and packaging. The supplied runtime error directly proves the missing-getter compatibility failure; a real in-game promotion remains the final runtime proof that the affected Distinguished Service build follows the corrected path without another version-specific mismatch.
