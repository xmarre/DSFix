# Changelog

## 1.7.2

- Fixed the `System.IndexOutOfRangeException` from `TroopRoster.AddToCountsAtIndex` / `TroopRoster.RemoveTroop` when Distinguished Service's `PromotionManager.FleeToOtherClanLord` processes a wanderer that is already absent from the ended map-event roster.
- Scoped the fix to `FleeToOtherClanLord`: unrelated `TroopRoster.RemoveTroop` calls retain native behavior.
- Added a preflight that skips only an already-satisfied removal and a narrow `IndexOutOfRangeException` fallback for a roster change between preflight and the native removal call.
- Rebuilt the repository from the Nexus v1.7.1 behavior so GitHub now contains the current TOR summoned-agent and promoted-name compatibility code instead of the obsolete 2025 source snapshot.
- Removed the v1.7.1 `ThreadLocal<Stack<object[]>>` implementation detail entirely; promotion naming now uses a thread-static linked context and therefore no longer needs the binary metadata rewrite that v1.7.1 required on Bannerlord 1.3.15.
- Added reproducible GitHub Actions build/validation and automatic GitHub release packaging for Bannerlord 1.3.15.

## 1.7.1

- Fixed the startup/shutdown `TypeInitializationException` in `DSFix.LoreNamePatch` on Bannerlord 1.3.15.
- Corrected the `Stack<T>` framework type reference from `mscorlib` to the netstandard 2.0 facade used by Bannerlord 1.3.15.
- Preserved the promoted-hero naming patches and TOR summoned-agent post-battle crash fix.

## 1.7.0

- Fixed the promoted hero still showing Distinguished Service's vanilla name in the immediate battle-end skill-focus inquiry.
- Patched the live suffix generator and pre-inquiry skill-assignment paths so the culture-accurate name exists before inquiry text is captured.
- First names use the source troop culture's male/female pool, with a same-culture same-sex wanderer-template fallback.
- The localized source troop name is used as the title.
