using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace DSFix
{
    internal static class LoreNamePatch
    {
        private const string HarmonyId = "xmarre.dsfix.tor.promoted.names";
        private const string PromotionManagerTypeName = "DistinguishedService.PromotionManager";
        private const string CharacterObjectTypeName = "TaleWorlds.CampaignSystem.CharacterObject";
        private const string HeroTypeName = "TaleWorlds.CampaignSystem.Hero";
        private const string NameGeneratorTypeName = "TaleWorlds.CampaignSystem.NameGenerator";
        private static readonly object PatchLock = new object();
        private static readonly object MissingPoolLock = new object();
        private static readonly HashSet<string> MissingPoolsLogged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static Harmony _harmony;
        private static bool _patched;

        [ThreadStatic]
        private static PromotionContext _current;

        internal static void TryPatch()
        {
            if (_patched)
                return;

            lock (PatchLock)
            {
                if (_patched)
                    return;

                Type managerType = ReflectionUtil.FindLoadedType(PromotionManagerTypeName);
                Type nameGeneratorType = ReflectionUtil.FindLoadedType(NameGeneratorTypeName);
                if (managerType == null || nameGeneratorType == null)
                    return;

                MethodInfo promoteUnit = FindPromoteUnit(managerType);
                MethodInfo suffix = FindSuffixTarget(managerType);
                MethodInfo externalNamesGetter = FindExternalNamesGetter(managerType);
                MethodInfo firstName = FindFirstNameTarget(nameGeneratorType);

                _harmony = new Harmony(HarmonyId);
                try
                {
                    _harmony.Patch(promoteUnit,
                        prefix: new HarmonyMethod(typeof(LoreNamePatch), nameof(PromotionPrefix)),
                        finalizer: new HarmonyMethod(typeof(LoreNamePatch), nameof(PromotionFinalizer)));
                    _harmony.Patch(firstName,
                        prefix: new HarmonyMethod(typeof(LoreNamePatch), nameof(FirstNamePrefix)),
                        finalizer: new HarmonyMethod(typeof(LoreNamePatch), nameof(FirstNameFinalizer)));
                    if (externalNamesGetter != null)
                    {
                        _harmony.Patch(externalNamesGetter,
                            prefix: new HarmonyMethod(typeof(LoreNamePatch), nameof(ExternalNamesPrefix)));
                    }
                    _harmony.Patch(suffix,
                        prefix: new HarmonyMethod(typeof(LoreNamePatch), nameof(SuffixPrefix)));
                }
                catch
                {
                    try { _harmony.UnpatchAll(HarmonyId); } catch { }
                    _harmony = null;
                    throw;
                }

                _patched = true;
                DSLog.Write(
                    "TOR promoted-troop naming patches applied: source-culture name pool" +
                    (externalNamesGetter != null ? ", external-list bypass" : ", external-list getter not present; optional bypass hook skipped") +
                    ", and localized troop-title suffix. Targets: " +
                    promoteUnit.DeclaringType.FullName + ".PromoteUnit, " + firstName.DeclaringType.FullName + ".GenerateHeroFirstName, " +
                    suffix.DeclaringType.FullName + ".GetNameSuffix.", true);
            }
        }

        internal static void Reset()
        {
            _current = null;
            if (_harmony != null)
            {
                try { _harmony.UnpatchAll(HarmonyId); } catch { }
            }
            _harmony = null;
            _patched = false;
        }

        private static MethodInfo FindPromoteUnit(Type managerType)
        {
            MethodInfo[] matches = managerType.GetMethods(ReflectionUtil.AllInstance)
                .Where(m => m.Name == "PromoteUnit")
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 3
                        && ReflectionUtil.TypeNameEquals(p[0].ParameterType, CharacterObjectTypeName)
                        && p[1].ParameterType == typeof(int)
                        && p[2].ParameterType == typeof(bool);
                }).ToArray();
            if (matches.Length != 1)
                throw new MissingMethodException(matches.Length > 1
                    ? "Multiple PromoteUnit(CharacterObject, int, bool) methods were found."
                    : "PromoteUnit(CharacterObject, int, bool)");
            return matches[0];
        }

        private static MethodInfo FindSuffixTarget(Type managerType)
        {
            MethodInfo[] matches = managerType.GetMethods(ReflectionUtil.AllInstance)
                .Where(m => m.Name == "GetNameSuffix")
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 1 && ReflectionUtil.TypeNameEquals(p[0].ParameterType, CharacterObjectTypeName) && m.ReturnType == typeof(string);
                }).ToArray();
            if (matches.Length != 1)
                throw new MissingMethodException(matches.Length > 1
                    ? "Multiple GetNameSuffix(CharacterObject) methods were found."
                    : "GetNameSuffix(CharacterObject)");
            return matches[0];
        }

        private static MethodInfo FindExternalNamesGetter(Type managerType)
        {
            MethodInfo[] matches = managerType.GetMethods(ReflectionUtil.AllInstance)
                .Where(m => m.Name == "get_using_extern_namelist" && m.GetParameters().Length == 0 && m.ReturnType == typeof(bool))
                .ToArray();
            if (matches.Length > 1)
                throw new MissingMethodException("Multiple get_using_extern_namelist methods were found.");
            return matches.Length == 1 ? matches[0] : null;
        }

        private static MethodInfo FindFirstNameTarget(Type nameGeneratorType)
        {
            MethodInfo[] matches = nameGeneratorType.GetMethods(ReflectionUtil.AllStatic)
                .Where(m => m.Name == "GenerateHeroFirstName")
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 1 && ReflectionUtil.TypeNameEquals(p[0].ParameterType, HeroTypeName);
                }).ToArray();
            if (matches.Length != 1)
                throw new MissingMethodException(matches.Length > 1
                    ? "Multiple NameGenerator.GenerateHeroFirstName(Hero) methods were found."
                    : "GenerateHeroFirstName(Hero)");
            return matches[0];
        }

        private static void PromotionPrefix(object __0)
        {
            PromotionContext context = BuildContext(__0);
            context.Previous = _current;
            _current = context;
        }

        private static Exception PromotionFinalizer(Exception __exception)
        {
            PromotionContext context = _current;
            _current = context?.Previous;
            return __exception;
        }

        private static void FirstNamePrefix(object __0, out object __state)
        {
            __state = null;
            PromotionContext context = _current;
            if (context == null || !context.Active || __0 == null)
                return;

            try
            {
                __state = ReflectionUtil.ReadMember(__0, "Culture");
                if (!ReflectionUtil.WriteMember(__0, "Culture", context.SourceCulture))
                    DSLog.Write("Failed to apply source culture before first-name generation: Culture is not writable.");
            }
            catch (Exception ex)
            {
                DSLog.Write("Failed to apply source culture before first-name generation: " + ex.Message);
            }
        }

        private static Exception FirstNameFinalizer(Exception __exception, object __0, object __state)
        {
            if (__0 != null && __state != null)
            {
                try
                {
                    if (!ReflectionUtil.WriteMember(__0, "Culture", __state))
                        DSLog.Write("Failed to restore temporary hero culture after first-name generation: Culture is not writable.");
                }
                catch (Exception ex)
                {
                    DSLog.Write("Failed to restore temporary hero culture after first-name generation: " + ex.Message);
                }
            }
            return __exception;
        }

        private static bool ExternalNamesPrefix(ref bool __result)
        {
            PromotionContext context = _current;
            if (context == null || !context.Active)
                return true;
            __result = false;
            return false;
        }

        private static bool SuffixPrefix(object __0, ref string __result)
        {
            PromotionContext context = _current;
            if (context == null || !context.Active)
                return true;

            try
            {
                __result = BuildTroopTitleSuffix(context.TroopTitle);
                return false;
            }
            catch (Exception ex)
            {
                DSLog.Write("Failed open while generating a TOR troop-title suffix: " + ex.Message);
                return true;
            }
        }

        private static PromotionContext BuildContext(object sourceTroop)
        {
            PromotionContext context = new PromotionContext { SourceTroop = sourceTroop };
            if (sourceTroop == null)
                return context;

            object culture = ReflectionUtil.ReadMember(sourceTroop, "Culture");
            if (culture == null)
                return context;

            bool isFemale = ReflectionUtil.ReadBoolean(sourceTroop, "IsFemale");
            string title = NormalizeTroopName(ReflectionUtil.SafeText(ReflectionUtil.ReadMember(sourceTroop, "Name")));
            object pool = ReflectionUtil.ReadMember(culture, isFemale ? "FemaleNameList" : "MaleNameList");
            int poolCount = ReflectionUtil.CountEnumerable(pool);

            context.SourceCulture = culture;
            context.IsFemale = isFemale;
            context.TroopTitle = title;
            context.Active = poolCount > 0 && !string.IsNullOrWhiteSpace(title);

            if (poolCount == 0)
                LogMissingPoolOnce(culture, isFemale);
            return context;
        }

        private static void LogMissingPoolOnce(object culture, bool isFemale)
        {
            string cultureId = ReflectionUtil.SafeText(ReflectionUtil.ReadMember(culture, "StringId"));
            if (string.IsNullOrWhiteSpace(cultureId))
                cultureId = "<unknown culture>";
            string key = cultureId + (isFemale ? ":female" : ":male");
            lock (MissingPoolLock)
            {
                if (!MissingPoolsLogged.Add(key))
                    return;
            }
            DSLog.Write("TOR culture name pool is empty for " + key +
                ". DSFix left Distinguished Service's original naming path active for this promotion; inspect the loaded TOR culture XML/data.");
        }

        private static string BuildTroopTitleSuffix(string title)
        {
            return string.IsNullOrWhiteSpace(title) ? string.Empty : " the " + title.Trim();
        }

        private static string NormalizeTroopName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return string.Empty;
            string normalized = name.Trim();
            while (normalized.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(4).TrimStart();
            return normalized;
        }

        private sealed class PromotionContext
        {
            internal PromotionContext Previous;
            internal object SourceTroop;
            internal object SourceCulture;
            internal bool IsFemale;
            internal string TroopTitle;
            internal bool Active;
        }
    }
}
