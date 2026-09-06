# DSFix v1.7.9 validation

## Exact supported Distinguished Service binary

The supplied target `DistinguishedService.dll` has SHA-256:

`58cfbba78db17c3f26787cf3cb97e3ae0da4c68f9604517ce7f3347275bce184`

The v1.7.8 exact roster fix and the v1.7.9 AI-companion lifecycle fix are both validated against this binary.

## AI-promoted companion tavern-resident failure

### Symptom

Distinguished Service successfully promotes troops for AI lords, but after the party is defeated/destroyed some of those promoted companions become permanent residents of a tavern and never return to an AI lord's party.

### Trigger

The exact Distinguished Service binary creates an AI promotion as a wanderer-template hero, applies `AddCompanionAction` for the AI lord's clan, and then calls `AddHeroToPartyAction.Apply` for the promoting party.

At map-event start, Distinguished Service snapshots wanderer companions. At `MapEventEnded`, its defeat logic compares that snapshot with the defeated party's current roster and processes only wanderers that are **already missing** from that roster.

### Root cause

A promoted companion that is still present in the defeated party roster when `MapEventEnded` executes does not enter Distinguished Service's defeat-recovery branch. If the party is subsequently destroyed/disbanded, Bannerlord can leave that wanderer companion unassigned and relocate them to a settlement.

Bannerlord 1.3.15 then has no normal AI lifecycle that repairs the state:

- `HeroSpawnCampaignBehavior.CanHeroMoveToAnotherSettlement` excludes wanderers;
- `HeroSpawnCampaignBehavior.GetBestAvailableCommander` requires `Occupation.Lord` for normal AI lord-party creation;
- the Distinguished Service promoted hero intentionally retains wanderer occupation/template semantics.

The permanent tavern resident is therefore a lifecycle gap between Distinguished Service's one-shot map-event cleanup and Bannerlord's later party-destruction recovery, not an intended Distinguished Service outcome.

## v1.7.9 exact promotion tracking

`AIPromotedCompanionRecoveryPatch` targets only:

`DistinguishedService.PromotionManager.PromoteToParty(CharacterObject, MobileParty)`

The transpiler discovers Bannerlord's exact:

`AddHeroToPartyAction.Apply(Hero, MobileParty, bool)`

and requires exactly **one** matching call in `PromoteToParty`.

That one call is replaced with a same-signature helper. The helper first invokes native `AddHeroToPartyAction.Apply` with the original arguments and only then records the promotion through `AIPromotedCompanionRecoveryBehavior.TrackPromotion`.

This ordering is intentional: a failed native handoff cannot leave behind a false recovery record.

A changed Distinguished Service binary with zero or multiple matching calls throws during patch application. The transpiler is removed on patch failure. DSFix never patches `AddHeroToPartyAction.Apply` globally.

## Save-persistent recovery state

`AIPromotedCompanionRecoveryBehavior` saves:

- exact tracked AI-promoted hero IDs;
- the current companion-clan ID for diagnostics;
- a one-time legacy-migration completion flag.

The saved clan ID is never resolved back into a clan to force ownership. Every recovery operation uses the hero's live `CompanionOf` value. If the hero legitimately leaves a clan or becomes a player-clan companion, DSFix stops managing that hero.

Tracking remains valid while the hero is alive, is still a non-player clan companion, and still uses `Occupation.Wanderer`. Prisoner/governor/transient states pause recovery rather than changing ownership.

## Destruction-time recovery

DSFix listens to Bannerlord's native `MobilePartyDestroyed` campaign event. For an exact tracked hero that still belongs to the party being destroyed, it looks for another party satisfying all of the following:

- not the destroyed party and not the player party;
- active;
- not disbanding;
- not currently in a map event;
- a lord party;
- has a living leader;
- leader belongs to the hero's current `CompanionOf` clan.

Destination preference is deterministic:

1. a valid clan lord party already in the hero's settlement, when applicable;
2. the current clan leader's valid party;
3. remaining valid clan lord parties ordered by party ID.

If a destination exists, Bannerlord's native `AddHeroToPartyAction.Apply` performs the transfer. This does not increase clan companion count, so Distinguished Service's creation/recruitment cap is not reused as a reattachment blocker.

If no destination is safe at destruction time, DSFix does nothing destructive. The hero remains tracked and Bannerlord is allowed to finish its native party-destruction/fugitive/teleport lifecycle.

## Deferred settlement recovery

On `DailyTickHero`, a tracked hero is reattached only after reaching the narrow stranded state:

- alive and active;
- still `Occupation.Wanderer`;
- current `CompanionOf` is a non-player clan;
- no party;
- not a prisoner;
- not a governor;
- currently resident in a settlement;
- not fugitive, released, or traveling through an active native transition.

This prevents DSFix from racing Bannerlord's normal captivity/release/teleport processing.

## Existing-save migration

Versions before v1.7.9 did not persist exact AI-promotion IDs. To repair already-broken saves, v1.7.9 performs one conservative migration after the exact `PromoteToParty` tracking patch is confirmed installed.

Only heroes already matching the same strict stranded state are adopted: non-player clan wanderer companions that are active, free, unassigned, non-governors, and currently in a settlement.

The migration completion flag is saved. It therefore runs once per existing save rather than continuously classifying future heroes heuristically.

## v1.7.8 roster root fix remains exact

The previous post-map-event crash fix is unchanged. The exact target binary contains five invalid positive-count removals of already-absent wanderers:

- **2** in `PromotionManager.MapEventEnded(MapEvent)`;
- **3** in `PromotionManager.FleeToOtherClanLord(MapEventParty, CharacterObject)`.

`LordPromotionRosterPatch` rewrites only those five calls to a helper that skips the proven already-absent case and otherwise invokes native Bannerlord `TroopRoster.RemoveTroop` with the original arguments. No global roster patch or exception suppression is installed.

## CI validation

GitHub Actions restores and builds both DSFix assemblies as `net472` against Bannerlord 1.3.15 reference assemblies. `tools/validate_release.py` verifies, among the existing invariants:

- release/module version consistency;
- exact `PromoteToParty(CharacterObject, MobileParty)` discovery;
- exact `AddHeroToPartyAction.Apply(Hero, MobileParty, bool)` discovery;
- exactly one AI-promotion handoff rewrite;
- native handoff executes before promotion tracking;
- absence of a global `AddHeroToPartyAction` patch;
- save-state and one-time migration keys;
- `MobilePartyDestroyed` and daily hero recovery hooks;
- strict player-clan, prisoner, governor, wanderer, party, and settlement boundaries;
- recovery by the hero's current `CompanionOf` clan only;
- active/non-disbanding/non-map-event lord-party destination filtering;
- the existing exact five-site `RemoveTroop` rewrite and all-or-nothing patch state;
- all existing promoted-race, save/load, naming, summoned-agent, and package invariants.

## Remaining runtime verification

Compilation and structural validation prove the target signatures, rewrite counts, and safety boundaries. Final behavioral proof remains an in-game Bannerlord 1.3.15 + TOR WiTM 1.16 + exact supported Distinguished Service run in which an AI-promoted companion survives destruction of its original party and subsequently appears in another valid party of its current clan instead of remaining in a tavern.
