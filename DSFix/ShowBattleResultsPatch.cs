using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Party;

namespace DSFix
{
    internal static class ShowBattleResultsPatch
    {
        private const string TargetTypeName = "DistinguishedService.DSBattleLogic";
        private const string TargetMethodName = "ShowBattleResults";
        private const int ExpectedRewriteCount = 3;
        private static readonly object PatchLock = new object();
        private static bool _patched;

        internal static void TryPatch(Harmony harmony)
        {
            if (_patched)
                return;

            lock (PatchLock)
            {
                if (_patched)
                    return;

                Type targetType = ReflectionUtil.FindLoadedType(TargetTypeName);
                if (targetType == null)
                    return;

                MethodInfo target = targetType.GetMethod(TargetMethodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null, Type.EmptyTypes, null);
                if (target == null)
                    throw new MissingMethodException(TargetTypeName, TargetMethodName + "()");

                harmony.Patch(target,
                    transpiler: new HarmonyMethod(typeof(ShowBattleResultsPatch), nameof(Transpiler)));
                _patched = true;
                DSLog.Write("Patched DistinguishedService battle-result handling; rewrote 3 origin-to-PartyBase conversions.", true);
            }
        }

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> code = new List<CodeInstruction>(instructions);
            MethodInfo resolver = AccessTools.Method(typeof(PartyResolver), nameof(PartyResolver.ResolveFromOrigin));
            int rewrites = 0;

            for (int i = 0; i + 1 < code.Count; i++)
            {
                if (!IsBattleCombatantGetter(code[i]) || !IsPartyBaseCast(code[i + 1]))
                    continue;

                CodeInstruction call = new CodeInstruction(OpCodes.Call, resolver);
                call.labels.AddRange(code[i].labels);
                call.blocks.AddRange(code[i].blocks);
                code[i] = call;

                CodeInstruction nop = new CodeInstruction(OpCodes.Nop);
                nop.labels.AddRange(code[i + 1].labels);
                nop.blocks.AddRange(code[i + 1].blocks);
                code[i + 1] = nop;
                rewrites++;
            }

            if (rewrites != ExpectedRewriteCount)
            {
                throw new InvalidOperationException(
                    "Expected to rewrite " + ExpectedRewriteCount +
                    " IAgentOriginBase.BattleCombatant -> PartyBase sequences in ShowBattleResults, found " + rewrites +
                    ". The installed DistinguishedService build is not the validated 1.3.x fork target.");
            }

            return code;
        }

        private static bool IsBattleCombatantGetter(CodeInstruction instruction)
        {
            if (instruction == null || (instruction.opcode != OpCodes.Call && instruction.opcode != OpCodes.Callvirt))
                return false;
            MethodInfo method = instruction.operand as MethodInfo;
            return method != null && string.Equals(method.Name, "get_BattleCombatant", StringComparison.Ordinal);
        }

        private static bool IsPartyBaseCast(CodeInstruction instruction)
        {
            if (instruction == null || instruction.opcode != OpCodes.Castclass)
                return false;
            Type type = instruction.operand as Type;
            return ReflectionUtil.TypeNameEquals(type, "TaleWorlds.CampaignSystem.Party.PartyBase");
        }
    }

    internal static class PartyResolver
    {
        internal static PartyBase ResolveFromOrigin(object origin)
        {
            PartyBase resolved = ResolveParty(ReflectionUtil.ReadMember(origin, "BattleCombatant"), 0)
                ?? ResolveParty(origin, 0);
            if (resolved != null)
                return resolved;

            try { return MobileParty.MainParty?.Party; }
            catch { return null; }
        }

        private static PartyBase ResolveParty(object value, int depth)
        {
            if (value == null || depth > 5)
                return null;

            PartyBase partyBase = value as PartyBase;
            if (partyBase != null)
                return partyBase;

            object ownerParty = ReflectionUtil.ReadMember(value, "OwnerParty") ?? ReflectionUtil.ReadMember(value, "Party");
            partyBase = ownerParty as PartyBase;
            if (partyBase != null)
                return partyBase;

            object battleCombatant = ReflectionUtil.ReadMember(value, "BattleCombatant");
            if (battleCombatant != null && !ReferenceEquals(battleCombatant, value))
            {
                partyBase = ResolveParty(battleCombatant, depth + 1);
                if (partyBase != null)
                    return partyBase;
            }

            object summoner = ReflectionUtil.ReadMember(value, "SummonerAgent")
                ?? ReflectionUtil.ReadMember(value, "CasterAgent")
                ?? ReflectionUtil.ReadMember(value, "OwnerAgent");
            object summonerOrigin = ReflectionUtil.ReadMember(summoner, "Origin");
            if (summonerOrigin != null)
            {
                partyBase = ResolveParty(summonerOrigin, depth + 1);
                if (partyBase != null)
                    return partyBase;
            }

            return null;
        }
    }
}
