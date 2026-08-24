## DSFix v1.7.5

Fixes TOR custom-race troops promoted through Distinguished Service becoming companions of the wrong race, including wraith/undead promotions turning human and the resulting malformed head/body combinations.

### Root cause

Distinguished Service does not create the companion from the promoted troop. `PromotionManager.PromoteUnit` first selects a wanderer template using culture, sex, and civilian-equipment criteria, then calls `HeroCreator.CreateSpecialHero(wanderer, ..., rand.Next(20, 50))`.

Bannerlord clones the selected wanderer's `Race`, `BodyPropertyRange`, and original-character link into the new hero. For wanderer templates, Bannerlord's hero-creation age model also ignores Distinguished Service's 20-49 age argument and derives age from the wanderer template itself. Distinguished Service later copies the source troop's culture, formation, equipment, level, and skills, but never repairs race/body identity.

That makes the result effectively random in TOR cultures containing wanderers of multiple races.

### Fix

v1.7.5 scopes a compatibility context to the exact `PromoteUnit -> CreateSpecialHero -> CreateHero` call. Only when the selected wanderer race differs from the promoted troop race, DSFix:

- applies the source troop's `Race` and `BodyPropertyRange` before hero initialization;
- clamps Distinguished Service's requested age to the source body's valid age range and evaluates it through the active `HeroCreationModel`;
- temporarily exposes the source troop as `OriginalCharacter` while Bannerlord derives culture and static body properties;
- restores the original wanderer `OriginalCharacter` immediately after `CreateSpecialHero`, including exception paths, preserving Distinguished Service's wanderer occupation/template semantics.

Corrected promoted identities are also save-persistent. DSFix tracks each corrected companion's current race and body-property-range ID, refreshes those values before save, and reapplies them on session launch after Bannerlord rebuilds the hero from its wanderer origin. This allows later intentional race/body changes to persist normally.

Existing malformed companions created before v1.7.5 are not rewritten automatically because their original promoted troop cannot be recovered safely from the finished hero.

The existing summoned-agent result fix, promoted-name compatibility, optional external-name-list handling, and exact-target lord-promotion roster guard remain unchanged.
