## DSFix v1.7.7

Fixes the remaining Distinguished Service post-map-event `System.IndexOutOfRangeException` visible after v1.7.6.

### New runtime evidence

The latest stack is:

`System.IndexOutOfRangeException -> TaleWorlds.CampaignSystem.Roster.TroopRoster.RemoveTroop_Patch1(...) -> DistinguishedService.PromotionManager.MapEventEnded(MapEvent me)`

This is a distinct path from the earlier `FleeToOtherClanLord` failures. The `RemoveTroop` call now comes directly from `MapEventEnded`, so neither the v1.7.2 exact-flee context nor the v1.7.6 `FleeToOtherClanLord` method-boundary finalizer can match it.

Public Distinguished Service source confirms that `MapEventEnded` iterates `MapEventParty` objects from the ended event and works from each party's transient `Troops` roster. The current Nexus build has added cleanup not present in that older public source, and the live stack establishes that one of those direct cleanup removals can resolve a stale roster index.

### Fix

v1.7.7 adds an exact `DistinguishedService.PromotionManager.MapEventEnded(MapEvent)` scope:

- the prefix captures only `MapEventParty.Troops` roster instances belonging to that same `MapEvent`;
- roster discovery uses `MapEvent.Parties`, with `PartiesOnSide(...)` as a signature-based fallback for build-shape compatibility;
- the existing global `TroopRoster.RemoveTroop` hook remains inert unless the roster is one of those exact captured instances or the existing exact `FleeToOtherClanLord` target;
- an already-absent troop becomes a no-op before Bannerlord resolves an invalid roster index;
- only `IndexOutOfRangeException` from `RemoveTroop` on an exact protected cleanup roster is contained;
- all other `RemoveTroop` calls and all other exception types keep native behavior;
- thread-local cleanup state is restored on every exit and composes with nested `FleeToOtherClanLord` calls.

The TOR summoned-agent result fix, promoted race/body identity preservation, save/load persistence, culture-accurate naming, and optional external-name-list handling are unchanged.
