# Changelog

## 1.7.8

- Replaced the v1.7.7 event-scoped `TroopRoster` containment with an exact root-cause compatibility rewrite against the supplied Distinguished Service 1.3.14 binary (`SHA-256 58cfbba78db17c3f26787cf3cb97e3ae0da4c68f9604517ce7f3347275bce184`).
- Confirmed the supported binary builds a list of wanderers that were present before battle and are already absent afterward, then attempts to remove those same missing wanderers again from party rosters.
- Confirmed the binary contains exactly five affected four-argument `TroopRoster.RemoveTroop` call sites: 2 in `PromotionManager.MapEventEnded` and 3 in `PromotionManager.FleeToOtherClanLord`.
- Confirmed Distinguished Service already works from copied/snapshot wanderer lists in this path; forward iteration over a collection being mutated is not the cause of this binary's failure.
- Replaced exactly those five calls with a helper that checks `TroopRoster.GetTroopCount` and skips the invalid removal when the target wanderer has no positive live count. When the troop is still present, Bannerlord's native `RemoveTroop` receives the original arguments unchanged.
- Removed the global `TroopRoster.RemoveTroop` Harmony hook, participant-roster capture, thread-local event/flee contexts, and `IndexOutOfRangeException` suppression introduced by the earlier containment approach.
- Added strict structural guards requiring exactly 2 rewrites in `MapEventEnded` and exactly 3 in `FleeToOtherClanLord`; patch application fails closed if a future Distinguished Service binary no longer matches the validated layout.
- Updated release validation to reject reintroduction of broad roster hooks or exception suppression and to require the exact five-site rewrite plus native-present-troop fallback.

## 1.7.7

- Fixed the remaining live `System.IndexOutOfRangeException` path shown after v1.7.6: `TroopRoster.RemoveTroop_Patch1 -> DistinguishedService.PromotionManager.MapEventEnded`.
- The new stack establishes a separate direct `RemoveTroop` call from `MapEventEnded`; it does not run inside `FleeToOtherClanLord`, so the v1.7.2 exact-flee roster context and the v1.7.6 flee method-boundary guard cannot match it.
- Added an exact `PromotionManager.MapEventEnded(MapEvent)` scope that captures only rosters owned by parties participating in that same ended event: each `MapEventParty.Troops` roster and the corresponding participant `PartyBase.MemberRoster`. The current Nexus DLL is not available as source and the stack does not expose the `TroopRoster` instance, so covering both exact participant-owned roster candidates avoids guessing which one the live cleanup removes from.
- Participant discovery uses `MapEvent.Parties`, with `PartiesOnSide` as a signature-based fallback. Roster matching is by reference identity, not troop/culture heuristics.
- The existing global `TroopRoster.RemoveTroop` Harmony hook remains inert outside Distinguished Service cleanup. During the exact `MapEventEnded` scope it preflights only captured participant rosters, skips an already-absent troop, and contains only `IndexOutOfRangeException` from `RemoveTroop` on those exact roster instances.
- The exact `FleeToOtherClanLord` protection remains unchanged and composes with the new map-event scope. Thread-local contexts are restored on all exits, including nested/re-entrant cleanup. Participant-roster enumeration also fails open so stale event enumeration cannot introduce a new crash.
- Added release validation for the exact `MapEventEnded` signature, transient and live participant-roster capture, reference-identity matching, narrow `RemoveTroop` suppression, context lifetime, and exception propagation outside the protected cleanup scope.

## 1.7.6

- Fixed the `System.IndexOutOfRangeException` still escaping from `DistinguishedService.PromotionManager.FleeToOtherClanLord` on the live Distinguished Service 1.3.14 path after v1.7.2-v1.7.5.
- The new runtime evidence shows the exception reaching `FleeToOtherClanLord_Patch1 -> MapEventEnded` without the previously assumed `TroopRoster.RemoveTroop -> AddToCountsAtIndex` frames. The v1.7.2 guard was therefore too narrow: it protected one known nested roster-removal operation, while another stale index operation inside the same post-map-event flee cleanup can also fail.
- `FleeToOtherClanLord` now has an exact method-boundary finalizer that restores DSFix's thread-local context first, suppresses only `IndexOutOfRangeException` escaping that exact Distinguished Service cleanup method, and propagates every other exception unchanged.
- The existing exact wanderer/roster `RemoveTroop` preflight and finalizer remain in place as the first-line guard; unrelated `TroopRoster` operations are still untouched.
- Updated the supported Distinguished Service description to the current Nexus 1.3.14 / 1.3.14-NoWarsails files instead of the stale v7.4.0 label used by older DSFix documentation.
- Added release validation for the exact `FleeToOtherClanLord` boundary guard and cleanup ordering.

