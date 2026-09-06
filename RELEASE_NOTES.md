## DSFix v1.7.9

Fixes Distinguished Service AI-promoted companions becoming permanent tavern residents after their party is defeated and later destroyed/disbanded.

### Exact target binary

The supported `DistinguishedService.dll` has SHA-256:

`58cfbba78db17c3f26787cf3cb97e3ae0da4c68f9604517ce7f3347275bce184`

### Root cause

The exact Distinguished Service 1.3.14 binary creates AI promotions as **wanderer companions** and adds them to the promoting lord's clan and party.

Its defeat cleanup snapshots wanderer companions at map-event start and, at `MapEventEnded`, processes only those that are **already absent** from the defeated party roster. A promoted companion that is still present at that instant is therefore outside that cleanup. If Bannerlord destroys/disbands the party afterward, the companion can become unassigned and be relocated to a settlement.

Bannerlord 1.3.15 does not naturally recover that state: wanderers are excluded from normal autonomous settlement movement, and normal AI lord-party commander selection requires `Occupation.Lord`. The result can be a permanent tavern resident that still belongs to an AI clan.

### Fix

v1.7.9 adds exact lifecycle tracking and recovery:

- DSFix transpiles only `DistinguishedService.PromotionManager.PromoteToParty(CharacterObject, MobileParty)`.
- The supported method must contain exactly **one** `AddHeroToPartyAction.Apply(Hero, MobileParty, bool)` call. A different shape fails closed.
- That call is replaced by a same-signature helper that executes Bannerlord's native `AddHeroToPartyAction.Apply` first and records the AI promotion only after the native party handoff succeeds.
- No global `AddHeroToPartyAction` hook is installed.
- Exact tracked AI promotions are persisted in the campaign save.
- When a tracked hero's party is destroyed, DSFix tries to transfer that companion to another active, non-disbanding, non-battle lord party of the hero's **current** companion clan.
- If no safe destination exists at destruction time, native Bannerlord recovery is allowed to continue. The hero remains tracked and is reattached once they become a free, unassigned settlement resident and a valid clan party becomes available.
- Prisoners, governors, player-clan companions, transitional fugitive/released/traveling heroes, and heroes that no longer use `Occupation.Wanderer` are left alone.
- Recovery never restores a saved historical clan. It always follows the current `Hero.CompanionOf`, so legitimate ownership changes are respected.

### Existing saves

Existing v1.7.8-and-earlier saves did not contain exact AI-promotion tracking. v1.7.9 therefore performs a **one-time persisted migration** for the narrow already-broken state: active, free, non-player clan wanderer companions with no party or governor that are currently resident in a settlement. Those heroes are adopted into the recovery set and reattached when a valid clan lord party exists.

The migration runs only once per save; future unrelated heroes are not repeatedly discovered through heuristic scanning.

### Preserved fixes

The v1.7.8 exact five-site `RemoveTroop` root fix remains unchanged, including the 2 validated calls in `MapEventEnded` and 3 in `FleeToOtherClanLord`. TOR summoned-agent handling, promoted race/body identity, body-compatible age generation, save/load persistence, culture-accurate naming, and optional external-name-list support are also unchanged.
