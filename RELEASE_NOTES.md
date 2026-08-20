## DSFix v1.7.3

Fixes the promoted-troop naming patch failing to initialize on Distinguished Service builds that do not expose:

`PromotionManager.get_using_extern_namelist()`

v1.7.2 treated that getter as a mandatory Harmony target. When it was absent, `LoreNamePatch.FindExternalNamesGetter` threw `MissingMethodException` before the remaining TOR naming hooks could be applied.

v1.7.3 makes the external-name-list bypass optional. The core `PromoteUnit`, `NameGenerator.GenerateHeroFirstName`, and `GetNameSuffix` patches still apply when the getter is absent, and the separate `DSFix.InBattleNaming` module continues to enforce the culture-appropriate promoted name before the skill inquiry.

The TOR summoned-agent post-battle fix and the exact-target `FleeToOtherClanLord` roster crash guard from v1.7.2 are unchanged.
