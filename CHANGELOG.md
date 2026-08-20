# Changelog

## 1.7.3

- Fixed the startup warning/error `System.MissingMethodException: get_using_extern_namelist()` from `DSFix.LoreNamePatch.FindExternalNamesGetter` on Distinguished Service builds that do not expose that property getter.
- Made the Distinguished Service external-name-list bypass an optional naming hook instead of a hard prerequisite for the entire TOR promoted-troop naming patch set.
- The core `PromoteUnit`, `NameGenerator.GenerateHeroFirstName`, and `GetNameSuffix` hooks now continue to patch when `get_using_extern_namelist()` is absent.
- Preserved the separate direct pre-inquiry naming enforcement in `DSFix.InBattleNaming`, so source-culture promoted names do not depend on the external-name-list getter existing.
- Added release validation that rejects reintroducing a mandatory external-name-list getter dependency.

## 1.7.2

- Fixed the reported `System.IndexOutOfRangeException` from `TroopRoster.AddToCountsAtIndex` / `TroopRoster.RemoveTroop` inside Distinguished Service's `PromotionManager.FleeToOtherClanLord` path after the ended map-event roster has changed.
- Scoped the compatibility guard to the exact wanderer and exact `MapEventParty.Troops` roster captured by `FleeToOtherClanLord`; unrelated `TroopRoster.RemoveTroop` calls retain native behavior.
- Added a preflight that skips an already-satisfied removal when that exact wanderer is absent, plus a narrow `IndexOutOfRangeException` fallback for the same exact target if native removal still resolves a stale/invalid roster index.
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
