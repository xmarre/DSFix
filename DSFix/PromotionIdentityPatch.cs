using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;

namespace DSFix
{
    internal static class PromotionIdentityPatch
    {
        private const string HarmonyId = "xmarre.dsfix.tor.promoted.identity";
        private const string PromotionManagerTypeName = "DistinguishedService.PromotionManager";
        private const string CharacterObjectTypeName = "TaleWorlds.CampaignSystem.CharacterObject";
        private static readonly object PatchLock = new object();

        private static Harmony _harmony;
        private static bool _patched;
        private static FieldInfo _originCharacterField;
        private static MethodInfo _bodyPropertyRangeSetter;

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
                if (managerType == null)
                    return;

                MethodInfo promoteUnit = FindPromoteUnit(managerType);
                MethodInfo createSpecialHero = FindCreateSpecialHero();
                MethodInfo createHero = FindCreateHero();

                _originCharacterField = typeof(CharacterObject).GetField("_originCharacter", ReflectionUtil.AllInstance);
                if (_originCharacterField == null || _originCharacterField.FieldType != typeof(CharacterObject))
                    throw new MissingFieldException(typeof(CharacterObject).FullName, "_originCharacter");

                PropertyInfo bodyPropertyRange = typeof(BasicCharacterObject).GetProperty("BodyPropertyRange", ReflectionUtil.AllInstance);
                _bodyPropertyRangeSetter = bodyPropertyRange?.GetSetMethod(true);
                if (bodyPropertyRange == null || bodyPropertyRange.PropertyType != typeof(MBBodyProperty) || _bodyPropertyRangeSetter == null)
                    throw new MissingMemberException(typeof(BasicCharacterObject).FullName, "BodyPropertyRange.set");

                _harmony = new Harmony(HarmonyId);
                try
                {
                    _harmony.Patch(
                        promoteUnit,
                        prefix: new HarmonyMethod(typeof(PromotionIdentityPatch), nameof(PromotionPrefix)),
                        finalizer: new HarmonyMethod(typeof(PromotionIdentityPatch), nameof(PromotionFinalizer)));

                    _harmony.Patch(
                        createSpecialHero,
                        prefix: new HarmonyMethod(typeof(PromotionIdentityPatch), nameof(CreateSpecialHeroPrefix)),
                        postfix: new HarmonyMethod(typeof(PromotionIdentityPatch), nameof(CreateSpecialHeroPostfix)),
                        finalizer: new HarmonyMethod(typeof(PromotionIdentityPatch), nameof(CreateSpecialHeroFinalizer)));

                    _harmony.Patch(
                        createHero,
                        prefix: new HarmonyMethod(typeof(PromotionIdentityPatch), nameof(CreateHeroPrefix)),
                        postfix: new HarmonyMethod(typeof(PromotionIdentityPatch), nameof(CreateHeroPostfix)));
                }
                catch
                {
                    try { _harmony.UnpatchAll(HarmonyId); } catch { }
                    _harmony = null;
                    throw;
                }

