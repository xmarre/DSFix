# DSFix v1.7.4 validation

## v1.7.4 Bannerlord 1.3.15 NameGenerator startup failure

A Bannerlord 1.3.15 + TOR WiTM 1.16 runtime reported while starting a new campaign:

`System.MissingMethodException: GenerateHeroFirstName(Hero)`

from:

`DSFix.LoreNamePatch.FindFirstNameTarget -> DSFix.LoreNamePatch.TryPatch -> DSFix.SubModule.TryPatchLoadedTargets`

### Root cause

The failure was in DSFix's reflection target lookup. `FindFirstNameTarget` searched `TaleWorlds.CampaignSystem.NameGenerator` using static binding flags.

Bannerlord 1.3.15 exposes the relevant method as:

`public TextObject NameGenerator.GenerateHeroFirstName(Hero hero)`

It is an **instance method**. Therefore the method existed in the target game assembly, but DSFix's static-only lookup could never find it and reported a misleading `MissingMethodException`.

The Bannerlord 1.3.15 decompiled API also shows `GenerateHeroNameAndHeroFullName(...)` calling `this.GenerateHeroFirstName(hero)`, independently confirming the instance semantics.

### v1.7.4 fix

- `FindFirstNameTarget` now enumerates `NameGenerator` instance methods.
- It still requires exactly one `GenerateHeroFirstName(Hero)` target and fails closed if the target is missing or ambiguous.
- The existing prefix/finalizer remains valid because Harmony maps `__0` to the method's `Hero` argument; the patch does not require the `NameGenerator` instance itself.
- Release validation now inspects the `FindFirstNameTarget` body and rejects a regression to static binding.

## v1.7.3 external-name-list compatibility failure

A previous runtime reported:

`System.MissingMethodException: get_using_extern_namelist()`

from `DSFix.LoreNamePatch.FindExternalNamesGetter`.

v1.7.3 made that Distinguished Service getter optional. The getter is patched only when present, while the underlying `using_extern_namelist` member is temporarily disabled/restored around an active TOR promotion when available. That compatibility path remains unchanged in v1.7.4.

## v1.7.2 lord-promotion roster failure

The reported stack was:

`TroopRoster.AddToCountsAtIndex -> TroopRoster.RemoveTroop -> PromotionManager.FleeToOtherClanLord -> PromotionManager.MapEventEnded`

The compatibility guard remains scoped to the exact wanderer and exact `MapEventParty.Troops` roster captured by `FleeToOtherClanLord`. An already-absent exact troop makes the redundant removal a no-op; only an `IndexOutOfRangeException` from native removal for that same exact pair is contained. Other troops, rosters, removal calls, and exception types retain native behavior.

## Compatibility preservation

v1.7.4 retains:

- the three-target TOR summoned-agent `ShowBattleResults` conversion patch;
- culture-accurate promoted first names and localized source-troop titles;
- direct pre-inquiry name enforcement through `DSFix.InBattleNaming`;
- the optional external-name-list compatibility path from v1.7.3;
- the exact-target `FleeToOtherClanLord` roster guard from v1.7.2;
- the thread-static linked promotion context that avoids the v1.7.1 `Stack<T>` loader regression.

## CI validation

GitHub Actions restores and builds both DSFix assemblies as `net472` against the Bannerlord 1.3.15 reference assemblies, then validates required compatibility hooks, the instance `GenerateHeroFirstName(Hero)` lookup, optional external-name handling, exact flee-target scoping, absence of the `Stack<T>` regression, and release-package structure.

## Runtime boundary

The supplied screenshot directly proves the v1.7.3 static-target lookup failure. The Bannerlord 1.3.15 API establishes the corrected instance-method signature. CI can validate compilation and patch structure, but a real Bannerlord run remains the final runtime check for the complete mod stack.
