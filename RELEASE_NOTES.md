## DSFix v1.7.2

Fixes the Distinguished Service lord-promotion/flee crash reported on Bannerlord 1.3.15 + TOR WiTM 1.16:

`TroopRoster.AddToCountsAtIndex -> TroopRoster.RemoveTroop -> PromotionManager.FleeToOtherClanLord -> PromotionManager.MapEventEnded`

The compatibility guard is scoped to the exact wanderer and exact ended `MapEventParty.Troops` roster from `FleeToOtherClanLord`. If that troop is already absent, the redundant removal is treated as complete. If native removal for that same exact pair still reaches a stale/invalid roster index and throws `IndexOutOfRangeException`, DSFix contains that exception so the remaining Distinguished Service flee logic can continue. Other roster operations retain native behavior.

This release also synchronizes GitHub with the supplied Nexus v1.7.1 baseline, retaining the TOR summoned-agent post-battle fix and culture-accurate promoted names, and adds reproducible CI/release packaging.