                _patched = true;
                DSLog.Write(
                    "TOR promoted-troop identity patch applied: Distinguished Service promotions now preserve the source race, race-specific body range, and a body-compatible age before Bannerlord initializes the companion. " +
                    "The temporary source OriginalCharacter is restored after CreateSpecialHero so Distinguished Service's wanderer occupation/template semantics remain unchanged.",
                    true);
            }
        }

        internal static void Reset()
        {
            PromotionContext context = _current;
            while (context != null)
            {
                RestoreOriginalCharacter(context);
                context = context.Previous;
            }

            _current = null;
            if (_harmony != null)
            {
                try { _harmony.UnpatchAll(HarmonyId); } catch { }
            }

            _harmony = null;
            _originCharacterField = null;
            _bodyPropertyRangeSetter = null;
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

        private static MethodInfo FindCreateSpecialHero()
        {
            MethodInfo[] matches = typeof(HeroCreator).GetMethods(ReflectionUtil.AllStatic)
                .Where(m => m.Name == "CreateSpecialHero" && m.ReturnType == typeof(Hero))
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 5
                        && p[0].ParameterType == typeof(CharacterObject)
                        && p[4].ParameterType == typeof(int);
                }).ToArray();

            if (matches.Length != 1)
                throw new MissingMethodException(matches.Length > 1
                    ? "Multiple HeroCreator.CreateSpecialHero(CharacterObject, ..., int) methods were found."
                    : "HeroCreator.CreateSpecialHero(CharacterObject, ..., int)");

            return matches[0];
        }

        private static MethodInfo FindCreateHero()
        {
            MethodInfo[] matches = typeof(HeroCreator).GetMethods(ReflectionUtil.AllStatic)
                .Where(m => m.Name == "CreateHero" && m.ReturnType == typeof(Hero))
                .Where(m =>
                {
                    ParameterInfo[] p = m.GetParameters();
                    return p.Length == 4
                        && p[0].ParameterType == typeof(CharacterObject)
                        && p[1].ParameterType == typeof(bool)
                        && p[2].ParameterType == typeof(CampaignTime)
                        && p[3].ParameterType == typeof(CampaignTime);
                }).ToArray();

            if (matches.Length != 1)
                throw new MissingMethodException(matches.Length > 1
                    ? "Multiple HeroCreator.CreateHero(CharacterObject, bool, CampaignTime, CampaignTime) methods were found."
                    : "HeroCreator.CreateHero(CharacterObject, bool, CampaignTime, CampaignTime)");

            return matches[0];
        }

        private static void PromotionPrefix(object __0, out PromotionContext __state)
        {
            PromotionContext context = new PromotionContext
            {
                Previous = _current,
                Source = __0 as CharacterObject
            };

            context.Active = context.Source != null;
            _current = context;
            __state = context;
        }

        private static Exception PromotionFinalizer(Exception __exception, PromotionContext __state)
        {
            if (__state != null)
            {
                RestoreOriginalCharacter(__state);
                _current = __state.Previous;
            }

            return __exception;
        }

        private static void CreateSpecialHeroPrefix(object __0, int __4, out PromotionContext __state)
        {
            __state = null;

            PromotionContext context = _current;
            if (context == null || !context.Active || context.CreationClaimed)
                return;

            // Distinguished Service creates exactly one special hero for the promoted troop.
            // Claim the first call even when no compatibility correction is required so a
            // nested CreateSpecialHero from a later event cannot inherit this promotion context.
            context.CreationClaimed = true;

            CharacterObject template = __0 as CharacterObject;
            if (template == null)
                return;

            context.Template = template;

            if (context.Source.Race == template.Race)
                return;

            if (context.Source.IsFemale != template.IsFemale)
            {
                DSLog.Write(
                    "Skipped TOR promoted-troop identity correction because Distinguished Service selected a wanderer template with a different sex than the source troop.");
                return;
            }

            if (context.Source.BodyPropertyRange == null)
            {
                DSLog.Write(
                    "Skipped TOR promoted-troop identity correction because the source troop has no BodyPropertyRange.");
                return;
            }

            context.DesiredAge = ClampAgeToSourceBodyRange(__4, context.Source);
            context.NeedsIdentityFix = true;
            __state = context;
        }

        private static void CreateHeroPrefix(
            object __0,
            bool __1,
            ref CampaignTime __2,
            ref CampaignTime __3,
            out PromotionContext __state)
        {
            __state = null;

            PromotionContext context = _current;
            CharacterObject template = __0 as CharacterObject;
            if (context == null
                || !context.NeedsIdentityFix
                || context.CreateHeroConsumed
                || !__1
                || template == null
                || !ReferenceEquals(template, context.Template))
            {
                return;
            }

            context.CreateHeroConsumed = true;
            __state = context;

            try
            {
                // Distinguished Service passes a random 20-49 age to CreateSpecialHero, but
                // Bannerlord ignores that value for wanderer templates and uses the wanderer's
                // own age. Re-run the age model against the actual source troop and a value
                // clamped to that troop's body range before the Hero object is constructed.
                var birthAndDeath = Campaign.Current.Models.HeroCreationModel.GetBirthAndDeathDay(
                    context.Source,
                    true,
                    context.DesiredAge);
                __2 = birthAndDeath.Item1;
                __3 = birthAndDeath.Item2;
            }
            catch (Exception ex)
            {
                DSLog.Write(
                    "Failed open while correcting the promoted troop age; race/body preservation will still be attempted: " +
                    ex.Message);
            }
        }

        private static void CreateHeroPostfix(Hero __result, PromotionContext __state)
        {
            if (__state == null || __result?.CharacterObject == null)
                return;

            PromotionContext context = __state;
            CharacterObject createdCharacter = __result.CharacterObject;

            int originalRace = createdCharacter.Race;
            MBBodyProperty originalBodyPropertyRange = createdCharacter.BodyPropertyRange;

            try
            {
                context.CreatedHero = __result;
                context.CreatedCharacter = createdCharacter;
                context.OriginalOrigin = _originCharacterField.GetValue(createdCharacter) as CharacterObject;

                // Bannerlord's hero initialization derives culture and static body properties
                // from CharacterObject.OriginalCharacter. Temporarily point that lookup at the
                // promoted troop while keeping the clone's wanderer occupation/template data.
                _originCharacterField.SetValue(createdCharacter, context.Source);
                context.OriginSwapped = true;

                createdCharacter.Race = context.Source.Race;
                SetBodyPropertyRange(createdCharacter, context.Source.BodyPropertyRange);
                context.IdentityApplied = true;
            }
            catch (Exception ex)
            {
                context.IdentityApplied = false;

                try
                {
                    createdCharacter.Race = originalRace;
                    if (originalBodyPropertyRange != null)
                        SetBodyPropertyRange(createdCharacter, originalBodyPropertyRange);
                }
                catch (Exception rollbackEx)
                {
                    DSLog.Write("Failed to roll back a partial TOR promoted-troop identity correction: " + rollbackEx);
                }

                RestoreOriginalCharacter(context);
                DSLog.Write("Failed to apply TOR promoted-troop race/body identity: " + ex);
            }
        }

        private static void CreateSpecialHeroPostfix(Hero __result, PromotionContext __state)
        {
            if (__state == null)
                return;

            PromotionContext context = __state;

            // Do not leave the source troop as the hero clone's permanent origin. Bannerlord
            // restores occupation and other template fields from that origin after save/load.
            // Keeping the wanderer origin preserves Distinguished Service's companion semantics.
            RestoreOriginalCharacter(context);

            if (!context.IdentityApplied || __result == null || !ReferenceEquals(__result, context.CreatedHero))
                return;

            try
            {
                // Reassert only the two identity fields that Bannerlord copied from the wrong
                // wanderer template. Everything else remains Distinguished Service/native.
                __result.CharacterObject.Race = context.Source.Race;
                SetBodyPropertyRange(__result.CharacterObject, context.Source.BodyPropertyRange);
                PromotionIdentityCampaignBehavior.TrackPromotion(__result);
            }
            catch (Exception ex)
            {
                DSLog.Write("Failed to finalize TOR promoted-troop race/body identity: " + ex);
            }
        }

        private static Exception CreateSpecialHeroFinalizer(Exception __exception, PromotionContext __state)
        {
            RestoreOriginalCharacter(__state);
            return __exception;
        }

        private static int ClampAgeToSourceBodyRange(int requestedAge, CharacterObject source)
        {
            MBBodyProperty range = source?.BodyPropertyRange;
            if (range == null)
                return requestedAge >= 0 ? requestedAge : 25;

            double minAge = Math.Min(range.BodyPropertyMin.Age, range.BodyPropertyMax.Age);
            double maxAge = Math.Max(range.BodyPropertyMin.Age, range.BodyPropertyMax.Age);
            int minWholeAge = (int)Math.Ceiling(minAge);
            int maxWholeAge = (int)Math.Floor(maxAge);

            int candidate = requestedAge >= 0
                ? requestedAge
                : (int)Math.Round(source.Age, MidpointRounding.AwayFromZero);

            if (minWholeAge <= maxWholeAge)
                return Math.Max(minWholeAge, Math.Min(maxWholeAge, candidate));

            // A narrow fractional range can contain no integer. Bannerlord hero birthdays use
            // integer ages here, so choose the nearest whole age to the valid range midpoint.
            return Math.Max(
                0,
                (int)Math.Round((minAge + maxAge) * 0.5, MidpointRounding.AwayFromZero));
        }

        private static void SetBodyPropertyRange(CharacterObject character, MBBodyProperty bodyPropertyRange)
        {
            if (character == null)
                throw new ArgumentNullException(nameof(character));
            if (bodyPropertyRange == null)
                throw new ArgumentNullException(nameof(bodyPropertyRange));
            if (_bodyPropertyRangeSetter == null)
                throw new MissingMethodException(typeof(BasicCharacterObject).FullName, "set_BodyPropertyRange");

            _bodyPropertyRangeSetter.Invoke(character, new object[] { bodyPropertyRange });
        }

        private static void RestoreOriginalCharacter(PromotionContext context)
        {
            if (context == null || !context.OriginSwapped || context.CreatedCharacter == null || _originCharacterField == null)
                return;

            try
            {
                _originCharacterField.SetValue(context.CreatedCharacter, context.OriginalOrigin);
                context.OriginSwapped = false;
            }
            catch (Exception ex)
            {
                if (!context.RestoreFailureLogged)
                {
                    context.RestoreFailureLogged = true;
                    DSLog.Write(
                        "CRITICAL: failed to restore the Distinguished Service wanderer origin after TOR identity initialization: " +
                        ex);
                }
            }
        }

        private sealed class PromotionContext
        {
            internal PromotionContext Previous;
            internal CharacterObject Source;
            internal CharacterObject Template;
            internal Hero CreatedHero;
            internal CharacterObject CreatedCharacter;
            internal CharacterObject OriginalOrigin;
            internal bool Active;
            internal bool CreationClaimed;
            internal bool NeedsIdentityFix;
            internal bool CreateHeroConsumed;
            internal bool IdentityApplied;
            internal bool OriginSwapped;
            internal bool RestoreFailureLogged;
            internal int DesiredAge;
        }
    }
}
