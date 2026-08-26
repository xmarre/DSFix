# DSFix v1.7.8

Compatibility module for **Mount & Blade II: Bannerlord 1.3.15**, **The Old Realms: War in the Mountains 1.16**, and the **Distinguished Service 1.3.x fork** from Nexus mod 6007 (current 1.3.14 / 1.3.14-NoWarsails files).

## What it fixes

- TOR summoned agents in Distinguished Service's post-battle result processing: the validated `ShowBattleResults` path contains three `IAgentOriginBase.BattleCombatant -> PartyBase` casts. DSFix rewrites those three conversions through an owner-party resolver so TOR summon wrappers resolve to the party that owns them.
- TOR promoted-troop race/body identity: Distinguished Service creates a companion from a culture/sex-matched wanderer template. In TOR, one culture can contain multiple races, so a wraith, vampire, skeleton, Blood Dragon, or other custom-race troop can be cloned from a human wanderer. DSFix preserves the source troop's race and race-specific body range before Bannerlord initializes the hero, and constrains the generated hero age to the source body's valid age range.
- Distinguished Service companion semantics remain intact: the source troop is exposed as the clone's `OriginalCharacter` only during the body/culture initialization window, then the original wanderer origin is restored on every success/exception path. The companion therefore keeps Distinguished Service's wanderer occupation/template behavior.
- Corrected promoted race/body identity persists across saves. DSFix records the tracked companion's **current** race and body-property-range ID before save and reapplies them on session launch after Bannerlord reconstructs the hero from its wanderer origin. Later intentional race/body changes are captured on the next save instead of being overwritten with the original troop forever.
- TOR promoted-troop names: promoted heroes use the source troop culture's gender-correct name pool and the localized source troop name as their title, e.g. `Aelar the Eonir Mounted Ranger`. The name is enforced before Distinguished Service creates the immediate skill-focus inquiry.
- Bannerlord 1.3.15 `NameGenerator` compatibility: `GenerateHeroFirstName(Hero)` is an instance method in the target game build. DSFix binds the Harmony hook to the actual instance method.
- Distinguished Service variants that do not expose `PromotionManager.get_using_extern_namelist()`: the external-name-list bypass remains optional. Its absence does not abort the TOR promoted-name patch set.
- Distinguished Service post-map-event roster cleanup: the exact supported `DistinguishedService.dll` identifies wanderers that were present before battle and are already absent afterward, then attempts to remove those same absent wanderers again. The target binary contains **two** such `TroopRoster.RemoveTroop` call sites in `PromotionManager.MapEventEnded` and **three** in `PromotionManager.FleeToOtherClanLord`. DSFix rewrites exactly those five calls to require a positive live troop count before invoking Bannerlord's native removal. The rewrite count is enforced at patch time, so a changed Distinguished Service binary fails closed instead of receiving a partial compatibility patch. No global `TroopRoster` hook or exception suppression is installed.

## Installation

1. Delete the complete existing `Modules/DSFix` folder.
2. Extract the `DSFix` folder from the release archive into Bannerlord's `Modules` directory.
3. Enable DSFix and load it after `TOR_Core` and `DistinguishedService`.

## Diagnostics

Log file:

`Documents/Mount and Blade II Bannerlord/Configs/DSFix.log`

Successful startup should report the battle-result patch, the promoted-troop identity patch, the promoted-troop naming patches, and exact roster rewrites of **2** `RemoveTroop` calls in `MapEventEnded` plus **3** in `FleeToOtherClanLord`. When one of those calls targets an already-absent wanderer, DSFix logs that the invalid removal was skipped.

## Save compatibility

A new campaign is not required.

Promotions made with v1.7.5 or later store their corrected race/body identity in the campaign save and restore it on load. Existing malformed companions created before v1.7.5 are left untouched because their original source troop cannot be identified reliably from the finished hero. Existing promoted heroes are not renamed automatically.
