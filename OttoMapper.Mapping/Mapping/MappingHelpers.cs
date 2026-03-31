using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace OttoMapper.Mapping
{
    internal static class MappingHelpers
    {
        public static bool IsEnumerable(Type type)
        {
            if (type == typeof(string)) return false;
            return typeof(IEnumerable).IsAssignableFrom(type);
        }

        public static bool IsSimpleType(Type type)
        {
            var candidateType = Nullable.GetUnderlyingType(type) ?? type;
            return candidateType.IsPrimitive
                || candidateType.IsEnum
                || candidateType == typeof(string)
                || candidateType == typeof(decimal)
                || candidateType == typeof(DateTime)
                || candidateType == typeof(DateTimeOffset)
                || candidateType == typeof(TimeSpan)
                || candidateType == typeof(Guid);
        }

        public static bool IsNumericType(Type type)
        {
            var candidateType = Nullable.GetUnderlyingType(type) ?? type;
            switch (Type.GetTypeCode(candidateType))
            {
                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.UInt16:
                case TypeCode.UInt32:
                case TypeCode.UInt64:
                case TypeCode.Int16:
                case TypeCode.Int32:
                case TypeCode.Int64:
                case TypeCode.Decimal:
                case TypeCode.Double:
                case TypeCode.Single:
                    return true;
                default:
                    return false;
            }
        }

        public static bool CanConvertSimpleType(Type sourceType, Type destinationType)
        {
            var sourceCandidate = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
            var destinationCandidate = Nullable.GetUnderlyingType(destinationType) ?? destinationType;

            if (sourceCandidate == destinationCandidate)
            {
                return true;
            }

            if (sourceCandidate.IsEnum && destinationCandidate.IsEnum)
            {
                return true;
            }

            if (sourceCandidate.IsEnum && IsNumericType(destinationCandidate))
            {
                return true;
            }

            if (destinationCandidate.IsEnum && IsNumericType(sourceCandidate))
            {
                return true;
            }

            return IsNumericType(sourceCandidate) && IsNumericType(destinationCandidate);
        }

        public static object? ConvertSimpleValue(object? value, Type destinationType)
        {
            if (value == null)
            {
                return null;
            }

            var destinationCandidate = Nullable.GetUnderlyingType(destinationType) ?? destinationType;
            var sourceCandidate = value.GetType();

            if (destinationCandidate.IsAssignableFrom(sourceCandidate))
            {
                return value;
            }

            if (destinationCandidate.IsEnum)
            {
                if (value is string enumName)
                {
                    return Enum.Parse(destinationCandidate, enumName, ignoreCase: true);
                }

                var enumUnderlyingType = Enum.GetUnderlyingType(destinationCandidate);
                var numericValue = Convert.ChangeType(value, enumUnderlyingType);
                return Enum.ToObject(destinationCandidate, numericValue!);
            }

            if (sourceCandidate.IsEnum)
            {
                var sourceUnderlyingType = Enum.GetUnderlyingType(sourceCandidate);
                var numericValue = Convert.ChangeType(value, sourceUnderlyingType);
                return Convert.ChangeType(numericValue, destinationCandidate);
            }

            return Convert.ChangeType(value, destinationCandidate);
        }

        public static bool CanBeNull(Type type)
        {
            return !type.IsValueType || Nullable.GetUnderlyingType(type) != null;
        }

        public static object CreateObjectInstance(Type type)
        {
            if (type.IsValueType)
            {
                return Activator.CreateInstance(type)!;
            }

            if (type.IsEnum)
            {
                return Enum.ToObject(type, 0);
            }

            if (type.IsAbstract || type.IsInterface)
            {
                throw new InvalidOperationException($"Destination type '{type.FullName}' cannot be instantiated.");
            }

            var constructors = type
                .GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .OrderBy(c => c.IsPublic ? 0 : 1)
                .ThenBy(c => c.GetParameters().Length)
                .ToArray();

            foreach (var constructor in constructors)
            {
                try
                {
                    var args = CreateConstructorArguments(constructor.GetParameters());
                    return constructor.Invoke(args);
                }
                catch
                {
                }
            }

            throw new InvalidOperationException($"Destination type '{type.FullName}' could not be instantiated. Provide ConstructUsing() if constructor arguments are required.");
        }

        private static object?[] CreateConstructorArguments(ParameterInfo[] parameters)
        {
            var args = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].HasDefaultValue)
                {
                    args[i] = parameters[i].DefaultValue;
                }
                else
                {
                    args[i] = GetDefaultValue(parameters[i].ParameterType);
                }
            }

            return args;
        }

        private static object? GetDefaultValue(Type type)
        {
            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        public static Type? GetEnumerableElementType(Type type)
        {
            if (type.IsArray) return type.GetElementType();
            if (type.IsGenericType && typeof(IEnumerable<>).IsAssignableFrom(type.GetGenericTypeDefinition()))
            {
                return type.GetGenericArguments()[0];
            }

            var intf = type.GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>));
            return intf?.GetGenericArguments()[0];
        }

        public static object MapCollection(object sourceCollection, Type sourceElementType, Type destElementType, Func<object, object> elementMapper, Type destCollectionType)
        {
            if (sourceCollection == null) return null!;

            var sourceEnum = (IEnumerable)sourceCollection;

            // try to get count
            int count = -1;
            if (sourceCollection is ICollection coll)
            {
                count = coll.Count;
            }

            var listType = typeof(List<>).MakeGenericType(destElementType);
            IList list;

            if (count > 0)
            {
                try
                {
                    var ctor = listType.GetConstructor(new[] { typeof(int) });
                    if (ctor != null)
                    {
                        list = (IList)ctor.Invoke(new object[] { count });
                    }
                    else
                    {
                        list = Activator.CreateInstance(listType) as IList
                            ?? throw new InvalidOperationException($"Could not create destination list instance for '{listType.FullName}'.");
                    }
                }
                catch
                {
                    list = Activator.CreateInstance(listType) as IList
                        ?? throw new InvalidOperationException($"Could not create destination list instance for '{listType.FullName}'.");
                }
            }
            else
            {
                list = Activator.CreateInstance(listType) as IList
                    ?? throw new InvalidOperationException($"Could not create destination list instance for '{listType.FullName}'.");
            }

            foreach (var item in sourceEnum)
            {
                var mapped = elementMapper(item);
                list.Add(mapped);
            }

            if (destCollectionType.IsArray)
            {
                var arr = Array.CreateInstance(destElementType, list.Count);
                list.CopyTo(arr, 0);
                return arr;
            }

            if (destCollectionType.IsAssignableFrom(listType)) return list;

            try
            {
                var constructed = Activator.CreateInstance(destCollectionType, list);
                if (constructed != null) return constructed;
            }
            catch (MissingMethodException)
            {
                throw new InvalidOperationException($"Could not construct destination collection '{destCollectionType.FullName}'. Ensure it has a constructor accepting IEnumerable or IList.");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Could not construct destination collection '{destCollectionType.FullName}'.", ex);
            }

            return list;
        }

        public static IEnumerable<TDestination> MapCollectionGeneric<TSource, TDestination>(IEnumerable<TSource> source, Func<TSource, TDestination> mapper)
        {
            if (source == null) yield break;
            foreach (var s in source)
            {
                yield return mapper(s);
            }
        }

        public static TDestination[] MapToArrayGeneric<TSource, TDestination>(IEnumerable<TSource> source, Func<TSource, TDestination> mapper)
        {
            if (source == null) return Array.Empty<TDestination>();
            if (source is ICollection<TSource> coll)
            {
                if (coll.Count == 0) return Array.Empty<TDestination>();
                var arr = new TDestination[coll.Count];
                int i = 0;
                foreach (var s in coll)
                {
                    arr[i++] = mapper(s);
                }
                return arr;
            }
            var list = new List<TDestination>();
            foreach (var s in source)
            {
                list.Add(mapper(s));
            }
            return list.ToArray();
        }

        public static List<TDestination> MapToListGeneric<TSource, TDestination>(IEnumerable<TSource> source, Func<TSource, TDestination> mapper)
        {
            if (source == null) return new List<TDestination>();
            if (source is ICollection<TSource> coll)
            {
                var list = new List<TDestination>(coll.Count);
                foreach (var s in coll)
                {
                    list.Add(mapper(s));
                }
                return list;
            }
            var res = new List<TDestination>();
            foreach (var s in source)
            {
                res.Add(mapper(s));
            }
            return res;
        }

        public static object InvokeTypedDelegate<TSource, TDestination>(Func<TSource, TDestination> del, object src)
        {
            return del((TSource)src)!;
        }

        public static void RunBeforeMaps(TypeMap typeMap, object source, object destination)
        {
            foreach (var action in typeMap.BeforeMapActions)
            {
                action(source, destination);
            }
        }

        public static void RunAfterMaps(TypeMap typeMap, object source, object destination)
        {
            foreach (var action in typeMap.AfterMapActions)
            {
                action(source, destination);
            }
        }

        public static void ApplyPathMaps(TypeMap typeMap, object source, object destination)
        {
            foreach (var pathMap in typeMap.PathMaps)
            {
                if (pathMap.Ignore || pathMap.Resolver == null)
                {
                    continue;
                }

                if (pathMap.Condition != null && !pathMap.Condition(source))
                {
                    continue;
                }

                if (pathMap.ConditionWithDestination != null && !pathMap.ConditionWithDestination(source, destination))
                {
                    continue;
                }

                var value = pathMap.Resolver(source);
                if (value == null && pathMap.NullSubstitute != null)
                {
                    value = pathMap.NullSubstitute;
                }

                if (value != null)
                {
                    SetPathValue(destination, pathMap.Path, value);
                }
            }
        }

        private static void SetPathValue(object destination, string path, object value)
        {
            var segments = path.Split('.');
            object current = destination;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                var property = current.GetType().GetProperty(segments[i]);
                if (property == null || !property.CanRead)
                {
                    return;
                }

                var nested = property.GetValue(current);
                if (nested == null)
                {
                    if (!property.CanWrite)
                    {
                        return;
                    }

                    nested = Activator.CreateInstance(property.PropertyType)
                        ?? throw new InvalidOperationException($"Could not create nested destination instance for '{property.PropertyType.FullName}'.");
                    property.SetValue(current, nested);
                }

                current = nested;
            }

            var leaf = current.GetType().GetProperty(segments[segments.Length - 1]);
            if (leaf == null || !leaf.CanWrite)
            {
                return;
            }

            leaf.SetValue(current, value);
        }
    }
}
