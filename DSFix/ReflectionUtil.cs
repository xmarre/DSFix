using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DSFix
{
    internal static class ReflectionUtil
    {
        internal const BindingFlags AllInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal const BindingFlags AllStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static Type FindLoadedType(string fullName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type type = null;
                try { type = assembly.GetType(fullName, false); } catch { }
                if (type != null)
                    return type;
            }
            return null;
        }

        internal static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            if (assembly == null)
                return Enumerable.Empty<Type>();
            try { return assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { return ex.Types.Where(t => t != null); }
            catch { return Enumerable.Empty<Type>(); }
        }

        internal static object ReadMember(object target, string name)
        {
            if (target == null || string.IsNullOrEmpty(name))
                return null;
            Type type = target as Type ?? target.GetType();
            object instance = target is Type ? null : target;
            BindingFlags flags = instance == null ? AllStatic : AllInstance;
            try
            {
                PropertyInfo property = type.GetProperty(name, flags);
                if (property != null && property.GetIndexParameters().Length == 0)
                    return property.GetValue(instance, null);
            }
            catch { }
            try
            {
                FieldInfo field = type.GetField(name, flags);
                if (field != null)
                    return field.GetValue(instance);
            }
            catch { }
            return null;
        }

        internal static bool WriteMember(object target, string name, object value)
        {
            if (target == null || string.IsNullOrEmpty(name))
                return false;
            Type type = target.GetType();
            try
            {
                PropertyInfo property = type.GetProperty(name, AllInstance);
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
                FieldInfo field = type.GetField(name, AllInstance);
                if (field != null && !field.IsInitOnly && (value == null || field.FieldType.IsInstanceOfType(value)))
                {
                    field.SetValue(target, value);
                    return true;
                }
            }
            catch { }
            return false;
        }

        internal static bool ReadBoolean(object target, string name, bool fallback = false)
        {
            object value = ReadMember(target, name);
            return value is bool b ? b : fallback;
        }

        internal static string SafeText(object value)
        {
            if (value == null)
                return string.Empty;
            try { return value.ToString() ?? string.Empty; }
            catch { return string.Empty; }
        }

        internal static int CountEnumerable(object value)
        {
            if (value == null)
                return 0;
            ICollection collection = value as ICollection;
            if (collection != null)
                return collection.Count;
            int count = 0;
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null)
                return 0;
            try
            {
                foreach (object ignored in enumerable)
                    count++;
            }
            catch { return 0; }
            return count;
        }

        internal static IEnumerable<object> ReadObjects(object value)
        {
            IEnumerable enumerable = value as IEnumerable;
            if (enumerable == null)
                yield break;
            IEnumerator enumerator = null;
            try { enumerator = enumerable.GetEnumerator(); }
            catch { yield break; }
            if (enumerator == null)
                yield break;
            try
            {
                while (enumerator.MoveNext())
                    yield return enumerator.Current;
            }
            finally
            {
                IDisposable disposable = enumerator as IDisposable;
                disposable?.Dispose();
            }
        }

        internal static bool TypeNameEquals(Type type, string fullName)
        {
            return type != null && string.Equals(type.FullName, fullName, StringComparison.Ordinal);
        }
    }
}
