## DSFix v1.7.2

Fixes the Distinguished Service lord-promotion/flee crash reported on Bannerlord 1.3.15 + TOR WiTM 1.16:

`TroopRoster.AddToCountsAtIndex -> TroopRoster.RemoveTroop -> PromotionManager.FleeToOtherClanLord -> PromotionManager.MapEventEnded`

The fix is scoped to the failing Distinguished Service path. If the ended map-event roster has already removed the fleeing wanderer, DSFix treats the redundant removal as complete and allows the rest of the feature to continue. A narrow fallback handles the same native `IndexOutOfRangeException` if the roster changes between the preflight and removal.

This release also synchronizes GitHub with the Nexus v1.7.1 behavior, retaining the TOR summoned-agent post-battle fix and culture-accurate promoted names, and adds reproducible CI/release packaging.
