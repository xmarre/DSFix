# DSFix v1.7.5 validation

## Reported TOR promoted-race failure

### Symptom

A TOR troop promoted through Distinguished Service can become a companion with a different race from the source troop. Reported cases include a wraith becoming human, Blood Dragon/Drakenhof promotions becoming human, and malformed undead bodies such as an ethereal/skeletal body with a human or child-sized head.

### Trigger

The failure is possible when the promoted troop's TOR culture contains a Distinguished Service-compatible wanderer template whose `Race` differs from the source troop's `Race`.

### Contributing path

Distinguished Service's `PromotionManager.PromoteUnit(CharacterObject co, ...)`:

1. selects `wanderer` from `co.Culture.NotableAndWandererTemplates` using occupation, sex, and civilian-equipment checks;
2. falls back to a player-culture wanderer if no culture-local template is found;
3. calls `HeroCreator.CreateSpecialHero(wanderer, ..., rand.Next(20, 50))`;
4. later copies the source troop's culture, formation, equipment, level, and skills.

The selection predicate does not compare `Race`.

Bannerlord 1.3.15 `HeroCreator.CreateSpecialHero` clones the selected template through `CharacterObject.CreateFrom`. `BasicCharacterObject.FillFrom` copies `BodyPropertyRange`, `Race`, and the template age into the clone. The clone also retains the selected wanderer as `OriginalCharacter`.

Bannerlord's default hero creation then derives culture and static body properties from that `OriginalCharacter`. For a wanderer template, `DefaultHeroCreationModel.GetBirthAndDeathDay` uses the wanderer's age instead of the age argument passed by Distinguished Service.

TOR's race data confirms that several custom bodies require specific ages; examples in `tor_bodyproperties.xml` include wraith and skeleton at age 25 and vampire at age 22.

### Root cause

The promoted hero is initialized from the **wanderer template's identity**, while Distinguished Service only copies a subset of source troop data after hero initialization. In a multi-race TOR culture this violates the required invariant:

> A companion promoted from a troop must be initialized with a race, race-specific body range, and compatible age belonging to that source troop.

Culture/equipment reassignment after `CreateSpecialHero` cannot repair an already-generated body.

### Alternative hypotheses

- The existing DSFix name patches are not the cause. They temporarily change culture for name generation and do not write `Race`.
- Equipment copying is not the cause. Race/body fields are already cloned from the wanderer before Distinguished Service replaces battle/civilian equipment.
- Excluding wraiths from promotion avoids one trigger but leaves every other multi-race TOR culture exposed to the same template-selection defect.
- Permanently replacing the hero's `OriginalCharacter` with the source troop was rejected: Bannerlord restores occupation and other template fields from that origin after load, which would turn a Distinguished Service wanderer companion back into a soldier-template character.

## v1.7.5 fix

`PromotionIdentityPatch` scopes itself to the exact Distinguished Service promotion and claims only its first `CreateSpecialHero` call. A nested hero creation triggered later by an event cannot inherit the promotion context.

When the exact selected wanderer has the same race as the source troop, the new compatibility path does nothing.

When the races differ:

1. Distinguished Service's requested age is clamped to the source `BodyPropertyRange` age interval.
2. Immediately before the private `HeroCreator.CreateHero` constructs the hero, DSFix asks the active `HeroCreationModel` for birth/death times using the source troop and the clamped age.
3. Immediately after `CreateHero` returns, the new hero clone receives the source `Race` and `BodyPropertyRange`.
4. The clone's private `_originCharacter` is temporarily changed from the selected wanderer to the source troop. This makes Bannerlord's normal initialization derive culture/static body properties from the correct TOR race without reimplementing the body generator.
5. `CreateSpecialHero` postfix/finalizer restores the original wanderer origin on every normal/exception path.
6. Only race/body identity remains corrected. Distinguished Service's wanderer occupation/template semantics and its later equipment/skill logic remain unchanged.

The patch matches the exact template reference handed from `CreateSpecialHero` into private `CreateHero`; unrelated `HeroCreator` calls remain native.

## Save/load invariant

Bannerlord's `CharacterObject.InitializeHeroCharacterOnAfterLoad` restores clone fields from the permanent wanderer origin. TOR already works around this for `Race` with its own campaign race map, while `BodyPropertyRange` is not covered by that TOR behavior.

`PromotionIdentityCampaignBehavior` therefore tracks only companions whose promotion required the race/body correction:

- on promotion, capture the corrected hero's current race and `BodyPropertyRange.StringId`;
- immediately before each save, refresh those values from the live hero;
- on session launch, restore the saved race and body range after Bannerlord has reconstructed the hero.

Refreshing before save is intentional: a later legitimate race/body change is persisted instead of being overwritten with the original promoted troop forever.

Pre-v1.7.5 malformed companions are not automatically inferred or rewritten. The finished hero does not contain a reliable source-troop identifier, and equipment/name heuristics would risk modifying unrelated companions.

## CI validation

GitHub Actions restores and builds both DSFix assemblies as `net472` against Bannerlord 1.3.15 reference assemblies. `tools/validate_release.py` checks:

- the exact Distinguished Service promotion hook;
- one-shot `CreateSpecialHero` claiming;
- same-race no-op behavior;
- exact template-reference matching for private `CreateHero`;
- source `Race` / `BodyPropertyRange` application;
- source-compatible age evaluation through `HeroCreationModel`;
- temporary `_originCharacter` substitution plus restoration;
- campaign `OnBeforeSaveEvent` / `OnSessionLaunchedEvent` persistence;
- existing summoned-agent, naming, external-name-list, and lord-roster compatibility guards;
- release version/package structure.

## Runtime boundary

The code path and data mismatch are established from Distinguished Service source, Bannerlord 1.3.15 source, TOR source/data, and the reported visual failure. CI can prove compilation and patch structure. A real Bannerlord 1.3.15 + TOR WiTM 1.16 promotion remains the final runtime check for rendered race/body behavior and save/reload behavior.
