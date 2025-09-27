using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;
using TaleWorlds.CampaignSystem;        // MobileParty
using TaleWorlds.CampaignSystem.Party;  // PartyBase

namespace DSFix
{
    public class SubModule : MBSubModuleBase
    {
        private Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            _harmony = new Harmony("ds.fix.harmony");
            PatchAllLoaded(); // alles was schon geladen ist
            AppDomain.CurrentDomain.AssemblyLoad += (_, e) => TryPatchAssembly(e.LoadedAssembly); // und alles was nachlädt
        }

        private void PatchAllLoaded()
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                TryPatchAssembly(asm);
        }

        private void TryPatchAssembly(Assembly asm)
        {
            int methodsPatched = 0, castsRewrittenTotal = 0;

            try
            {
                // Zieltypen finden: jede Klasse mit Name "DSBattleLogic"
                var targetTypes = new List<Type>();
                try { targetTypes.AddRange(asm.GetTypes().Where(t => t.Name == "DSBattleLogic")); }
                catch { /* ReflectionTypeLoadException ignorieren */ }

                foreach (var t in targetTypes)
                {
                    // 1) Hauptmethode
                    var m = t.GetMethod("ShowBattleResults",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (m != null)
                        methodsPatched += PatchMethod(m, ref castsRewrittenTotal);

                    // 2) Compiler-Generatoren (Iterator/async state machines) innerhalb von DSBattleLogic
                    foreach (var nt in t.GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
                    {
                        // Muster: <ShowBattleResults>d__xx  oder ähnliche
                        if (!(nt.Name.Contains("ShowBattleResults") || nt.FullName.Contains("ShowBattleResults")))
                            continue;

                        var moveNext = nt.GetMethod("MoveNext",
                            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (moveNext != null)
                            methodsPatched += PatchMethod(moveNext, ref castsRewrittenTotal);
                    }
                }

                if (methodsPatched > 0)
                    TryMsg($"[DSFix] Patched {methodsPatched} method(s), rewrote {castsRewrittenTotal} PartyBase cast(s) in {asm.GetName().Name}");
            }
            catch { /* still */ }
        }

        private int PatchMethod(MethodBase method, ref int castsRewrittenTotal)
        {
            // idempotent
            _harmony.Unpatch(method, HarmonyPatchType.Transpiler, _harmony.Id);

            var transpiler = new HarmonyMethod(typeof(ShowBattleResultsTranspile), nameof(ShowBattleResultsTranspile.Transpiler));
            var finalizer = new HarmonyMethod(typeof(ShowBattleResultsTranspile), nameof(ShowBattleResultsTranspile.Finalizer));
            ShowBattleResultsTranspile._lastRewriteCount = 0;

            _harmony.Patch(method, transpiler: transpiler, finalizer: finalizer);

            castsRewrittenTotal += ShowBattleResultsTranspile._lastRewriteCount;
            return 1;
        }

        private static void TryMsg(string s)
        {
            try { InformationManager.DisplayMessage(new InformationMessage(s)); } catch { }
        }
    }

    internal static class Compat
    {
        public static PartyBase ToPartyBase(object obj)
        {
            if (obj is PartyBase pb && pb != null) return pb;

            if (obj != null)
            {
                var ty = obj.GetType();
                var name = ty.FullName ?? string.Empty;

                // TOR summons → Besitzerpartei ableiten
                if (name.Contains("TOR_Core.AbilitySystem.SummonedCombatant"))
                {
                    var ownerParty =
                        GetProp<PartyBase>(obj, "OwnerParty") ??
                        GetProp<PartyBase>(obj, "Party") ??
                        GetField<PartyBase>(obj, "OwnerParty") ??
                        GetField<PartyBase>(obj, "Party");
                    if (ownerParty != null) return ownerParty;

                    var summoner = GetProp<object>(obj, "SummonerAgent") ?? GetProp<object>(obj, "CasterAgent");
                    var origin = summoner != null ? GetProp<object>(summoner, "Origin") ?? GetField<object>(summoner, "Origin") : null;
                    if (origin != null)
                    {
                        var bc = GetProp<object>(origin, "BattleCombatant") ?? GetField<object>(origin, "BattleCombatant");
                        if (bc is PartyBase pbo) return pbo;
                    }
                }

                // generische Hülle mit BattleCombatant
                var bc2 = GetProp<object>(obj, "BattleCombatant") ?? GetField<object>(obj, "BattleCombatant");
                if (bc2 is PartyBase pb2) return pb2;
            }

            // Fallback: Spielerpartei
            return MobileParty.MainParty?.Party;
        }

        private static T GetProp<T>(object o, string n) where T : class
        {
            var p = o.GetType().GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p != null && typeof(T).IsAssignableFrom(p.PropertyType) ? p.GetValue(o) as T : null;
        }
        private static T GetField<T>(object o, string n) where T : class
        {
            var f = o.GetType().GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return f != null && typeof(T).IsAssignableFrom(f.FieldType) ? f.GetValue(o) as T : null;
        }
    }

    [HarmonyPatch]
    internal static class ShowBattleResultsTranspile
    {
        public static int _lastRewriteCount;

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = new List<CodeInstruction>(instructions);
            var toPartyBase = AccessTools.Method(typeof(Compat), nameof(Compat.ToPartyBase));
            var partyBaseT = typeof(PartyBase);

            int rewrites = 0;

            for (int i = 0; i < list.Count; i++)
            {
                var ci = list[i];

                // Ersetze JEDE direkte Umwandlung auf PartyBase
                if (ci.opcode == OpCodes.Castclass && ci.operand is Type t && t == partyBaseT)
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = toPartyBase;
                    rewrites++;
                }

                // defensive: auch unbox.any PartyBase
                if (ci.opcode == OpCodes.Unbox_Any && ci.operand is Type t2 && t2 == partyBaseT)
                {
                    // Unbox_Any PartyBase -> Box -> Call ToPartyBase
                    // Einfacher: ersetze mit Call ToPartyBase (nutzt object vom Stack)
                    ci.opcode = OpCodes.Call;
                    ci.operand = toPartyBase;
                    rewrites++;
                }

                list[i] = ci;
            }

            _lastRewriteCount = rewrites;
            return list;
        }

        // Airbag: Falls irgendwo noch ein Cast durchrutscht, verhindere Crash.
        public static Exception Finalizer(Exception __exception)
            => __exception is InvalidCastException ? null : __exception;
    }
}
