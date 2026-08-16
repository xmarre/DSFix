using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.Library;

namespace DSFix
{
    internal static class PromotionNamingPatch
    {
        private const string HarmonyId = "xmarre.dsfix.tor.promoted.names.v1.7.2";
        private const string PromotionManagerTypeName = "DistinguishedService.PromotionManager";
        private const string CharacterObjectTypeName = "TaleWorlds.CampaignSystem.CharacterObject";
        private const string HeroTypeName = "TaleWorlds.CampaignSystem.Hero";
        private static readonly object PatchLock = new object();
        private static readonly object RandomLock = new object();
        private static readonly object LogLock = new object();
        private static readonly Random Random = new Random();
        private static Harmony _harmony;
        private static AssemblyLoadEventHandler _assemblyLoadHandler;
        private static bool _initialized;

        [ThreadStatic]
        private static PromotionState _currentState;

        internal static void Initialize()
        {
            lock (PatchLock)
            {
                if (_initialized)
                    return;
                _initialized = true;
                _harmony = new Harmony(HarmonyId);
                TryPatchAssembly(AppDomain.CurrentDomain.GetAssemblies());
                _assemblyLoadHandler = OnAssemblyLoad;
                AppDomain.CurrentDomain.AssemblyLoad += _assemblyLoadHandler;
                Log("DSFix v1.7.2 naming patch initialized.");
            }
        }

        internal static void Reset()
        {
            lock (PatchLock)
            {
                if (_assemblyLoadHandler != null)
                {
                    try { AppDomain.CurrentDomain.AssemblyLoad -= _assemblyLoadHandler; } catch { }
                    _assemblyLoadHandler = null;
                }
                try { _harmony?.UnpatchAll(HarmonyId); } catch (Exception ex) { Log("Failed to unpatch DSFix naming hooks: " + ex.Message); }
                _harmony = null;
                _currentState = null;
                _initialized = false;
            }
        }

        private static void OnAssemblyLoad(object sender, AssemblyLoadEventArgs args)
        {
            TryPatchAssembly(new[] { args.LoadedAssembly });
        }

        private static void TryPatchAssembly(IEnumerable<Assembly> assemblies)
        {
            if (_harmony == null)
                return;

            Type managerType = null;
            foreach (Assembly assembly in assemblies)
            {
                try { managerType = assembly.GetType(PromotionManagerTypeName, false); } catch { }
                if (managerType != null)
                    break;
            }
            if (managerType == null)
                managerType = FindLoadedType(PromotionManagerTypeName);
            if (managerType == null)
                return;

            try
            {
                PatchPromotionManager(managerType);
            }
            catch (Exception ex)
            {
                Log("Failed to initialize DSFix v1.7.2 naming patch: " + Unwrap(ex));
            }
        }

        private static void PatchPromotionManager(Type managerType)
        {
            MethodInfo promote = FindMethod(managerType, "PromoteUnit", CharacterObjectTypeName, null);
            MethodInfo suffix = FindMethod(managerType, "GetNameSuffix", CharacterObjectTypeName, typeof(string));
            MethodInfo assign = FindMethod(managerType, "AssignSkills", HeroTypeName, null);
            MethodInfo assignRandom = FindMethod(managerType, "AssignSkillsRandomly", HeroTypeName, null);

            PatchMethod(promote, nameof(PromotionPrefix), nameof(PromotionPostfix), nameof(PromotionFinalizer), "promotion entry point");
            PatchMethod(suffix, nameof(NameSuffixPrefix), null, null, "live suffix generator");
            PatchMethod(assign, nameof(BeforeSkillAssignment), null, null, "pre-inquiry name enforcement");
            PatchMethod(assignRandom, nameof(BeforeSkillAssignment), null, null, "pre-inquiry name enforcement");
        }

