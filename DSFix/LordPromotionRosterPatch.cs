using System;
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
        private const string MapEventPartyTypeName = "TaleWorlds.CampaignSystem.MapEvents.MapEventParty";
        private static readonly object PatchLock = new object();
        private static bool _patched;

        [ThreadStatic]
        private static FleeContext _currentFlee;

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
                MethodInfo removeTroop = FindRemoveTroop(troopRosterType);

                // Install the globally visible hook first. It remains behaviorally inert unless the
                // exact FleeToOtherClanLord call has established a thread-local target context.
                harmony.Patch(removeTroop,
                    prefix: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(RemoveTroopPrefix)),
                    finalizer: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(RemoveTroopFinalizer)));
                harmony.Patch(flee,
                    prefix: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(FleePrefix)),
                    finalizer: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(FleeFinalizer)));

                _patched = true;
                DSLog.Write("Patched Distinguished Service lord-promotion flee handling: exact-roster RemoveTroop protection plus an exact FleeToOtherClanLord IndexOutOfRangeException boundary guard.", true);
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
                DSLog.Write("Suppressed IndexOutOfRangeException escaping the exact DistinguishedService.PromotionManager.FleeToOtherClanLord boundary. The v1.7.2 exact RemoveTroop guard did not contain this runtime path, so the failure originated from another stale index operation inside the same post-map-event flee cleanup.");
                return null;
            }

            return __exception;
        }

        private static bool RemoveTroopPrefix(object __instance, object __0, int __1)
        {
            if (__1 <= 0 || !MatchesCurrentFleeTarget(__instance, __0))
                return true;

            bool? contains = TryContainsTroop(__instance, __0);
            if (contains != false)
                return true;

            DSLog.Write("Skipped Distinguished Service's stale lord-promotion roster removal because the fleeing wanderer was already absent from the exact map-event TroopRoster.");
            return false;
        }

        private static Exception RemoveTroopFinalizer(Exception __exception, object __instance, object __0)
        {
            if (!(__exception is IndexOutOfRangeException) || !MatchesCurrentFleeTarget(__instance, __0))
                return __exception;

            DSLog.Write("Suppressed the target TroopRoster.RemoveTroop IndexOutOfRangeException for the exact wanderer/roster pair inside DistinguishedService.PromotionManager.FleeToOtherClanLord after the roster changed between preflight and native removal.");
            return null;
        }

        private static bool MatchesCurrentFleeTarget(object roster, object troop)
        {
            FleeContext context = _currentFlee;
            if (context == null || roster == null || troop == null || !ReferenceEquals(troop, context.Wanderer))
                return false;
            return context.Roster == null || ReferenceEquals(roster, context.Roster);
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
                DSLog.Write("Lord-promotion roster preflight failed open: " + Unwrap(ex).Message);
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
    }
}
