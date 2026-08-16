# DSFix v1.7.2

Compatibility module for **Mount & Blade II: Bannerlord 1.3.15**, **The Old Realms: War in the Mountains 1.16**, and the **Distinguished Service 1.3.x fork** from Nexus mod 6007.

## What it fixes

- TOR summoned agents in Distinguished Service's post-battle result processing: the validated `ShowBattleResults` path contains three `IAgentOriginBase.BattleCombatant -> PartyBase` casts. DSFix rewrites those three conversions through an owner-party resolver so TOR summon wrappers resolve to the party that owns them.
- TOR promoted-troop names: promoted heroes use the source troop culture's gender-correct name pool and the localized source troop name as their title, e.g. `Aelar the Eonir Mounted Ranger`. The name is enforced before Distinguished Service creates the immediate skill-focus inquiry.
- Distinguished Service lord-promotion/flee handling: a defeated map-event roster can already have removed/depleted the wanderer when `PromotionManager.FleeToOtherClanLord` calls `TroopRoster.RemoveTroop`. Bannerlord 1.3.15 then reaches `AddToCountsAtIndex` with an invalid roster index and throws `IndexOutOfRangeException`. DSFix makes that specific removal idempotent when the troop is already absent and keeps a narrow fallback for the same native exception if the roster changes between preflight and removal.

## Installation

1. Delete the complete existing `Modules/DSFix` folder.
2. Extract the `DSFix` folder from the release archive into Bannerlord's `Modules` directory.
3. Enable DSFix and load it after `TOR_Core` and `DistinguishedService`.

## Diagnostics

Log file:

`Documents/Mount and Blade II Bannerlord/Configs/DSFix.log`

Successful startup should report the battle-result patch, the promoted-troop naming patches, and the lord-promotion roster patch.

## Save compatibility

A new campaign is not required. Existing promoted heroes are not renamed automatically.