## 1.7.5

- Fixed Distinguished Service promotions losing TOR races when a culture contains wanderer templates from a different race, including wraith/undead promotions becoming human and the associated malformed body/head combinations.
- Root cause: `PromotionManager.PromoteUnit` selects a wanderer template by culture/sex and `HeroCreator.CreateSpecialHero` clones that template's `Race`, `BodyPropertyRange`, and wanderer age path. Distinguished Service later copies culture/equipment/skills, but never restores the promoted troop's race/body identity.
- The compatibility hook now scopes itself to the exact `PromoteUnit -> CreateSpecialHero -> CreateHero` call. When the selected wanderer race differs from the source troop, the hero clone receives the source race/body range before hero initialization, and Bannerlord temporarily sees the source troop as `OriginalCharacter` while it derives culture/static body properties.
- The temporary `OriginalCharacter` substitution is always restored to Distinguished Service's wanderer origin after `CreateSpecialHero`, including exception paths, so companion occupation/template behavior and save-load initialization remain Distinguished Service-compatible.
- Fixed the related custom-race age mismatch. Distinguished Service's requested 20-49 age is clamped to the source `BodyPropertyRange` age interval and evaluated through the active `HeroCreationModel`, preventing exact-age TOR bodies such as wraith/skeleton/vampire from inheriting an unrelated wanderer's age.
- Added campaign persistence for corrected promotions. DSFix saves each tracked companion's current race and `BodyPropertyRange` ID, reapplies them on session launch after Bannerlord reconstructs the hero from its wanderer origin, and refreshes the stored values immediately before save so intentional later race/body changes remain persistent.
- Existing malformed companions created before v1.7.5 are not guessed or rewritten automatically because the original promoted troop identity is not recoverable safely from the finished hero.
- Added release validation for the one-shot promotion scope, exact template match, temporary-origin restoration, source-compatible age path, and save/load race/body persistence.

## 1.7.4

- Fixed `System.MissingMethodException: GenerateHeroFirstName(Hero)` from `DSFix.LoreNamePatch.FindFirstNameTarget` when starting a Bannerlord 1.3.15 campaign.
- Corrected the Harmony target lookup to match Bannerlord 1.3.15's actual API: `NameGenerator.GenerateHeroFirstName(Hero)` is an instance method, not a static method.
- Added release validation that rejects changing the Bannerlord 1.3.15 first-name target back to a static lookup.
- Preserved the v1.7.3 optional external-name-list compatibility path, the TOR summoned-agent post-battle fix, the promoted-name enforcement, and the exact-target `FleeToOtherClanLord` roster crash guard.

## 1.7.3

- Fixed the startup warning/error `System.MissingMethodException: get_using_extern_namelist()` from `DSFix.LoreNamePatch.FindExternalNamesGetter` on Distinguished Service builds that do not expose that property getter.
- Made the Distinguished Service external-name-list bypass an optional naming hook instead of a hard prerequisite for the entire TOR promoted-name patch set.
- The core `PromoteUnit`, `NameGenerator.GenerateHeroFirstName`, and `GetNameSuffix` hooks now continue to patch when `get_using_extern_namelist()` is absent.
- Preserved the separate direct pre-inquiry naming enforcement in `DSFix.InBattleNaming`, so source-culture promoted names do not depend on the external-name-list getter existing.
- Added release validation that rejects reintroducing a mandatory external-name-list getter dependency.

## 1.7.2

- Added protection for the reported `System.IndexOutOfRangeException` from `TroopRoster.AddToCountsAtIndex` / `TroopRoster.RemoveTroop` inside Distinguished Service's `PromotionManager.FleeToOtherClanLord` path after the ended map-event roster has changed.
- Scoped that first-line compatibility guard to the exact wanderer and exact `MapEventParty.Troops` roster captured by `FleeToOtherClanLord`; unrelated `TroopRoster.RemoveTroop` calls retain native behavior.
- Added a preflight that skips an already-satisfied removal when that exact wanderer is absent, plus a narrow `IndexOutOfRangeException` fallback for the same exact target if native removal still resolves a stale/invalid roster index.
- Rebuilt the repository from the Nexus v1.7.1 behavior so GitHub contains the current TOR summoned-agent and promoted-name compatibility code instead of the obsolete 2025 source snapshot.
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
