## DSFix v1.7.7

Fixes the remaining Distinguished Service post-map-event `System.IndexOutOfRangeException` visible after v1.7.6.

### New runtime evidence

The latest stack is:

`System.IndexOutOfRangeException -> TaleWorlds.CampaignSystem.Roster.TroopRoster.RemoveTroop_Patch1(...) -> DistinguishedService.PromotionManager.MapEventEnded(MapEvent me)`

This is a distinct path from the earlier `FleeToOtherClanLord` failures. The `RemoveTroop` call now comes directly from `MapEventEnded`, so neither the v1.7.2 exact-flee context nor the v1.7.6 `FleeToOtherClanLord` method-boundary finalizer can match it.

Public Distinguished Service source confirms that `MapEventEnded` iterates `MapEventParty` objects from the ended event and reads their transient `Troops` rosters. The current Nexus 1.3.14 DLL contains additional cleanup not present in that older public source. The runtime stack does not expose which `TroopRoster` instance the live cleanup passes to `RemoveTroop`.

### Fix

v1.7.7 adds an exact `DistinguishedService.PromotionManager.MapEventEnded(MapEvent)` scope:

- the prefix captures only roster objects owned by parties participating in that same event;
- for each exact `MapEventParty`, DSFix records both its transient `Troops` roster and its underlying `PartyBase.MemberRoster`, covering the two participant-owned roster locations the live cleanup can reasonably target without guessing from troop identity;
- participant discovery uses `MapEvent.Parties`, with `PartiesOnSide(...)` as a signature-based fallback;
- roster matching uses object reference identity;
- the existing global `TroopRoster.RemoveTroop` hook remains inert unless the roster is one of those exact captured participant rosters or the existing exact `FleeToOtherClanLord` target;
- an already-absent troop becomes a no-op before Bannerlord resolves an invalid internal index;
- only `IndexOutOfRangeException` from `RemoveTroop` on an exact protected cleanup roster is contained;
- all other `RemoveTroop` calls and all other exception types keep native behavior;
- linked thread-local cleanup state is restored on every exit, and participant enumeration fails open rather than creating a new map-event failure.

The TOR summoned-agent result fix, promoted race/body identity preservation, save/load persistence, culture-accurate naming, and optional external-name-list handling are unchanged.
