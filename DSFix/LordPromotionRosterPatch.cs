using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Roster;
using TaleWorlds.Core;

namespace DSFix
{
    internal static class LordPromotionRosterPatch
    {
        private const string PromotionManagerTypeName = "DistinguishedService.PromotionManager";
        private const string TroopRosterTypeName = "TaleWorlds.CampaignSystem.Roster.TroopRoster";
        private const string CharacterObjectTypeName = "TaleWorlds.CampaignSystem.CharacterObject";
        private const string MapEventTypeName = "TaleWorlds.CampaignSystem.MapEvents.MapEvent";
        private const string MapEventPartyTypeName = "TaleWorlds.CampaignSystem.MapEvents.MapEventParty";
        private const int ExpectedMapEventEndedRewriteCount = 2;
        private const int ExpectedFleeRewriteCount = 3;
        private static readonly object PatchLock = new object();
        private static readonly MethodInfo SafeRemoveTroopMethod = AccessTools.Method(typeof(LordPromotionRosterPatch), nameof(RemoveTroopIfPresent));
        private static MethodInfo _removeTroopMethod;
        private static bool _patched;

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
                _removeTroopMethod = FindRemoveTroop(troopRosterType);
                if (SafeRemoveTroopMethod == null)
                    throw new MissingMethodException(nameof(RemoveTroopIfPresent));

                try
                {
                    harmony.Patch(flee,
                        transpiler: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(FleeToOtherClanLordTranspiler)));
                    harmony.Patch(mapEventEnded,
                        transpiler: new HarmonyMethod(typeof(LordPromotionRosterPatch), nameof(MapEventEndedTranspiler)));
                }
                catch
                {
                    // Patch application is atomic at the DSFix feature level. If either validated
                    // target fails to rewrite, remove any transpiler already installed by this
                    // Harmony owner so the game never runs with only part of the five-site fix.
                    harmony.Unpatch(flee, HarmonyPatchType.Transpiler, harmony.Id);
                    harmony.Unpatch(mapEventEnded, HarmonyPatchType.Transpiler, harmony.Id);
                    throw;
                }

                _patched = true;
                DSLog.Write("Patched Distinguished Service's five known invalid post-map-event RemoveTroop call sites: 2 in MapEventEnded and 3 in FleeToOtherClanLord. No global TroopRoster hook or exception suppression is installed.", true);
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
                        && p[1].ParameterType == typeof(int)
                        && p[2].ParameterType == typeof(UniqueTroopDescriptor)
                        && p[3].ParameterType == typeof(int);
                }).ToArray();
            if (matches.Length != 1)
                throw new MissingMethodException(matches.Length > 1
                    ? "Multiple four-argument TroopRoster.RemoveTroop(CharacterObject, int, UniqueTroopDescriptor, int) methods were found."
                    : "TroopRoster.RemoveTroop(CharacterObject, int, UniqueTroopDescriptor, int)");
            return matches[0];
        }

        private static IEnumerable<CodeInstruction> MapEventEndedTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            return RewriteRemoveTroopCalls(instructions, ExpectedMapEventEndedRewriteCount, "PromotionManager.MapEventEnded");
        }

        private static IEnumerable<CodeInstruction> FleeToOtherClanLordTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            return RewriteRemoveTroopCalls(instructions, ExpectedFleeRewriteCount, "PromotionManager.FleeToOtherClanLord");
        }

        private static IEnumerable<CodeInstruction> RewriteRemoveTroopCalls(IEnumerable<CodeInstruction> instructions, int expectedCount, string targetName)
        {
            List<CodeInstruction> rewritten = instructions.ToList();
            int rewriteCount = 0;

            for (int i = 0; i < rewritten.Count; i++)
            {
                CodeInstruction instruction = rewritten[i];
                if (!instruction.Calls(_removeTroopMethod))
                    continue;

                instruction.opcode = OpCodes.Call;
                instruction.operand = SafeRemoveTroopMethod;
                rewriteCount++;
            }

            if (rewriteCount != expectedCount)
            {
                throw new InvalidOperationException(
                    $"{targetName} contained {rewriteCount} matching TroopRoster.RemoveTroop call(s); expected exactly {expectedCount}. " +
                    "Refusing to apply a partial or structurally mismatched Distinguished Service compatibility rewrite.");
            }

            DSLog.Write($"Rewrote {rewriteCount} exact TroopRoster.RemoveTroop call site(s) in {targetName}.", true);
            return rewritten;
        }

        private static void RemoveTroopIfPresent(TroopRoster roster, CharacterObject troop, int numberToRemove, UniqueTroopDescriptor troopSeed, int xp)
        {
            if (roster == null || troop == null || numberToRemove <= 0)
                return;

            if (roster.GetTroopCount(troop) <= 0)
            {
                DSLog.Write("Skipped Distinguished Service's invalid post-map-event RemoveTroop call because the wanderer is already absent from the target roster.");
                return;
            }

            roster.RemoveTroop(troop, numberToRemove, troopSeed, xp);
        }
    }
}
