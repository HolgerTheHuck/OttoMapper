using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OttoMapper.Mapping
{
    internal sealed class ObjectMapExpressionBuilder
    {
        private readonly TypedMapCache _typedCache;
        private readonly Func<Type, Type, Func<object, object>> _getMapFunc;
        private readonly Action<Type, Type> _prepareMap;
        private readonly Action<Type, Type, Delegate> _registerTypedDelegate;

        public ObjectMapExpressionBuilder(
            TypedMapCache typedCache,
            Func<Type, Type, Func<object, object>> getMapFunc,
            Action<Type, Type> prepareMap,
            Action<Type, Type, Delegate> registerTypedDelegate)
        {
            _typedCache = typedCache;
            _getMapFunc = getMapFunc;
            _prepareMap = prepareMap;
            _registerTypedDelegate = registerTypedDelegate;
        }

        public Func<object, object> CreateObjectMap(Type sourceType, Type destinationType, TypeMap? typeMap)
        {
            var srcParamTyped = Expression.Parameter(sourceType, "src");
            var destVar = Expression.Variable(destinationType, "dest");

            var assignDestTyped = CreateDestinationAssignment(typeMap, sourceType, destinationType, srcParamTyped, destVar);
            var blockExpressionsTyped = new List<Expression> { assignDestTyped };

            if (typeMap != null && typeMap.BeforeMapActions.Count > 0)
            {
                var beforeMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "RunBeforeMaps", BindingFlags.Static | BindingFlags.Public);
                blockExpressionsTyped.Add(Expression.Call(beforeMethod, Expression.Constant(typeMap), Expression.Convert(srcParamTyped, typeof(object)), Expression.Convert(destVar, typeof(object))));
            }

            foreach (var destProp in destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!destProp.CanWrite) continue;
                if (typeMap != null && typeMap.IgnoredMembers.Contains(destProp.Name)) continue;

                var valueExpr = CreateMemberValueExpression(typeMap, sourceType, destinationType, srcParamTyped, destVar, destProp);
                if (valueExpr == null) continue;

                if (typeMap != null && typeMap.NullSubstitutes.TryGetValue(destProp.Name, out var nullSubstitute) && nullSubstitute != null && MappingHelpers.CanBeNull(destProp.PropertyType))
                {
                    valueExpr = Expression.Condition(
                        Expression.Equal(Expression.Convert(valueExpr, typeof(object)), Expression.Constant(null)),
                        Expression.Constant(nullSubstitute, destProp.PropertyType),
                        valueExpr);
                }

                var bind = Expression.Assign(Expression.Property(destVar, destProp), valueExpr);
                if (typeMap != null && typeMap.MemberConditionsWithDestination.TryGetValue(destProp.Name, out var destinationCondition))
                {
                    var conditionConst = Expression.Constant(destinationCondition, typeof(Func<object, object, bool>));
                    var conditionInvoke = Expression.Invoke(conditionConst, Expression.Convert(srcParamTyped, typeof(object)), Expression.Convert(destVar, typeof(object)));
                    blockExpressionsTyped.Add(Expression.IfThen(conditionInvoke, bind));
                }
                else if (typeMap != null && typeMap.MemberConditions.TryGetValue(destProp.Name, out var condition))
                {
                    var conditionConst = Expression.Constant(condition, typeof(Func<object, bool>));
                    var conditionInvoke = Expression.Invoke(conditionConst, Expression.Convert(srcParamTyped, typeof(object)));
                    blockExpressionsTyped.Add(Expression.IfThen(conditionInvoke, bind));
                }
                else
                {
                    blockExpressionsTyped.Add(bind);
                }
            }

            AppendPostMapActions(typeMap, srcParamTyped, destVar, blockExpressionsTyped);
            blockExpressionsTyped.Add(destVar);

            var bodyTyped = Expression.Block(new[] { destVar }, blockExpressionsTyped);
            var funcType = typeof(Func<,>).MakeGenericType(sourceType, destinationType);

            Delegate? typedDel = null;
            try
            {
                var typedLambda = Expression.Lambda(funcType, bodyTyped, srcParamTyped);
                typedDel = typedLambda.Compile();
                _registerTypedDelegate(sourceType, destinationType, typedDel);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OttoMapper: Failed to compile typed delegate for '{sourceType.FullName}' -> '{destinationType.FullName}': {ex.Message}");
                typedDel = null;
            }

            var sourceParamObj = Expression.Parameter(typeof(object), "srcObj");
            Expression invokeExpr;
            if (typedDel != null)
            {
                var helper = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "InvokeTypedDelegate", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(sourceType, destinationType);
                var typedConst = Expression.Constant(typedDel, funcType);
                var call = Expression.Call(helper, Expression.Convert(typedConst, typeof(Func<,>).MakeGenericType(sourceType, destinationType)), sourceParamObj);
                invokeExpr = Expression.Convert(call, typeof(object));
            }
            else
            {
                invokeExpr = CreateFallbackObjectBody(typeMap, sourceType, destinationType, sourceParamObj);
            }

            var wrapperLambda = Expression.Lambda<Func<object, object>>(invokeExpr, sourceParamObj);
            return wrapperLambda.Compile();
        }

        private static Expression CreateDestinationAssignment(TypeMap? typeMap, Type sourceType, Type destinationType, ParameterExpression srcParamTyped, ParameterExpression destVar)
        {
            if (typeMap?.TypedConstructUsing != null)
            {
                var typedCtor = typeMap.TypedConstructUsing;
                var typedConst = Expression.Constant(typedCtor, typedCtor.GetType());
                var invoked = Expression.Invoke(typedConst, srcParamTyped);
                return Expression.Assign(destVar, Expression.Convert(invoked, destinationType));
            }

            if (typeMap?.ConstructUsing != null)
            {
                var constructConst = Expression.Constant(typeMap.ConstructUsing, typeof(Func<object, object>));
                var invoked = Expression.Invoke(constructConst, Expression.Convert(srcParamTyped, typeof(object)));
                return Expression.Assign(destVar, Expression.Convert(invoked, destinationType));
            }

            return Expression.Assign(destVar, CreateObjectFactoryExpression(destinationType));
        }

        private Expression? CreateMemberValueExpression(TypeMap? typeMap, Type sourceType, Type destinationType, ParameterExpression srcParamTyped, ParameterExpression destVar, PropertyInfo destProp)
        {
            var srcProp = sourceType.GetProperty(destProp.Name, BindingFlags.Public | BindingFlags.Instance);

            if (typeMap != null && typeMap.TypedMemberResolvers.TryGetValue(destProp.Name, out var typedResolver))
            {
                var delConst = Expression.Constant(typedResolver.resolver, typedResolver.resolver.GetType());
                var invoke = Expression.Invoke(delConst, srcParamTyped);
                return CreateCompatibleValueExpression(invoke, destProp.PropertyType);
            }

            if (typeMap != null && typeMap.MemberResolvers.TryGetValue(destProp.Name, out var resolver))
            {
                var resolverConst = Expression.Constant(resolver, typeof(Func<object, object>));
                var invoke = Expression.Invoke(resolverConst, Expression.Convert(srcParamTyped, typeof(object)));
                return CreateCompatibleValueExpression(invoke, destProp.PropertyType);
            }

            if (srcProp == null || !srcProp.CanRead)
            {
                return null;
            }

            if (MappingHelpers.IsEnumerable(srcProp.PropertyType) && MappingHelpers.IsEnumerable(destProp.PropertyType))
            {
                return CreateCollectionValueExpression(srcParamTyped, srcProp, destProp);
            }

            if (!MappingHelpers.IsEnumerable(srcProp.PropertyType) && !MappingHelpers.IsEnumerable(destProp.PropertyType))
            {
                if (srcProp.PropertyType == destProp.PropertyType)
                {
                    return Expression.Property(srcParamTyped, srcProp);
                }

                if (MappingHelpers.CanConvertSimpleType(srcProp.PropertyType, destProp.PropertyType))
                {
                    return CreateCompatibleValueExpression(Expression.Property(srcParamTyped, srcProp), destProp.PropertyType);
                }

                _prepareMap(srcProp.PropertyType, destProp.PropertyType);

                var srcAccess = Expression.Property(srcParamTyped, srcProp);

                if (_typedCache.TryGet(srcProp.PropertyType, destProp.PropertyType, out var nestedTypedObj) && nestedTypedObj != null)
                {
                    var nestedConst = Expression.Constant(nestedTypedObj, nestedTypedObj.GetType());
                    var invoke = Expression.Invoke(nestedConst, srcAccess);
                    var mappedExpr = Expression.Convert(invoke, destProp.PropertyType);

                    // Guard against null source property to prevent NRE in nested mapper
                    if (MappingHelpers.CanBeNull(srcProp.PropertyType))
                    {
                        return Expression.Condition(
                            Expression.Equal(srcAccess, Expression.Default(srcProp.PropertyType)),
                            Expression.Default(destProp.PropertyType),
                            mappedExpr);
                    }

                    return mappedExpr;
                }

                var nestedMapper = _getMapFunc(srcProp.PropertyType, destProp.PropertyType);
                var nestedMapperConst = Expression.Constant(nestedMapper, typeof(Func<object, object>));
                var nestedInvoke = Expression.Invoke(nestedMapperConst, Expression.Convert(srcAccess, typeof(object)));
                var nestedMappedExpr = Expression.Convert(nestedInvoke, destProp.PropertyType);

                // Guard against null source property to prevent NRE in nested mapper
                if (MappingHelpers.CanBeNull(srcProp.PropertyType))
                {
                    return Expression.Condition(
                        Expression.Equal(srcAccess, Expression.Default(srcProp.PropertyType)),
                        Expression.Default(destProp.PropertyType),
                        nestedMappedExpr);
                }

                return nestedMappedExpr;
            }

            return null;
        }

        private Expression CreateCollectionValueExpression(ParameterExpression srcParamTyped, PropertyInfo srcProp, PropertyInfo destProp)
        {
            var srcElem = MappingHelpers.GetEnumerableElementType(srcProp.PropertyType) ?? typeof(object);
            var dstElem = MappingHelpers.GetEnumerableElementType(destProp.PropertyType) ?? typeof(object);

            if (srcElem == dstElem)
            {
                return Expression.Property(srcParamTyped, srcProp);
            }

            if (_typedCache.TryGet(srcElem, dstElem, out var typedElem))
            {
                var srcAccess = Expression.Property(srcParamTyped, srcProp);
                var enumerableType = typeof(IEnumerable<>).MakeGenericType(srcElem);
                var srcConverted = Expression.Convert(srcAccess, enumerableType);
                var typedDelegateType = typeof(Func<,>).MakeGenericType(srcElem, dstElem);
                var typedConst = Expression.Constant(typedElem, typedDelegateType);

                Expression collectionExpr;
                if (destProp.PropertyType.IsArray)
                {
                    var mapToArrayMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "MapToArrayGeneric", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(srcElem, dstElem);
                    collectionExpr = Expression.Call(mapToArrayMethod, srcConverted, typedConst);
                            return CreateCompatibleValueExpression(collectionExpr, destProp.PropertyType);
                }

                var listType = typeof(List<>).MakeGenericType(dstElem);
                var listCtor = listType.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(dstElem) });
                if (destProp.PropertyType.IsAssignableFrom(listType) && listCtor != null)
                {
                    var mapToListMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "MapToListGeneric", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(srcElem, dstElem);
                    collectionExpr = Expression.Call(mapToListMethod, srcConverted, typedConst);
                    return Expression.Convert(collectionExpr, destProp.PropertyType);
                }

                var destCtor = destProp.PropertyType.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(dstElem) });
                if (destCtor != null)
                {
                    var mapGenericMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "MapCollectionGeneric", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(srcElem, dstElem);
                    collectionExpr = Expression.Call(mapGenericMethod, srcConverted, typedConst);
                    var newDest = Expression.New(destCtor, collectionExpr);
                    return Expression.Convert(newDest, destProp.PropertyType);
                }

                var fallbackGenericMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "MapCollectionGeneric", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(srcElem, dstElem);
                collectionExpr = Expression.Call(fallbackGenericMethod, srcConverted, typedConst);
                return Expression.Convert(collectionExpr, destProp.PropertyType);
            }

            var srcAccessFallback = Expression.Property(srcParamTyped, srcProp);
            var mapCollectionMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "MapCollection", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var mappedCall = Expression.Call(mapCollectionMethod, Expression.Convert(srcAccessFallback, typeof(object)), Expression.Constant(srcElem), Expression.Constant(dstElem), Expression.Constant(_getMapFunc(srcElem, dstElem)), Expression.Constant(destProp.PropertyType));
            return Expression.Convert(mappedCall, destProp.PropertyType);
        }

        private void AppendPostMapActions(TypeMap? typeMap, ParameterExpression srcParamTyped, ParameterExpression destVar, List<Expression> blockExpressionsTyped)
        {
            if (typeMap != null && typeMap.PathMaps.Count > 0)
            {
                var pathMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "ApplyPathMaps", BindingFlags.Static | BindingFlags.Public);
                blockExpressionsTyped.Add(Expression.Call(pathMethod, Expression.Constant(typeMap), Expression.Convert(srcParamTyped, typeof(object)), Expression.Convert(destVar, typeof(object))));
            }

            if (typeMap != null && typeMap.AfterMapActions.Count > 0)
            {
                var afterMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "RunAfterMaps", BindingFlags.Static | BindingFlags.Public);
                blockExpressionsTyped.Add(Expression.Call(afterMethod, Expression.Constant(typeMap), Expression.Convert(srcParamTyped, typeof(object)), Expression.Convert(destVar, typeof(object))));
            }
        }

        private Expression CreateFallbackObjectBody(TypeMap? typeMap, Type sourceType, Type destinationType, ParameterExpression sourceParamObj)
        {
            var destVarObj = Expression.Variable(destinationType, "destObj");

            Expression assignDestObj;
            if (typeMap?.ConstructUsing != null)
            {
                var constructConst = Expression.Constant(typeMap.ConstructUsing, typeof(Func<object, object>));
                var invoked = Expression.Invoke(constructConst, sourceParamObj);
                assignDestObj = Expression.Assign(destVarObj, Expression.Convert(invoked, destinationType));
            }
            else
            {
                assignDestObj = Expression.Assign(destVarObj, CreateObjectFactoryExpression(destinationType));
            }

            var blockExprsObj = new List<Expression> { assignDestObj };

            if (typeMap != null && typeMap.BeforeMapActions.Count > 0)
            {
                var beforeMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "RunBeforeMaps", BindingFlags.Static | BindingFlags.Public);
                blockExprsObj.Add(Expression.Call(beforeMethod, Expression.Constant(typeMap), sourceParamObj, Expression.Convert(destVarObj, typeof(object))));
            }

            foreach (var destProp in destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!destProp.CanWrite) continue;
                if (typeMap != null && typeMap.IgnoredMembers.Contains(destProp.Name)) continue;

                var srcProp = sourceType.GetProperty(destProp.Name, BindingFlags.Public | BindingFlags.Instance);
                Expression? valueExprObj = null;

                if (typeMap != null && typeMap.MemberResolvers.TryGetValue(destProp.Name, out var resolver))
                {
                    var resolverConst = Expression.Constant(resolver, typeof(Func<object, object>));
                    var invoke = Expression.Invoke(resolverConst, sourceParamObj);
                    valueExprObj = CreateCompatibleValueExpression(invoke, destProp.PropertyType);
                }
                else if (srcProp != null && srcProp.CanRead)
                {
                    if (MappingHelpers.IsEnumerable(srcProp.PropertyType) && MappingHelpers.IsEnumerable(destProp.PropertyType))
                    {
                        var srcElem = MappingHelpers.GetEnumerableElementType(srcProp.PropertyType) ?? typeof(object);
                        var dstElem = MappingHelpers.GetEnumerableElementType(destProp.PropertyType) ?? typeof(object);
                        var elemMapper = _getMapFunc(srcElem, dstElem);
                        var srcAccess = Expression.Property(Expression.Convert(sourceParamObj, sourceType), srcProp);
                        var mapCollectionMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "MapCollection", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        var mappedCall = Expression.Call(mapCollectionMethod, Expression.Convert(srcAccess, typeof(object)), Expression.Constant(srcElem), Expression.Constant(dstElem), Expression.Constant(elemMapper), Expression.Constant(destProp.PropertyType));
                        valueExprObj = Expression.Convert(mappedCall, destProp.PropertyType);
                    }
                    else if (!MappingHelpers.IsEnumerable(srcProp.PropertyType) && !MappingHelpers.IsEnumerable(destProp.PropertyType))
                    {
                        if (srcProp.PropertyType == destProp.PropertyType)
                        {
                            var srcAccess = Expression.Property(Expression.Convert(sourceParamObj, sourceType), srcProp);
                            valueExprObj = srcAccess;
                        }
                        else if (MappingHelpers.CanConvertSimpleType(srcProp.PropertyType, destProp.PropertyType))
                        {
                            var srcAccess = Expression.Property(Expression.Convert(sourceParamObj, sourceType), srcProp);
                            valueExprObj = CreateCompatibleValueExpression(srcAccess, destProp.PropertyType);
                        }
                        else
                        {
                            var nestedMapper = _getMapFunc(srcProp.PropertyType, destProp.PropertyType);
                            var srcAccess = Expression.Property(Expression.Convert(sourceParamObj, sourceType), srcProp);
                            var nestedConst = Expression.Constant(nestedMapper, typeof(Func<object, object>));
                            var invoke = Expression.Invoke(nestedConst, Expression.Convert(srcAccess, typeof(object)));
                            var nestedMapped = Expression.Convert(invoke, destProp.PropertyType);

                            if (MappingHelpers.CanBeNull(srcProp.PropertyType))
                            {
                                valueExprObj = Expression.Condition(
                                    Expression.Equal(Expression.Convert(srcAccess, typeof(object)), Expression.Constant(null)),
                                    Expression.Default(destProp.PropertyType),
                                    nestedMapped);
                            }
                            else
                            {
                                valueExprObj = nestedMapped;
                            }
                        }
                    }
                }

                if (valueExprObj == null)
                {
                    continue;
                }

                if (typeMap != null && typeMap.NullSubstitutes.TryGetValue(destProp.Name, out var nullSubstitute) && MappingHelpers.CanBeNull(destProp.PropertyType))
                {
                    valueExprObj = Expression.Condition(
                        Expression.Equal(Expression.Convert(valueExprObj, typeof(object)), Expression.Constant(null)),
                        Expression.Constant(nullSubstitute, destProp.PropertyType),
                        valueExprObj);
                }

                var bind = Expression.Assign(Expression.Property(destVarObj, destProp), valueExprObj);
                if (typeMap != null && typeMap.MemberConditionsWithDestination.TryGetValue(destProp.Name, out var destinationCondition))
                {
                    var conditionConst = Expression.Constant(destinationCondition, typeof(Func<object, object, bool>));
                    var conditionInvoke = Expression.Invoke(conditionConst, sourceParamObj, Expression.Convert(destVarObj, typeof(object)));
                    blockExprsObj.Add(Expression.IfThen(conditionInvoke, bind));
                }
                else if (typeMap != null && typeMap.MemberConditions.TryGetValue(destProp.Name, out var condition))
                {
                    var conditionConst = Expression.Constant(condition, typeof(Func<object, bool>));
                    var conditionInvoke = Expression.Invoke(conditionConst, sourceParamObj);
                    blockExprsObj.Add(Expression.IfThen(conditionInvoke, bind));
                }
                else
                {
                    blockExprsObj.Add(bind);
                }
            }

            if (typeMap != null && typeMap.PathMaps.Count > 0)
            {
                var pathMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "ApplyPathMaps", BindingFlags.Static | BindingFlags.Public);
                blockExprsObj.Add(Expression.Call(pathMethod, Expression.Constant(typeMap), sourceParamObj, Expression.Convert(destVarObj, typeof(object))));
            }

            if (typeMap != null && typeMap.AfterMapActions.Count > 0)
            {
                var afterMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "RunAfterMaps", BindingFlags.Static | BindingFlags.Public);
                blockExprsObj.Add(Expression.Call(afterMethod, Expression.Constant(typeMap), sourceParamObj, Expression.Convert(destVarObj, typeof(object))));
            }

            blockExprsObj.Add(destVarObj);
            var bodyObj = Expression.Block(new[] { destVarObj }, blockExprsObj);
            return Expression.Convert(bodyObj, typeof(object));
        }

        private static Expression CreateObjectFactoryExpression(Type type)
        {
            var createMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), nameof(MappingHelpers.CreateObjectInstance), BindingFlags.Static | BindingFlags.Public);
            var createCall = Expression.Call(createMethod, Expression.Constant(type));
            return Expression.Convert(createCall, type);
        }

        private static Expression CreateCompatibleValueExpression(Expression sourceExpression, Type destinationType)
        {
            if (sourceExpression.Type == destinationType)
            {
                return sourceExpression;
            }

            if (MappingHelpers.CanConvertSimpleType(sourceExpression.Type, destinationType))
            {
                var convertMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), nameof(MappingHelpers.ConvertSimpleValue), BindingFlags.Static | BindingFlags.Public);
                var converted = Expression.Call(convertMethod, Expression.Convert(sourceExpression, typeof(object)), Expression.Constant(destinationType));
                return Expression.Convert(converted, destinationType);
            }

            return Expression.Convert(sourceExpression, destinationType);
        }
    }
}
