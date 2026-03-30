using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace OttoMapper.Mapping
{
    internal sealed class CollectionMapFactory
    {
        private readonly Func<Type, Type, Func<object, object>> _getMapFunc;
        private readonly TypedMapCache _typedCache;

        public CollectionMapFactory(Func<Type, Type, Func<object, object>> getMapFunc, TypedMapCache typedCache)
        {
            _getMapFunc = getMapFunc;
            _typedCache = typedCache;
        }

        public Func<object, object> CreateCollectionMap(Type sourceType, Type destinationType, Action<Type, Type, Type, Type, Func<object, object>, Func<object, object>> registerTypedCollection)
        {
            var srcElem = MappingHelpers.GetEnumerableElementType(sourceType) ?? typeof(object);
            var dstElem = MappingHelpers.GetEnumerableElementType(destinationType) ?? typeof(object);

            if (_typedCache.TryGet(srcElem, dstElem, out var typedElem) && typedElem != null)
            {
                return CreateTypedCollectionWrapper(destinationType, srcElem, dstElem, typedElem);
            }

            var elemMapper = _getMapFunc(srcElem, dstElem);
            Func<object, object> wrapper = src => MappingHelpers.MapCollection(src, srcElem, dstElem, elemMapper, destinationType);
            registerTypedCollection(sourceType, destinationType, srcElem, dstElem, elemMapper, wrapper);
            return wrapper;
        }

        private static Func<object, object> CreateTypedCollectionWrapper(Type destinationType, Type srcElem, Type dstElem, object typedElem)
        {
            var srcParamObj = Expression.Parameter(typeof(object), "srcObj");
            var enumerableType = typeof(IEnumerable<>).MakeGenericType(srcElem);
            var srcConverted = Expression.Convert(srcParamObj, enumerableType);
            var typedDelegateType = typeof(Func<,>).MakeGenericType(srcElem, dstElem);
            var typedConst = Expression.Constant(typedElem, typedDelegateType);

            Expression finalCollectionExpr;
            if (destinationType.IsArray)
            {
                var mapToArrayMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "MapToArrayGeneric", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(srcElem, dstElem);
                var call = Expression.Call(mapToArrayMethod, srcConverted, typedConst);
                finalCollectionExpr = Expression.Convert(call, typeof(object));
            }
            else
            {
                var listType = typeof(List<>).MakeGenericType(dstElem);
                var listCtor = listType.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(dstElem) });
                if (destinationType.IsAssignableFrom(listType) && listCtor != null)
                {
                    var mapToListMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "MapToListGeneric", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(srcElem, dstElem);
                    var call = Expression.Call(mapToListMethod, srcConverted, typedConst);
                    finalCollectionExpr = Expression.Convert(call, typeof(object));
                }
                else
                {
                    var destCtor = destinationType.GetConstructor(new[] { typeof(IEnumerable<>).MakeGenericType(dstElem) });
                    if (destCtor != null)
                    {
                        var mapGenericMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "MapCollectionGeneric", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(srcElem, dstElem);
                        var collectionExpr = Expression.Call(mapGenericMethod, srcConverted, typedConst);
                        var newDest = Expression.New(destCtor, collectionExpr);
                        finalCollectionExpr = Expression.Convert(newDest, typeof(object));
                    }
                    else
                    {
                        var mapGenericMethod = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "MapCollectionGeneric", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(srcElem, dstElem);
                        var collectionExpr = Expression.Call(mapGenericMethod, srcConverted, typedConst);
                        finalCollectionExpr = Expression.Convert(collectionExpr, typeof(object));
                    }
                }
            }

            var collWrapperLambda = Expression.Lambda<Func<object, object>>(finalCollectionExpr, srcParamObj);
            return collWrapperLambda.Compile();
        }
    }
}
