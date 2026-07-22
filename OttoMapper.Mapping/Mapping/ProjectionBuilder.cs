using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace OttoMapper.Mapping
{
    /// <summary>
    /// Builds EF-translatable projection expressions (<c>Expression&lt;Func&lt;TSource, TDestination&gt;&gt;</c>)
    /// from configured maps and naming conventions. Unlike <see cref="ObjectMapExpressionBuilder"/>, which
    /// compiles to a runtime delegate, this builder emits a pure <c>MemberInit</c> tree using only constructs a
    /// LINQ provider (e.g. EF Core) can translate to SQL: property accesses, constructor + member bindings,
    /// inline <c>Enumerable.Select</c> for collections, and inlined <c>MapFrom</c> resolver bodies.
    /// </summary>
    internal sealed class ProjectionBuilder
    {
        private readonly MapperConfiguration _config;
        private readonly Func<Type, Type, LambdaExpression> _buildNested;

        private static readonly MethodInfo EnumerableSelectMethod =
            typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(Enumerable.Select) && m.GetGenericArguments().Length == 2
                            && m.GetParameters().Length == 2
                            && m.GetParameters()[1].ParameterType.GetGenericArguments().Length == 2);

        private static readonly MethodInfo EnumerableToListMethod =
            typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(Enumerable.ToList) && m.GetGenericArguments().Length == 1);

        private static readonly MethodInfo EnumerableToArrayMethod =
            typeof(Enumerable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(Enumerable.ToArray) && m.GetGenericArguments().Length == 1);

        public ProjectionBuilder(MapperConfiguration config, Func<Type, Type, LambdaExpression> buildNested)
        {
            _config = config;
            _buildNested = buildNested;
        }

        /// <summary>
        /// Builds a <c>Func&lt;sourceType, destinationType&gt;</c> lambda expression that projects
        /// <paramref name="sourceType"/> onto <paramref name="destinationType"/>.
        /// </summary>
        public LambdaExpression Build(Type sourceType, Type destinationType)
        {
            var typeMap = _config.GetTypeMap(sourceType, destinationType);

            EnsureMapLevelProjectable(typeMap, sourceType, destinationType);

            if (_config.RequireExplicitMaps && typeMap == null)
            {
                throw new ProjectionException(
                    $"Missing map configuration for '{sourceType.FullName}' -> '{destinationType.FullName}'. ProjectTo requires an explicit CreateMap when RequireExplicitMaps is enabled.");
            }

            var parameterlessCtor = destinationType.IsValueType
                ? null
                : destinationType.GetConstructor(BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (!destinationType.IsValueType && parameterlessCtor == null)
            {
                throw new ProjectionException(
                    $"Destination type '{destinationType.FullName}' has no public parameterless constructor. ProjectTo does not support records/constructor-only types in this version; materialize first and use Map.");
            }

            var srcParam = Expression.Parameter(sourceType, "src");
            var bindings = new List<MemberBinding>();

            bool caseInsensitive = typeMap?.CaseInsensitiveMapping ?? _config.CaseInsensitiveMapping;
            bool ignoreUnderscores = typeMap?.IgnoreUnderscoresInPropertyNames ?? _config.IgnoreUnderscoresInPropertyNames;

            foreach (var destProp in destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!destProp.CanWrite) continue;
                if (destProp.GetIndexParameters().Length > 0) continue;

                var binding = BuildMemberBinding(typeMap, sourceType, destinationType, srcParam, destProp, caseInsensitive, ignoreUnderscores);
                if (binding != null)
                {
                    bindings.Add(binding);
                }
            }

            var newExpr = destinationType.IsValueType ? Expression.New(destinationType) : Expression.New(parameterlessCtor!);
            var memberInit = Expression.MemberInit(newExpr, bindings);
            var funcType = typeof(Func<,>).MakeGenericType(sourceType, destinationType);
            return Expression.Lambda(funcType, memberInit, srcParam);
        }

        private static void EnsureMapLevelProjectable(TypeMap? typeMap, Type sourceType, Type destinationType)
        {
            if (typeMap == null) return;

            if (typeMap.CustomConverter != null || typeMap.TypedCustomConverter != null)
            {
                throw new ProjectionException(
                    $"Map '{sourceType.Name}' -> '{destinationType.Name}' uses ConvertUsing, which is not translatable to SQL. Materialize the query first and use Map.");
            }

            if (typeMap.ConstructUsing != null || typeMap.TypedConstructUsing != null)
            {
                throw new ProjectionException(
                    $"Map '{sourceType.Name}' -> '{destinationType.Name}' uses ConstructUsing, which is not translatable to SQL. Materialize the query first and use Map.");
            }

            if (typeMap.BeforeMapActions.Count > 0 || typeMap.AfterMapActions.Count > 0)
            {
                throw new ProjectionException(
                    $"Map '{sourceType.Name}' -> '{destinationType.Name}' uses BeforeMap/AfterMap hooks, which are not translatable to SQL. Materialize the query first and use Map.");
            }

            if (typeMap.PathMaps.Count > 0)
            {
                throw new ProjectionException(
                    $"Map '{sourceType.Name}' -> '{destinationType.Name}' uses ForPath/PathMaps, which are not translatable to SQL. Materialize the query first and use Map.");
            }
        }

        private MemberBinding? BuildMemberBinding(
            TypeMap? typeMap, Type sourceType, Type destinationType, ParameterExpression srcParam,
            PropertyInfo destProp, bool caseInsensitive, bool ignoreUnderscores)
        {
            var name = destProp.Name;

            if (typeMap != null && typeMap.IgnoredMembers.Contains(name))
            {
                return null;
            }

            // Customizations that are not SQL-translatable take precedence over the resolver expression: even
            // when a MapFrom expression is present, a Condition/NullSubstitute cannot be expressed in a
            // projection initializer, so the whole member is rejected.
            if (typeMap != null && typeMap.NullSubstitutes.ContainsKey(name))
            {
                throw new ProjectionException(
                    $"Destination member '{destinationType.Name}.{name}' uses NullSubstitute, which is not translatable to SQL in this version. Use opt.MapFrom(s => s.Value ?? fallback) instead.",
                    name);
            }

            if (typeMap != null && (typeMap.MemberConditions.ContainsKey(name) || typeMap.MemberConditionsWithDestination.ContainsKey(name)))
            {
                throw new ProjectionException(
                    $"Destination member '{destinationType.Name}.{name}' uses Condition, which is not translatable to SQL. Materialize the query first and use Map.",
                    name);
            }

            if (typeMap != null && typeMap.MemberResolverExpressions.TryGetValue(name, out var resolverLambda))
            {
                var body = ParameterReplacer.Replace(resolverLambda.Body, resolverLambda.Parameters[0], srcParam);
                return Expression.Bind(destProp, body);
            }

            if (typeMap != null && typeMap.MemberResolvers.ContainsKey(name))
            {
                // Func-based ForMember without an expression body — not translatable.
                throw new ProjectionException(
                    $"Destination member '{destinationType.Name}.{name}' uses a Func-based ForMember resolver, which is not translatable to SQL. Use opt.MapFrom(s => ...) with an expression instead.",
                    name);
            }

            var srcProp = MappingHelpers.GetPropertyCaseInsensitive(sourceType, name, caseInsensitive, ignoreUnderscores, BindingFlags.Public | BindingFlags.Instance);
            if (srcProp == null || !srcProp.CanRead)
            {
                return null;
            }

            var srcAccess = Expression.Property(srcParam, srcProp);
            var valueExpr = BuildConventionValue(typeMap, srcAccess, srcProp.PropertyType, destProp.PropertyType);
            return Expression.Bind(destProp, valueExpr);
        }

        private Expression BuildConventionValue(TypeMap? typeMap, Expression srcAccess, Type srcPropType, Type dstPropType)
        {
            // Collection member (both enumerable, neither string).
            if (MappingHelpers.IsEnumerable(srcPropType) && MappingHelpers.IsEnumerable(dstPropType))
            {
                return BuildCollectionValue(srcAccess, srcPropType, dstPropType);
            }

            // Simple same-type direct assignment.
            if (srcPropType == dstPropType)
            {
                return srcAccess;
            }

            // Numeric -> numeric (incl. nullable wrapping) via explicit Convert — reliably EF-translatable.
            if (MappingHelpers.IsNumericType(srcPropType) && MappingHelpers.IsNumericType(dstPropType))
            {
                return Expression.Convert(srcAccess, dstPropType);
            }

            // Complex nested object — recurse into a nested projection and inline it.
            if (!MappingHelpers.IsSimpleType(srcPropType) && !MappingHelpers.IsSimpleType(dstPropType))
            {
                var nestedLambda = _buildNested(srcPropType, dstPropType);
                var nestedBody = ParameterReplacer.Replace(nestedLambda.Body, nestedLambda.Parameters[0], srcAccess);

                if (MappingHelpers.CanBeNull(srcPropType))
                {
                    return Expression.Condition(
                        Expression.Equal(srcAccess, Expression.Default(srcPropType)),
                        Expression.Default(dstPropType),
                        nestedBody);
                }

                return nestedBody;
            }

            // Differing simple types that are not numeric (enum<->string, string<->numeric, enum<->enum) are not
            // reliably translatable — require an explicit MapFrom expression.
            throw new ProjectionException(
                $"Cannot project member of type '{srcPropType.Name}' onto '{dstPropType.Name}': the conversion is not reliably translatable to SQL. Provide an explicit opt.MapFrom(s => ...) expression.",
                srcAccess is MemberExpression me ? me.Member.Name : null);
        }

        private Expression BuildCollectionValue(Expression srcAccess, Type srcPropType, Type dstPropType)
        {
            var srcElem = MappingHelpers.GetEnumerableElementType(srcPropType) ?? typeof(object);
            var dstElem = MappingHelpers.GetEnumerableElementType(dstPropType) ?? typeof(object);

            Expression projected;
            if (srcElem == dstElem)
            {
                // Same element type: no per-element mapping needed, just materialize.
                projected = Materialize(srcAccess, srcElem, dstElem, dstPropType);
            }
            else
            {
                // Different element types: build a nested element projection and inline it via Enumerable.Select.
                var elemLambda = _buildNested(srcElem, dstElem);
                var x = Expression.Parameter(srcElem, "x");
                var elemBody = ParameterReplacer.Replace(elemLambda.Body, elemLambda.Parameters[0], x);

                // Guard null collection elements (reference element types) so a null row maps to null rather
                // than throwing; EF translates this as CASE WHEN x IS NULL THEN NULL ELSE ... .
                if (MappingHelpers.CanBeNull(srcElem))
                {
                    elemBody = Expression.Condition(
                        Expression.Equal(x, Expression.Default(srcElem)),
                        Expression.Default(dstElem),
                        elemBody);
                }

                var elemFuncType = typeof(Func<,>).MakeGenericType(srcElem, dstElem);
                var elemLambdaInline = Expression.Lambda(elemFuncType, elemBody, x);

                var srcConverted = Expression.Convert(srcAccess, typeof(IEnumerable<>).MakeGenericType(srcElem));
                var selectCall = Expression.Call(
                    EnumerableSelectMethod.MakeGenericMethod(srcElem, dstElem),
                    srcConverted,
                    elemLambdaInline);

                projected = Materialize(selectCall, dstElem, dstElem, dstPropType);
            }

            if (MappingHelpers.CanBeNull(srcPropType))
            {
                return Expression.Condition(
                    Expression.Equal(srcAccess, Expression.Default(srcPropType)),
                    Expression.Default(dstPropType),
                    projected);
            }

            return projected;
        }

        /// <summary>
        /// Materializes an <see cref="IEnumerable{TElement}"/> expression into the destination collection type
        /// (array via ToArray, otherwise List via ToList, converted to the destination type if needed).
        /// </summary>
        private Expression Materialize(Expression sourceExpression, Type sourceElement, Type destinationElement, Type destinationCollectionType)
        {
            var enumerableElementType = destinationElement;

            if (destinationCollectionType.IsArray)
            {
                var toArray = Expression.Call(EnumerableToArrayMethod.MakeGenericMethod(destinationElement), sourceExpression);
                return toArray.Type == destinationCollectionType ? toArray : Expression.Convert(toArray, destinationCollectionType);
            }

            var toList = Expression.Call(EnumerableToListMethod.MakeGenericMethod(destinationElement), sourceExpression);
            if (destinationCollectionType.IsAssignableFrom(toList.Type))
            {
                return toList.Type == destinationCollectionType ? toList : Expression.Convert(toList, destinationCollectionType);
            }

            // Fall back to the IEnumerable<T> projection itself if assignable (e.g. IEnumerable<T> destination).
            if (destinationCollectionType.IsAssignableFrom(typeof(IEnumerable<>).MakeGenericType(destinationElement)))
            {
                return Expression.Convert(sourceExpression, destinationCollectionType);
            }

            return Expression.Convert(toList, destinationCollectionType);
        }
    }
}