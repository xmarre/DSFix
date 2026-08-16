# DSFix v1.7.2 validation

## Reported failure

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

## v1.7.1 compatibility preservation

The supplied Nexus v1.7.1 package was used as the behavioral baseline. v1.7.2 retains its module layout and dependencies, the validated summoned-agent conversion patch, source-culture promoted naming, pre-inquiry name enforcement, and `DSFix.log` diagnostics. The v1.7.1 `Stack<T>` metadata workaround is no longer required because the rebuilt source does not use `Stack<T>` for promotion context storage.

## CI validation

GitHub Actions restores and builds both DSFix assemblies as `net472` against the Bannerlord 1.3.15 reference assemblies, then validates required compatibility hooks, exact flee-target scoping, the absence of the v1.7.1 `Stack<T>` regression, and release-package structure.

## Remaining runtime uncertainty

The Bannerlord process and the reporter's exact Distinguished Service/TOR runtime cannot be executed in CI. A real in-game reproduction of the lord-promotion flee case remains the final runtime proof. The compatibility guard is intentionally scoped to the exact stack path reported so that this uncertainty does not justify a broader roster rewrite.
