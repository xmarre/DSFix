# DSFix v1.7.3

Compatibility module for **Mount & Blade II: Bannerlord 1.3.15**, **The Old Realms: War in the Mountains 1.16**, and the **Distinguished Service 1.3.x fork** from Nexus mod 6007.

## What it fixes

- TOR summoned agents in Distinguished Service's post-battle result processing: the validated `ShowBattleResults` path contains three `IAgentOriginBase.BattleCombatant -> PartyBase` casts. DSFix rewrites those three conversions through an owner-party resolver so TOR summon wrappers resolve to the party that owns them.
- TOR promoted-troop names: promoted heroes use the source troop culture's gender-correct name pool and the localized source troop name as their title, e.g. `Aelar the Eonir Mounted Ranger`. The name is enforced before Distinguished Service creates the immediate skill-focus inquiry.
- Distinguished Service variants that do not expose `PromotionManager.get_using_extern_namelist()`: the external-name-list bypass is an optional compatibility hook in v1.7.3. Its absence no longer aborts the entire TOR promoted-name patch set; the core promotion, first-name, and suffix hooks still apply, while the separate direct naming module continues to enforce the source-culture name before the skill inquiry.
- Distinguished Service lord-promotion/flee handling: the reported `PromotionManager.FleeToOtherClanLord -> TroopRoster.RemoveTroop -> AddToCountsAtIndex` path can reach an invalid roster index after the ended map-event roster has changed. DSFix guards only the exact wanderer/roster pair from that flee call: an already-absent troop makes the redundant removal a no-op, and an `IndexOutOfRangeException` from native removal for that same exact pair is contained so the remaining Distinguished Service logic can continue. Unrelated roster operations keep native behavior.

## Installation

1. Delete the complete existing `Modules/DSFix` folder.
2. Extract the `DSFix` folder from the release archive into Bannerlord's `Modules` directory.
3. Enable DSFix and load it after `TOR_Core` and `DistinguishedService`.

## Diagnostics

Log file:

`Documents/Mount and Blade II Bannerlord/Configs/DSFix.log`

Successful startup should report the battle-result patch, the promoted-troop naming patches, and the lord-promotion roster patch. On a Distinguished Service build without `get_using_extern_namelist()`, v1.7.3 reports that the optional external-list hook was skipped instead of reporting that all promoted-name patches failed.

## Save compatibility

A new campaign is not required. Existing promoted heroes are not renamed automatically.
