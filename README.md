# DSFix v1.7.9

Compatibility module for **Mount & Blade II: Bannerlord 1.3.15**, **The Old Realms: War in the Mountains 1.16**, and the **Distinguished Service 1.3.x fork** from Nexus mod 6007 (current 1.3.14 / 1.3.14-NoWarsails files).

## What it fixes

- TOR summoned agents in Distinguished Service's post-battle result processing: the validated `ShowBattleResults` path contains three `IAgentOriginBase.BattleCombatant -> PartyBase` casts. DSFix rewrites those three conversions through an owner-party resolver so TOR summon wrappers resolve to the party that owns them.
- TOR promoted-troop race/body identity: Distinguished Service creates a companion from a culture/sex-matched wanderer template. In TOR, one culture can contain multiple races, so a wraith, vampire, skeleton, Blood Dragon, or other custom-race troop can be cloned from a human wanderer. DSFix preserves the source troop's race and race-specific body range before Bannerlord initializes the hero, and constrains the generated hero age to the source body's valid age range.
- Distinguished Service companion semantics remain intact: the source troop is exposed as the clone's `OriginalCharacter` only during the body/culture initialization window, then the original wanderer origin is restored on every success/exception path. The companion therefore keeps Distinguished Service's wanderer occupation/template behavior.
- Corrected promoted race/body identity persists across saves. DSFix records the tracked companion's **current** race and body-property-range ID before save and reapplies them on session launch after Bannerlord reconstructs the hero from its wanderer origin. Later intentional race/body changes are captured on the next save instead of being overwritten with the original troop forever.
- TOR promoted-troop names: promoted heroes use the source troop culture's gender-correct name pool and the localized source troop name as their title, e.g. `Aelar the Eonir Mounted Ranger`. The name is enforced before Distinguished Service creates the immediate skill-focus inquiry.
- Bannerlord 1.3.15 `NameGenerator` compatibility: `GenerateHeroFirstName(Hero)` is an instance method in the target game build. DSFix binds the Harmony hook to the actual instance method.
- Distinguished Service variants that do not expose `PromotionManager.get_using_extern_namelist()`: the external-name-list bypass remains optional. Its absence does not abort the TOR promoted-name patch set.
- Distinguished Service post-map-event roster cleanup: the exact supported `DistinguishedService.dll` identifies wanderers that were present before battle and are already absent afterward, then attempts to remove those same absent wanderers again. The target binary contains **two** such `TroopRoster.RemoveTroop` call sites in `PromotionManager.MapEventEnded` and **three** in `PromotionManager.FleeToOtherClanLord`. DSFix rewrites exactly those five calls to require a positive live troop count before invoking Bannerlord's native removal. No global `TroopRoster` hook or exception suppression is installed.
- AI-promoted companions becoming permanent tavern residents after their party is destroyed: the supported Distinguished Service binary creates AI promotions as **wanderer companions**, and its defeat cleanup only handles promoted companions that are already missing from the defeated party at `MapEventEnded`. If a promoted companion is still in the roster at that point and the party is destroyed/disbanded afterward, Bannerlord can relocate the now-unassigned wanderer to a settlement. Vanilla AI does not later move that wanderer back into a clan party and does not select wanderers as lord-party commanders. DSFix tracks only successful AI promotions from the exact `PromotionManager.PromoteToParty` handoff and reattaches stranded tracked companions to a valid party of their **current** companion clan.

## AI promoted-companion recovery

The v1.7.9 recovery is lifecycle-based rather than a periodic tavern cleanup:

- one exact `AddHeroToPartyAction.Apply(Hero, MobileParty, bool)` call inside `DistinguishedService.PromotionManager.PromoteToParty` is rewritten so the native party handoff happens first and the resulting AI companion is then recorded;
- when a tracked companion's party is destroyed, DSFix attempts to move that hero to another active, non-disbanding lord party of the same current clan;
- if no safe party exists at destruction time, the hero remains tracked and native Bannerlord recovery is allowed to finish; once the hero is an unassigned settlement resident, a daily check reattaches them when a valid clan party becomes available;
- prisoners, governors, fugitives/released heroes still in native transition, player-clan companions, and heroes whose occupation is no longer `Wanderer` are not forcibly moved;
- the saved historical clan ID is diagnostic only. Recovery always follows `hero.CompanionOf`, so DSFix never pulls a companion back into a clan they have legitimately left.

Existing saves receive a one-time conservative migration for already-stranded **non-player clan wanderer companions** that are active, free, unassigned, non-governors, and currently in a settlement. The migration is persisted so unrelated future heroes are not repeatedly adopted by heuristic scanning.

## Installation

1. Delete the complete existing `Modules/DSFix` folder.
2. Extract the `DSFix` folder from the release archive into Bannerlord's `Modules` directory.
3. Enable DSFix and load it after `TOR_Core` and `DistinguishedService`.

## Diagnostics

Log file:

`Documents/Mount and Blade II Bannerlord/Configs/DSFix.log`

Successful startup should report the battle-result patch, promoted-troop identity/naming patches, exact roster rewrites of **2** `RemoveTroop` calls in `MapEventEnded` plus **3** in `FleeToOtherClanLord`, and exactly **1** AI-promotion `AddHeroToPartyAction.Apply` rewrite in `PromoteToParty`. Recovery events log the promoted hero and destination party.

## Save compatibility

A new campaign is not required.

Promotions made with v1.7.5 or later store their corrected race/body identity in the campaign save and restore it on load. Existing malformed companions created before v1.7.5 are left untouched because their original source troop cannot be identified reliably from the finished hero. Existing promoted heroes are not renamed automatically.

v1.7.9 also stores exact AI-promotion tracking state. Existing saves are repaired through the one-time stranded-companion migration described above.
