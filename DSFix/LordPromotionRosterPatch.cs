using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace DSFix
{
    internal static class LordPromotionRosterPatch
    {
        private const string PromotionManagerTypeName = "DistinguishedService.PromotionManager";
        private const string TroopRosterTypeName = "TaleWorlds.CampaignSystem.Roster.TroopRoster";
        private const string CharacterObjectTypeName = "TaleWorlds.CampaignSystem.CharacterObject";
        private const string MapEventTypeName = "TaleWorlds.CampaignSystem.MapEvents.MapEvent";
        private const string MapEventPartyTypeName = "TaleWorlds.CampaignSystem.MapEvents.MapEventParty";
        private static readonly object PatchLock = new object();
        private static bool _patched;

        [ThreadStatic]
        private static FleeContext _currentFlee;

        [ThreadStatic]
        private static MapEventContext _currentMapEvent;

        internal static void TryPatch(Harmony harmony)
        {
            if (_patched)
                return;

            lock (PatchLock)
            {
                if (_patched)
                    return;

                Type managerType = ReflectionUtil.FindLoadedType(PromotionManagerTypeName);
                Type troopRosterType = ReflectionUtil.FindLoadedType(TroopRosterTypeName);
                if (managerType == null || troopRosterType == null)
                    return;

                MethodInfo flee = FindFleeToOtherClanLord(managerType);
                MethodInfo mapEventEnded = FindMapEventEnded(managerType);
                MethodInfo removeTroop = FindRemoveTroop(troopRosterType);

                // RemoveTroop is globally visible, but the hook is behaviorally inert unless the
                // call is proven to belong to Distinguished Service's exact post-map-event cleanup.
                harmony.Patch(removeTroop,
                    prefix: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(RemoveTroopPrefix)),
                    finalizer: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(RemoveTroopFinalizer)));
                harmony.Patch(flee,
                    prefix: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(FleePrefix)),
                    finalizer: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(FleeFinalizer)));
                harmony.Patch(mapEventEnded,
                    prefix: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(MapEventEndedPrefix)),
                    finalizer: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(MapEventEndedFinalizer)));

                _patched = true;
                DSLog.Write("Patched Distinguished Service post-map-event roster handling: exact FleeToOtherClanLord protection plus exact MapEventEnded participant-roster RemoveTroop protection.", true);
            }
        }

        private static MethodInfo FindFleeToOtherClanLord(Type managerType)
        {
            MethodInfo[] matches = managerType.GetMethods(ReflectionUtil.AllInstance)
                .Where(m => m.Name == "FleeToOtherClanLord")
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 2
                        && ReflectionUtil.TypeNameEquals(p[0].ParameterType, MapEventPartyTypeName)
                        && ReflectionUtil.TypeNameEquals(p[1].ParameterType, CharacterObjectTypeName);
                }).ToArray();
            if (matches.Length != 1)
                throw new MissingMethodException(matches.Length > 1
                    ? "Multiple FleeToOtherClanLord(MapEventParty, CharacterObject) methods were found."
                    : "FleeToOtherClanLord(MapEventParty, CharacterObject)");
            return matches[0];
        }

        private static MethodInfo FindMapEventEnded(Type managerType)
        {
            MethodInfo[] matches = managerType.GetMethods(ReflectionUtil.AllInstance)
                .Where(m => m.Name == "MapEventEnded")
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 1 && ReflectionUtil.TypeNameEquals(p[0].ParameterType, MapEventTypeName);
                }).ToArray();
            if (matches.Length != 1)
                throw new MissingMethodException(matches.Length > 1
                    ? "Multiple MapEventEnded(MapEvent) methods were found."
                    : "MapEventEnded(MapEvent)");
            return matches[0];
        }

        private static MethodInfo FindRemoveTroop(Type troopRosterType)
        {
            MethodInfo[] matches = troopRosterType.GetMethods(ReflectionUtil.AllInstance)
                .Where(m => m.Name == "RemoveTroop")
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 4
                        && ReflectionUtil.TypeNameEquals(p[0].ParameterType, CharacterObjectTypeName)
                        && p[1].ParameterType == typeof(int);
                }).ToArray();
            if (matches.Length != 1)
                throw new MissingMethodException(matches.Length > 1
                    ? "Multiple four-argument TroopRoster.RemoveTroop(CharacterObject, ...) methods were found."
                    : "TroopRoster.RemoveTroop(CharacterObject, int, UniqueTroopDescriptor, int)");
            return matches[0];
        }

        private static void FleePrefix(object __0, object __1)
        {
            FleeContext context = new FleeContext
            {
                Previous = _currentFlee,
                Wanderer = __1,
                Roster = ReflectionUtil.ReadMember(__0, "Troops")
            };
            _currentFlee = context;
        }

        private static Exception FleeFinalizer(Exception __exception)
        {
            FleeContext context = _currentFlee;
            _currentFlee = context?.Previous;

            if (__exception is IndexOutOfRangeException)
            {
                DSLog.Write("Suppressed IndexOutOfRangeException escaping the exact DistinguishedService.PromotionManager.FleeToOtherClanLord boundary.");
                return null;
            }

            return __exception;
        }

        private static void MapEventEndedPrefix(object __0)
        {
            MapEventContext context = new MapEventContext
            {
                Previous = _currentMapEvent,
                Rosters = CollectMapEventRosters(__0)
            };
            _currentMapEvent = context;
        }

        private static Exception MapEventEndedFinalizer(Exception __exception)
        {
            MapEventContext context = _currentMapEvent;
            _currentMapEvent = context?.Previous;
            return __exception;
        }

        private static bool RemoveTroopPrefix(object __instance, object __0, int __1)
        {
            if (__1 <= 0 || !MatchesProtectedCleanupTarget(__instance, __0))
                return true;

            bool? contains = TryContainsTroop(__instance, __0);
            if (contains != false)
                return true;

            DSLog.Write("Skipped Distinguished Service's stale post-map-event roster removal because the exact troop was already absent from the protected TroopRoster.");
            return false;
        }

        private static Exception RemoveTroopFinalizer(Exception __exception, object __instance, object __0)
        {
            if (!(__exception is IndexOutOfRangeException) || !MatchesProtectedCleanupTarget(__instance, __0))
                return __exception;

            if (MatchesCurrentFleeTarget(__instance, __0))
                DSLog.Write("Suppressed TroopRoster.RemoveTroop IndexOutOfRangeException for the exact wanderer/roster pair inside DistinguishedService.PromotionManager.FleeToOtherClanLord.");
            else
                DSLog.Write("Suppressed TroopRoster.RemoveTroop IndexOutOfRangeException for an exact participant roster while DistinguishedService.PromotionManager.MapEventEnded was cleaning up the same ended map event.");
            return null;
        }

        private static bool MatchesProtectedCleanupTarget(object roster, object troop)
        {
            return MatchesCurrentFleeTarget(roster, troop) || MatchesCurrentMapEventRoster(roster);
        }

        private static bool MatchesCurrentFleeTarget(object roster, object troop)
        {
            FleeContext context = _currentFlee;
            if (context == null || roster == null || troop == null || !ReferenceEquals(troop, context.Wanderer))
                return false;
            return context.Roster == null || ReferenceEquals(roster, context.Roster);
        }

        private static bool MatchesCurrentMapEventRoster(object roster)
        {
            MapEventContext context = _currentMapEvent;
            if (context == null || roster == null || context.Rosters == null)
                return false;

            for (int i = 0; i < context.Rosters.Count; i++)
            {
                if (ReferenceEquals(roster, context.Rosters[i]))
                    return true;
            }
            return false;
        }

        private static List<object> CollectMapEventRosters(object mapEvent)
        {
            List<object> rosters = new List<object>();
            if (mapEvent == null)
                return rosters;

            // Capture exact roster objects owned by parties participating in this event. The live
            // Distinguished Service build can remove from either the transient MapEventParty.Troops
            // roster or the participant PartyBase.MemberRoster while MapEventEnded is running.
            AddRostersFromParties(ReflectionUtil.ReadMember(mapEvent, "Parties"), rosters);

            // Keep a signature-based fallback for builds where the Parties property shape differs.
            try
            {
                MethodInfo partiesOnSide = mapEvent.GetType().GetMethods(ReflectionUtil.AllInstance)
                    .FirstOrDefault(m => m.Name == "PartiesOnSide"
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsEnum);
                if (partiesOnSide != null)
                {
                    Type sideType = partiesOnSide.GetParameters()[0].ParameterType;
                    foreach (object side in Enum.GetValues(sideType))
                    {
                        object parties = null;
                        try { parties = partiesOnSide.Invoke(mapEvent, new[] { side }); } catch { }
                        AddRostersFromParties(parties, rosters);
                    }
                }
            }
            catch (Exception ex)
            {
                DSLog.Write("MapEvent participant-roster capture fallback failed open: " + Unwrap(ex).Message);
            }

            return rosters;
        }

        private static void AddRostersFromParties(object parties, List<object> rosters)
        {
            if (parties == null || rosters == null)
                return;

            try
            {
                foreach (object mapEventParty in ReflectionUtil.ReadObjects(parties))
                {
                    if (mapEventParty == null || !ReflectionUtil.TypeNameEquals(mapEventParty.GetType(), MapEventPartyTypeName))
                        continue;

                    AddRosterReference(ReflectionUtil.ReadMember(mapEventParty, "Troops"), rosters);

                    object partyBase = ReflectionUtil.ReadMember(mapEventParty, "Party");
                    AddRosterReference(ReflectionUtil.ReadMember(partyBase, "MemberRoster"), rosters);
                }
            }
            catch (Exception ex)
            {
                // Capture is an optimization/scope-discovery step. Failing to enumerate one stale
                // event collection must not become a new campaign exception; unmatched calls stay native.
                DSLog.Write("MapEvent participant-roster enumeration failed open: " + Unwrap(ex).Message);
            }
        }

        private static void AddRosterReference(object roster, List<object> rosters)
        {
            if (roster == null || rosters == null)
                return;

            for (int i = 0; i < rosters.Count; i++)
            {
                if (ReferenceEquals(roster, rosters[i]))
                    return;
            }
            rosters.Add(roster);
        }

        private static bool? TryContainsTroop(object roster, object troop)
        {
            try
            {
                MethodInfo contains = roster.GetType().GetMethods(ReflectionUtil.AllInstance)
                    .FirstOrDefault(m => m.Name == "Contains"
                        && m.ReturnType == typeof(bool)
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsInstanceOfType(troop));
                if (contains != null)
                    return (bool)contains.Invoke(roster, new[] { troop });

                MethodInfo getCount = roster.GetType().GetMethods(ReflectionUtil.AllInstance)
                    .FirstOrDefault(m => m.Name == "GetTroopCount"
                        && m.ReturnType == typeof(int)
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType.IsInstanceOfType(troop));
                if (getCount != null)
                    return (int)getCount.Invoke(roster, new[] { troop }) > 0;
            }
            catch (Exception ex)
            {
                DSLog.Write("Post-map-event roster preflight failed open: " + Unwrap(ex).Message);
            }
            return null;
        }

        private static Exception Unwrap(Exception ex)
        {
            TargetInvocationException tie = ex as TargetInvocationException;
            return tie?.InnerException ?? ex;
        }

        private sealed class FleeContext
        {
            internal FleeContext Previous;
            internal object Wanderer;
            internal object Roster;
        }

        private sealed class MapEventContext
        {
            internal MapEventContext Previous;
            internal List<object> Rosters;
        }
    }
}