        private static void PatchMethod(MethodInfo original, string prefixName, string postfixName, string finalizerName, string role)
        {
            if (original == null)
                return;
            PatchInfo info = Harmony.GetPatchInfo(original);
            if (info != null && info.Owners.Contains(HarmonyId))
                return;

            HarmonyMethod prefix = prefixName == null ? null : new HarmonyMethod(typeof(PromotionNamingPatch), prefixName);
            HarmonyMethod postfix = postfixName == null ? null : new HarmonyMethod(typeof(PromotionNamingPatch), postfixName);
            HarmonyMethod finalizer = finalizerName == null ? null : new HarmonyMethod(typeof(PromotionNamingPatch), finalizerName);
            _harmony.Patch(original, prefix: prefix, postfix: postfix, finalizer: finalizer);
            Log("Patched Distinguished Service " + role + ": " + original.DeclaringType.FullName + "." + original.Name + ".");
        }

        private static MethodInfo FindMethod(Type type, string name, string firstParameterType, Type returnType)
        {
            MethodInfo[] matches = type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(m => m.Name == name)
                .Where(m => returnType == null || m.ReturnType == returnType)
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length > 0 && string.Equals(p[0].ParameterType.FullName, firstParameterType, StringComparison.Ordinal);
                }).ToArray();
            if (matches.Length == 1)
                return matches[0];
            if (matches.Length == 0)
            {
                Log("No compatible " + name + "(" + firstParameterType + ", ...) method found on " + type.FullName + ".");
                return null;
            }
            Log("Multiple compatible " + name + " methods found on " + type.FullName + "; leaving that hook unpatched.");
            return null;
        }

        private static void PromotionPrefix(object __0)
        {
            PromotionState state = BuildState(__0);
            state.Previous = _currentState;
            _currentState = state;
        }

        private static void PromotionPostfix()
        {
            CompleteState(_currentState, "PromoteUnit postfix");
        }

        private static Exception PromotionFinalizer(Exception __exception)
        {
            PromotionState state = _currentState;
            try { CompleteState(state, "PromoteUnit finalizer"); }
            catch (Exception ex) { Log("Failed to finalize promoted name: " + Unwrap(ex)); }
            _currentState = state?.Previous;
            return __exception;
        }

        private static void BeforeSkillAssignment(object __0)
        {
            PromotionState state = _currentState;
            if (state == null || __0 == null)
                return;
            try { ApplyName(state, __0, "before skill inquiry"); }
            catch (Exception ex) { Log("Failed to enforce promoted name before skill inquiry: " + Unwrap(ex)); }
        }

        private static bool NameSuffixPrefix(ref string __result)
        {
            PromotionState state = _currentState;
            if (state == null || string.IsNullOrWhiteSpace(state.TroopTitle))
                return true;
            try
            {
                __result = " the " + state.TroopTitle;
                return false;
            }
            catch (Exception ex)
            {
                Log("Failed to replace Distinguished Service name suffix: " + Unwrap(ex));
                return true;
            }
        }

        private static PromotionState BuildState(object sourceTroop)
        {
            PromotionState state = new PromotionState { SourceTroop = sourceTroop };
            if (sourceTroop == null)
                return state;

            state.SourceCulture = ReadMember(sourceTroop, "Culture");
            if (state.SourceCulture == null)
            {
                Log("Promotion naming skipped because the source troop has no culture.");
                return state;
            }

            bool isFemale = ReadBoolean(sourceTroop, "IsFemale");
            state.TroopTitle = NormalizeTroopTitle(SafeText(ReadMember(sourceTroop, "Name")));
            if (string.IsNullOrWhiteSpace(state.TroopTitle))
            {
                Log("Promotion naming skipped because the source troop title is empty.");
                return state;
            }

            List<string> names = ReadNonEmptyTexts(ReadMember(state.SourceCulture, isFemale ? "FemaleNameList" : "MaleNameList"));
            state.PoolName = isFemale ? "FemaleNameList" : "MaleNameList";
            if (names.Count == 0)
            {
                names = ReadSameCultureTemplateNames(state.SourceCulture, isFemale);
                state.PoolName = "same-culture, same-sex wanderer templates";
            }

            if (names.Count > 0)
            {
                lock (RandomLock)
                    state.FirstName = names[Random.Next(names.Count)];
            }
            else
            {
                state.PoolName = "existing Distinguished Service first name (culture pool unavailable)";
            }

            state.ExistingCompanions = new HashSet<object>(GetPlayerCompanions(), ReferenceEqualityComparer.Instance);
            Log("Captured promotion source troop '" + SafeText(ReadMember(sourceTroop, "Name")) + "' from culture '" +
                SafeText(ReadMember(state.SourceCulture, "StringId")) + "'; first-name source: " + state.PoolName + ".");
            return state;
        }

        private static void CompleteState(PromotionState state, string stage)
        {
            if (state == null || state.Completed)
                return;
            object hero = FindNewPlayerCompanion(state);
            if (hero == null)
            {
                Log("Promotion completed, but the new hero could not be identified for source troop '" +
                    SafeText(ReadMember(state.SourceTroop, "Name")) + "'.");
                return;
            }
            ApplyName(state, hero, stage);
            state.Completed = true;
        }

        private static void ApplyName(PromotionState state, object hero, string stage)
        {
            if (state == null || hero == null || state.SourceCulture == null || string.IsNullOrWhiteSpace(state.TroopTitle))
                return;

            WriteMember(hero, "Culture", state.SourceCulture);
            string firstName = state.FirstName;
            if (string.IsNullOrWhiteSpace(firstName))
                firstName = SafeText(ReadMember(hero, "FirstName"));
            if (string.IsNullOrWhiteSpace(firstName))
            {
                Log("Could not apply TOR promoted name because no first name was available for '" + state.TroopTitle + "'.");
                return;
            }

            string fullName = firstName.Trim() + " the " + state.TroopTitle;
            if (string.Equals(fullName, state.LastLoggedName, StringComparison.Ordinal))
                return;

            MethodInfo setName = FindSetNameMethod(hero.GetType());
            if (setName == null)
            {
                Log("Could not apply TOR promoted name because Hero.SetName(TextObject, TextObject) was not found.");
                return;
            }
            Type textObjectType = setName.GetParameters()[0].ParameterType;
            object fullNameText = CreateTextObject(textObjectType, fullName);
            object firstNameText = CreateTextObject(textObjectType, firstName.Trim());
            if (fullNameText == null || firstNameText == null)
            {
                Log("Could not create TextObject for promoted hero name.");
                return;
            }

            setName.Invoke(hero, new[] { fullNameText, firstNameText });
            state.NamedHero = hero;
            state.FullName = fullName;
            state.LastLoggedName = fullName;
            Log("Applied TOR culture-accurate promoted name '" + fullName + "' (source troop " + state.TroopTitle +
                ", culture " + SafeText(ReadMember(state.SourceCulture, "StringId")) + ", pool " + state.PoolName + ", stage " + stage + ").");
        }

        private static object FindNewPlayerCompanion(PromotionState state)
        {
            if (state.NamedHero != null)
                return state.NamedHero;
            foreach (object hero in GetPlayerCompanions())
            {
                if (state.ExistingCompanions == null || !state.ExistingCompanions.Contains(hero))
                    return hero;
            }
            return null;
        }

        private static IEnumerable<object> GetPlayerCompanions()
        {
            Type clanType = FindLoadedType("TaleWorlds.CampaignSystem.Clan");
            object playerClan = ReadStaticMember(clanType, "PlayerClan");
            object companions = ReadMember(playerClan, "Companions");
            foreach (object item in ReadObjects(companions))
                yield return item;
        }

        private static List<string> ReadSameCultureTemplateNames(object culture, bool isFemale)
        {
            List<string> result = new List<string>();
            object templates = ReadMember(culture, "NotableAndWandererTemplates") ?? ReadMember(culture, "NotableTemplates");
            foreach (object template in ReadObjects(templates))
            {
                if (ReadBoolean(template, "IsFemale") != isFemale)
                    continue;
                string first = SafeText(ReadMember(template, "FirstName"));
                if (string.IsNullOrWhiteSpace(first))
                {
                    string full = SafeText(ReadMember(template, "Name"));
                    if (!string.IsNullOrWhiteSpace(full))
                        first = full.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                }
                if (!string.IsNullOrWhiteSpace(first))
                    result.Add(first.Trim());
            }
            return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<string> ReadNonEmptyTexts(object collection)
        {
            return ReadObjects(collection)
                .Select(SafeText)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeTroopTitle(string title)
        {
            if (string.IsNullOrWhiteSpace(title))
                return string.Empty;
            string value = title.Trim();
            while (value.StartsWith("the ", StringComparison.OrdinalIgnoreCase))
                value = value.Substring(4).TrimStart();
            return value;
        }

        private static MethodInfo FindSetNameMethod(Type heroType)
        {
            return heroType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "SetName" && m.GetParameters().Length == 2
                    && string.Equals(m.GetParameters()[0].ParameterType.FullName, "TaleWorlds.Localization.TextObject", StringComparison.Ordinal)
                    && string.Equals(m.GetParameters()[1].ParameterType.FullName, "TaleWorlds.Localization.TextObject", StringComparison.Ordinal));
        }

        private static object CreateTextObject(Type type, string text)
        {
            try
            {
                ConstructorInfo ctor = type.GetConstructor(new[] { typeof(string), typeof(Dictionary<string, object>) });
                if (ctor != null)
                    return ctor.Invoke(new object[] { text, null });
                ctor = type.GetConstructor(new[] { typeof(string) });
                if (ctor != null)
                    return ctor.Invoke(new object[] { text });
            }
            catch { }
            return null;
        }

        private static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    Type type = assembly.GetType(fullName, false);
                    if (type != null)
                        return type;
                }
                catch { }
            }
            return null;
        }

        private static object ReadStaticMember(Type type, string name)
        {
            if (type == null)
                return null;
            try
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(null, null);
            }
            catch { }
            try
            {
                FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    return field.GetValue(null);
            }
            catch { }
            return null;
        }

        private static object ReadMember(object target, string name)
        {
            if (target == null)
                return null;
            Type type = target.GetType();
            try
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(target, null);
            }
            catch { }
            try
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                    return field.GetValue(target);
            }
            catch { }
            return null;
        }

        private static bool WriteMember(object target, string name, object value)
        {
            if (target == null)
                return false;
            Type type = target.GetType();
            try
            {
                PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                MethodInfo setter = property?.GetSetMethod(true);
                if (setter != null && (value == null || property.PropertyType.IsInstanceOfType(value)))
                {
                    setter.Invoke(target, new[] { value });
                    return true;
                }
            }
            catch { }
            try
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && !field.IsInitOnly && (value == null || field.FieldType.IsInstanceOfType(value)))
                {
                    field.SetValue(target, value);
                    return true;
                }
            }
            catch { }
            return false;
        }

        private static bool ReadBoolean(object target, string name)
        {
            object value = ReadMember(target, name);
            return value is bool b && b;
        }

        private static IEnumerable<object> ReadObjects(object collection)
        {
            IEnumerable enumerable = collection as IEnumerable;
            if (enumerable == null)
                yield break;
            IEnumerator enumerator = null;
            try { enumerator = enumerable.GetEnumerator(); }
            catch { yield break; }
            try
            {
                while (enumerator.MoveNext())
                    yield return enumerator.Current;
            }
            finally
            {
                (enumerator as IDisposable)?.Dispose();
            }
        }

        private static string SafeText(object value)
        {
            if (value == null)
                return string.Empty;
            try { return value.ToString() ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static Exception Unwrap(Exception ex)
        {
            TargetInvocationException tie = ex as TargetInvocationException;
            return tie?.InnerException ?? ex;
        }

        private static void Log(string message)
        {
            try
            {
                lock (LogLock)
                {
                    string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                        "Mount and Blade II Bannerlord", "Configs");
                    Directory.CreateDirectory(directory);
                    File.AppendAllText(Path.Combine(directory, "DSFix.log"),
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " [Naming 1.7.2] " + message + Environment.NewLine);
                }
            }
            catch { }
        }

        private sealed class PromotionState
        {
            internal PromotionState Previous;
            internal object SourceTroop;
            internal object SourceCulture;
            internal string FirstName;
            internal string TroopTitle;
            internal string PoolName;
            internal string FullName;
            internal string LastLoggedName;
            internal HashSet<object> ExistingCompanions;
            internal object NamedHero;
            internal bool Completed;
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();
            public new bool Equals(object x, object y) { return ReferenceEquals(x, y); }
            public int GetHashCode(object obj) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj); }
        }
    }
}
