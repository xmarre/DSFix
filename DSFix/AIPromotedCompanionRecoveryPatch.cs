using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Actions;
using TaleWorlds.CampaignSystem.Party;

namespace DSFix
{
    internal static class AIPromotedCompanionRecoveryPatch
    {
        private const string PromotionManagerTypeName = "DistinguishedService.PromotionManager";
        private const string CharacterObjectTypeName = "TaleWorlds.CampaignSystem.CharacterObject";
        private const string MobilePartyTypeName = "TaleWorlds.CampaignSystem.Party.MobileParty";
        private const int ExpectedAddHeroToPartyRewriteCount = 1;

        private static readonly object PatchLock = new object();
        private static readonly MethodInfo TrackedAddHeroToPartyMethod =
            AccessTools.Method(typeof(AIPromotedCompanionRecoveryPatch), nameof(AddHeroToPartyAndTrack));

        private static MethodInfo _nativeAddHeroToPartyMethod;
        private static bool _patched;

        internal static bool IsPatched => _patched;

        internal static void TryPatch(Harmony harmony)
        {
            if (_patched)
                return;

            lock (PatchLock)
            {
                if (_patched)
                    return;

                Type managerType = ReflectionUtil.FindLoadedType(PromotionManagerTypeName);
                if (managerType == null)
                    return;

                MethodInfo promoteToParty = FindPromoteToParty(managerType);
                _nativeAddHeroToPartyMethod = FindAddHeroToParty();
                if (TrackedAddHeroToPartyMethod == null)
                    throw new MissingMethodException(nameof(AddHeroToPartyAndTrack));

                try
                {
                    harmony.Patch(
                        promoteToParty,
                        transpiler: new HarmonyMethod(
                            typeof(AIPromotedCompanionRecoveryPatch),
                            nameof(PromoteToPartyTranspiler)));
                }
                catch
                {
                    harmony.Unpatch(promoteToParty, HarmonyPatchType.Transpiler, harmony.Id);
                    throw;
                }

                _patched = true;
                DSLog.Write(
                    "Patched the exact Distinguished Service PromoteToParty AI-promotion handoff so successfully added AI companions are tracked for later party-destruction/tavern recovery. No global AddHeroToPartyAction hook is installed.",
                    true);
            }
        }

        internal static void Reset()
        {
            _nativeAddHeroToPartyMethod = null;
            _patched = false;
        }

        private static MethodInfo FindPromoteToParty(Type managerType)
        {
            MethodInfo[] matches = managerType.GetMethods(ReflectionUtil.AllInstance)
                .Where(m => m.Name == "PromoteToParty" && m.ReturnType == typeof(void))
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 2
                        && ReflectionUtil.TypeNameEquals(p[0].ParameterType, CharacterObjectTypeName)
                        && ReflectionUtil.TypeNameEquals(p[1].ParameterType, MobilePartyTypeName);
                }).ToArray();

            if (matches.Length != 1)
                throw new MissingMethodException(matches.Length > 1
                    ? "Multiple PromoteToParty(CharacterObject, MobileParty) methods were found."
                    : "PromoteToParty(CharacterObject, MobileParty)");

            return matches[0];
        }

        private static MethodInfo FindAddHeroToParty()
        {
            MethodInfo[] matches = typeof(AddHeroToPartyAction).GetMethods(ReflectionUtil.AllStatic)
                .Where(m => m.Name == "Apply" && m.ReturnType == typeof(void))
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 3
                        && p[0].ParameterType == typeof(Hero)
                        && p[1].ParameterType == typeof(MobileParty)
                        && p[2].ParameterType == typeof(bool);
                }).ToArray();

            if (matches.Length != 1)
                throw new MissingMethodException(matches.Length > 1
                    ? "Multiple AddHeroToPartyAction.Apply(Hero, MobileParty, bool) methods were found."
                    : "AddHeroToPartyAction.Apply(Hero, MobileParty, bool)");

            return matches[0];
        }

        private static IEnumerable<CodeInstruction> PromoteToPartyTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> rewritten = instructions.ToList();
            int rewriteCount = 0;

            for (int i = 0; i < rewritten.Count; i++)
            {
                CodeInstruction instruction = rewritten[i];
                if (!instruction.Calls(_nativeAddHeroToPartyMethod))
                    continue;

                instruction.opcode = OpCodes.Call;
                instruction.operand = TrackedAddHeroToPartyMethod;
                rewriteCount++;
            }

            if (rewriteCount != ExpectedAddHeroToPartyRewriteCount)
            {
                throw new InvalidOperationException(
                    "PromotionManager.PromoteToParty contained " + rewriteCount +
                    " matching AddHeroToPartyAction.Apply call(s); expected exactly " +
                    ExpectedAddHeroToPartyRewriteCount +
                    ". Refusing to install ambiguous AI-promotion tracking against a changed Distinguished Service binary.");
            }

            DSLog.Write(
                "Rewrote exactly one AddHeroToPartyAction.Apply call in DistinguishedService.PromotionManager.PromoteToParty for AI-promotion tracking.",
                true);
            return rewritten;
        }

        private static void AddHeroToPartyAndTrack(Hero hero, MobileParty party, bool showNotification)
        {
            // Preserve native behavior first. A failed native handoff must never create a tracked
            // companion record for a hero that was not actually added to the AI party.
            AddHeroToPartyAction.Apply(hero, party, showNotification);
            AIPromotedCompanionRecoveryBehavior.TrackPromotion(hero, party);
        }
    }
}
