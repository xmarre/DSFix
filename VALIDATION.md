# DSFix v1.7.2 validation

## Reported failure

`System.IndexOutOfRangeException` originates in `TaleWorlds.CampaignSystem.Roster.TroopRoster.AddToCountsAtIndex`, called by `TroopRoster.RemoveTroop`, from `DistinguishedService.PromotionManager.FleeToOtherClanLord(MapEventParty, CharacterObject)` during `PromotionManager.MapEventEnded`.

## Symptom

The battle ends, Distinguished Service runs its lord-promotion/flee path, and a roster removal crashes with an array index outside the native `TroopRoster` bounds.

## Trigger

`FleeToOtherClanLord` attempts to remove its `wanderer` from the ended `MapEventParty` troop roster after the battle outcome has already depleted or removed that character from the roster.

## Root cause

The Distinguished Service path assumes the ended map-event roster still contains the wanderer. On Bannerlord 1.3.15 that assumption is not stable at this lifecycle point. Native `TroopRoster.RemoveTroop` does not tolerate the resulting missing index on this overload and reaches `AddToCountsAtIndex` with an invalid index.

## Violated invariant

A `TroopRoster.RemoveTroop` call must address an element that is still present in that roster. The caller is allowed to want the postcondition "wanderer absent"; it cannot assume that the removal operation is still necessary after map-event teardown has mutated the roster.

## Fix

The compatibility patch marks only the dynamic extent of `PromotionManager.FleeToOtherClanLord`. While inside that method:

1. Before the exact four-argument `TroopRoster.RemoveTroop(CharacterObject, ...)` overload runs, DSFix checks whether the troop is still present.
2. If the troop is already absent, DSFix skips that one removal. The intended postcondition is already true and the remainder of Distinguished Service's flee logic continues.
3. If the roster changes after the preflight and native removal still raises `IndexOutOfRangeException`, DSFix suppresses only that exception while still inside `FleeToOtherClanLord`.
4. Every other roster-removal path keeps native behavior.

## Alternative hypothesis checked

A generic TOR summoned-agent cast failure is not the reported path: the stack reaches `PromotionManager.FleeToOtherClanLord` and native roster mutation, not `DSBattleLogic.ShowBattleResults`. The established summoned-agent transpiler remains separate and unchanged in purpose.

A global `TroopRoster` corruption workaround was also rejected. The patch does not clamp indices, alter roster internals, or swallow `IndexOutOfRangeException` outside the exact Distinguished Service flee path.

## v1.7.1 compatibility preservation

The Nexus v1.7.1 package was used as the behavioral baseline. v1.7.2 retains the validated summoned-agent conversion patch, source-culture promoted naming, pre-inquiry name enforcement, module dependencies, and diagnostics. The v1.7.1 `Stack<T>` metadata workaround is no longer required because the source no longer uses `Stack<T>` for promotion context storage.

## Remaining runtime uncertainty

The Bannerlord process and the user's exact Distinguished Service/TOR runtime cannot be executed in CI. CI builds against the published Bannerlord 1.3.15.110062 reference assemblies and validates the release layout and patch targets statically. The reported `FleeToOtherClanLord(MapEventParty, CharacterObject)` signature is taken directly from the supplied crash stack.
