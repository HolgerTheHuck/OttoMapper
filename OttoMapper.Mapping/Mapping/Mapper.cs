using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using System.Threading;

namespace OttoMapper.Mapping
{
    /// <summary>
    /// Default runtime mapper implementation for OttoMapper.
    /// </summary>
    public class Mapper : IMapper
    {
        private readonly MapperConfiguration _config;
        private readonly ConcurrentDictionary<(Type, Type), Lazy<Func<object, object>>> _mapFuncs = new ConcurrentDictionary<(Type, Type), Lazy<Func<object, object>>>();
        private readonly TypedMapCache _typedCache = new TypedMapCache();
        private readonly ThreadLocal<HashSet<(Type, Type)>> _mapsBeingBuilt = new ThreadLocal<HashSet<(Type, Type)>>(() => new HashSet<(Type, Type)>());
        private readonly CollectionMapFactory _collectionMapFactory;
        private readonly ObjectMapExpressionBuilder _objectMapExpressionBuilder;
        private readonly ConcurrentDictionary<(Type, Type), LambdaExpression> _projections = new ConcurrentDictionary<(Type, Type), LambdaExpression>();
        private readonly ThreadLocal<HashSet<(Type, Type)>> _projectionsBeingBuilt = new ThreadLocal<HashSet<(Type, Type)>>(() => new HashSet<(Type, Type)>());
        private readonly ProjectionBuilder _projectionBuilder;

        /// <summary>
        /// Initializes a new instance of the <see cref="Mapper"/> class.
        /// </summary>
        /// <param name="config">The mapper configuration to use.</param>
        public Mapper(MapperConfiguration config)
        {
            _config = config;
            _collectionMapFactory = new CollectionMapFactory(GetMapFunc, _typedCache);
            _objectMapExpressionBuilder = new ObjectMapExpressionBuilder(_typedCache, GetMapFunc, PrepareMap, RegisterTypedDelegate);
            _projectionBuilder = new ProjectionBuilder(_config, BuildProjectionCore);
        }

        /// <summary>
        /// Maps a source instance to the specified destination type.
        /// </summary>
        [return: MaybeNull]
        public TDestination Map<TSource, TDestination>(TSource source)
        {
            if (source == null) return default;

            if (_typedCache.TryGet<TSource, TDestination>(out var typed))
            {
                return typed(source);
            }

            var generated = TryGetGeneratedMap<TSource, TDestination>();
            if (generated != null)
            {
                // Seed both caches so subsequent typed, object-typed and nested runtime maps reuse the
                // generated delegate instead of building an expression tree.
                _typedCache.Set(generated);
                var generatedKey = (typeof(TSource), typeof(TDestination));
                _mapFuncs.GetOrAdd(generatedKey, _ => new Lazy<Func<object, object>>(() => o => (object)generated!((TSource)o!)!));
                return generated(source);
            }

            var func = GetMapFunc(typeof(TSource), typeof(TDestination));
            return (TDestination)func(source);
        }

        /// <summary>
        /// Maps a source instance into the specified destination instance.
        /// </summary>
        [return: MaybeNull]
        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
        {
            if (source == null)
            {
                return destination;
            }

            if (destination == null)
            {
                return Map<TSource, TDestination>(source);
            }

            if (source is IEnumerable sourceEnumerable && !(source is string) && destination is IList destinationList)
            {
                MapCollectionIntoExistingDestination(sourceEnumerable, destinationList);
                return destination;
            }

            var mapped = Map<TSource, TDestination>(source);
            if (mapped == null)
            {
                return destination;
            }

            CopyToExistingDestination(mapped, destination);
            return destination;
        }

        /// <summary>
        /// Maps a source instance into an existing destination instance known only at runtime.
        /// </summary>
        [return: MaybeNull]
        public object Map<TSource>(TSource source, object destination)
        {
            if (source == null)
            {
                return destination;
            }

            if (destination == null)
            {
                return null;
            }

            if (source is IEnumerable sourceEnumerable && !(source is string) && destination is IList destinationList)
            {
                return MapCollectionIntoExistingDestination(sourceEnumerable, destinationList);
            }

            var mapped = Map(source, typeof(TSource), destination.GetType());
            if (mapped == null)
            {
                return destination;
            }

            CopyToExistingDestination(mapped, destination);
            return destination;
        }

        /// <summary>
        /// Maps an object instance into the specified destination instance.
        /// </summary>
        [return: MaybeNull]
        public object Map(object source, object destination)
        {
            if (source == null)
            {
                return destination;
            }

            if (destination == null)
            {
                return null;
            }

            if (source is IEnumerable sourceEnumerable && !(source is string) && destination is IList destinationList)
            {
                return MapCollectionIntoExistingDestination(sourceEnumerable, destinationList);
            }

            var mapped = Map(source, source.GetType(), destination.GetType());
            if (mapped == null)
            {
                return destination;
            }

            CopyToExistingDestination(mapped, destination);
            return destination;
        }

        /// <summary>
        /// Maps an object instance to the specified destination type.
        /// </summary>
        [return: MaybeNull]
        public TDestination Map<TDestination>(object source)
        {
            if (source == null) return default;

            var func = GetMapFunc(source.GetType(), typeof(TDestination));
            return (TDestination)func(source);
        }

        /// <summary>
        /// Maps an object instance using explicit runtime source and destination types.
        /// </summary>
        [return: MaybeNull]
        public object Map(object source, Type sourceType, Type destinationType)
        {
            if (source == null) return null;
            var func = GetMapFunc(sourceType, destinationType);
            return func(source);
        }

        internal void PrepareMap(Type sourceType, Type destinationType)
        {
            // ensure compiled map exists
            GetMapFunc(sourceType, destinationType);
        }

        /// <summary>
        /// Builds an <see cref="IQueryable"/>-translatable projection expression that maps
        /// <typeparamref name="TSource"/> onto <typeparamref name="TDestination"/> server-side. See
        /// <see cref="QueryableProjectionExtensions.ProjectTo"/> for usage. Throws <see cref="ProjectionException"/>
        /// when the configured map uses customizations that cannot be translated to SQL.
        /// </summary>
        public Expression<Func<TSource, TDestination>> BuildProjection<TSource, TDestination>()
        {
            var lambda = BuildProjectionCore(typeof(TSource), typeof(TDestination));
            if (lambda is Expression<Func<TSource, TDestination>> typed)
            {
                return typed;
            }

            // The core builder produces a LambdaExpression of the right Func<TSource,TDestination> type but the
            // CLR may not recognize the exact generic type instance; rebuild as the typed expression.
            return Expression.Lambda<Func<TSource, TDestination>>(lambda.Body, lambda.Parameters);
        }

        /// <summary>
        /// Non-generic projection builder for runtime-known types. See
        /// <see cref="QueryableProjectionExtensions.ProjectTo{TSource, TDestination}"/>.
        /// </summary>
        public LambdaExpression BuildProjection(Type sourceType, Type destinationType)
            => BuildProjectionCore(sourceType, destinationType);

        /// <summary>
        /// Core (non-generic) projection builder with caching and cycle detection. Used by
        /// <see cref="BuildProjection{TSource, TDestination}"/> and recursively by <see cref="ProjectionBuilder"/>
        /// for nested/collection projections.
        /// </summary>
        internal LambdaExpression BuildProjectionCore(Type sourceType, Type destinationType)
        {
            var key = (sourceType, destinationType);
            if (_projections.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var beingBuilt = _projectionsBeingBuilt.Value!;
            if (beingBuilt.Contains(key))
            {
                throw new ProjectionException(
                    $"Circular projection detected for '{sourceType.FullName}' -> '{destinationType.FullName}'. Break the cycle (e.g. ignore the self-referencing member) to make the map projectable.");
            }

            beingBuilt.Add(key);
            try
            {
                var lambda = _projectionBuilder.Build(sourceType, destinationType);
                _projections[key] = lambda;
                return lambda;
            }
            finally
            {
                beingBuilt.Remove(key);
            }
        }

        // ---- Source-generated convention map precedence ----
        // Generated maps are only eligible when the generator package is referenced (registry non-empty),
        // the kill-switch is on, the global name-matching flags match the generator's compile-time defaults,
        // and the runtime TypeMap (if any) carries no customizations. Without the generator package the
        // registry is always empty, so every branch below is a no-op and behavior is identical to before.
        private bool UseGeneratedMapFor(Type sourceType, Type destinationType)
        {
            if (!_config.UseGeneratedMaps)
            {
                return false;
            }

            if (!_config.CaseInsensitiveMapping || !_config.IgnoreUnderscoresInPropertyNames)
            {
                return false;
            }

            var typeMap = _config.GetTypeMap(sourceType, destinationType);
            if (typeMap != null && typeMap.HasCustomizations)
            {
                return false;
            }

            if (typeMap != null && (!typeMap.CaseInsensitiveMapping || !typeMap.IgnoreUnderscoresInPropertyNames))
            {
                return false;
            }

            return true;
        }

        private Func<TSource, TDestination>? TryGetGeneratedMap<TSource, TDestination>()
        {
            if (!UseGeneratedMapFor(typeof(TSource), typeof(TDestination)))
            {
                return null;
            }

            return Generated.GeneratedMapRegistry.TryGet<TSource, TDestination>(out var generated) ? generated : null;
        }

        private Func<object, object> GetMapFunc(Type sourceType, Type destinationType)
        {
            var key = (sourceType, destinationType);
            var mapsBeingBuilt = _mapsBeingBuilt.Value!;
            if (mapsBeingBuilt.Contains(key))
            {
                return CreateDeferredMapFunc(sourceType, destinationType);
            }

            // Belt-and-suspenders fast path for object-typed calls: use the generated wrapper when
            // eligible, before falling back to the lazy runtime compilation.
            if (UseGeneratedMapFor(sourceType, destinationType)
                && Generated.GeneratedMapRegistry.TryGetObject(sourceType, destinationType, out var generatedObject)
                && generatedObject != null)
            {
                return generatedObject;
            }

            return GetOrCreateMapFunc(sourceType, destinationType).Value;
        }

        private Func<object, object> CreateMapFunc(Type sourceType, Type destinationType)
        {
            var key = (sourceType, destinationType);
            var mapsBeingBuilt = _mapsBeingBuilt.Value!;
            mapsBeingBuilt.Add(key);
            try
            {
                var typeMap = _config.GetTypeMap(sourceType, destinationType);
                var isCollectionMap = MappingHelpers.IsEnumerable(sourceType) && MappingHelpers.IsEnumerable(destinationType);

                if (_config.RequireExplicitMaps && typeMap == null && !isCollectionMap)
                {
                    throw new InvalidOperationException($"Missing map configuration for '{sourceType.FullName}' -> '{destinationType.FullName}'.");
                }

                // Respect typed custom converter if present
                if (typeMap?.TypedCustomConverter != null)
                {
                    TryRegisterTypedFromDelegate(sourceType, destinationType, typeMap.TypedCustomConverter);
                    try
                    {
                        var srcParamObj = Expression.Parameter(typeof(object), "srcObj");
                        var typedConvType = typeMap.TypedCustomConverter.GetType();
                        var typedConst = Expression.Constant(typeMap.TypedCustomConverter, typedConvType);
                        var helper = ReflectionHelpers.GetRequiredMethod(typeof(MappingHelpers), "InvokeTypedDelegate", BindingFlags.Static | BindingFlags.Public).MakeGenericMethod(sourceType, destinationType);
                        var call = Expression.Call(helper, Expression.Convert(typedConst, typeof(Func<,>).MakeGenericType(sourceType, destinationType)), srcParamObj);
                        var lambda = Expression.Lambda<Func<object, object>>(Expression.Convert(call, typeof(object)), srcParamObj);
                        return lambda.Compile();
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException($"Failed to compile typed custom converter for '{sourceType.FullName}' -> '{destinationType.FullName}'.", ex);
                    }
                }

                // Respect CustomConverter: entirely custom mapping (untyped)
                if (typeMap?.CustomConverter != null)
                {
                    TryRegisterTypedFromObjectConverter(sourceType, destinationType, typeMap.CustomConverter);
                    return typeMap.CustomConverter;
                }

                // collection mapping
                if (MappingHelpers.IsEnumerable(sourceType) && MappingHelpers.IsEnumerable(destinationType))
                {
                    return _collectionMapFactory.CreateCollectionMap(sourceType, destinationType, TryRegisterTypedForCollection);
                }

                // Let ObjectMapExpressionBuilder generate the mapping expression
                return _objectMapExpressionBuilder.CreateObjectMap(sourceType, destinationType, typeMap);
            }
            finally
            {
                mapsBeingBuilt.Remove(key);
            }
        }

        private Lazy<Func<object, object>> GetOrCreateMapFunc(Type sourceType, Type destinationType)
        {
            return _mapFuncs.GetOrAdd((sourceType, destinationType), _ => new Lazy<Func<object, object>>(() => CreateMapFunc(sourceType, destinationType)));
        }

        private Func<object, object> CreateDeferredMapFunc(Type sourceType, Type destinationType)
        {
            return source => source == null ? null! : GetOrCreateMapFunc(sourceType, destinationType).Value(source);
        }

        private void RegisterTypedDelegate(Type sourceType, Type destinationType, Delegate typedDel)
        {
            try
            {
                var setMethod = ReflectionHelpers.GetRequiredMethod(typeof(TypedMapCache), "Set", BindingFlags.Public | BindingFlags.Instance).MakeGenericMethod(sourceType, destinationType);
                setMethod.Invoke(_typedCache, new object[] { typedDel });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to cache typed map delegate for '{sourceType.FullName}' -> '{destinationType.FullName}'.", ex);
            }
        }

        private void TryRegisterTypedFromDelegate(Type sourceType, Type destinationType, Delegate del)
        {
            try
            {
                var setMethod = ReflectionHelpers.GetRequiredMethod(typeof(TypedMapCache), "Set", BindingFlags.Public | BindingFlags.Instance).MakeGenericMethod(sourceType, destinationType);
                setMethod.Invoke(_typedCache, new object[] { del });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to cache typed delegate for '{sourceType.FullName}' -> '{destinationType.FullName}'.", ex);
            }
        }

        private void TryRegisterTypedFromObjectConverter(Type sourceType, Type destinationType, Func<object, object> converter)
        {
            try
            {
                var funcType = typeof(Func<,>).MakeGenericType(sourceType, destinationType);
                var srcParam = Expression.Parameter(sourceType, "s");
                var convConst = Expression.Constant(converter, typeof(Func<object, object>));
                var call = Expression.Invoke(convConst, Expression.Convert(srcParam, typeof(object)));
                var body = Expression.Convert(call, destinationType);
                var lambda = Expression.Lambda(funcType, body, srcParam);
                var del = lambda.Compile();
                RegisterTypedDelegate(sourceType, destinationType, del);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to compile typed converter for '{sourceType.FullName}' -> '{destinationType.FullName}'.", ex);
            }
        }

        private void TryRegisterTypedForCollection(Type sourceType, Type destinationType, Type srcElem, Type dstElem, Func<object, object> elemMapper, Func<object, object> wrapper)
        {
            try
            {
                var method = ReflectionHelpers.GetRequiredMethod(typeof(Mapper), nameof(WrapTypedCollection), BindingFlags.Instance | BindingFlags.NonPublic).MakeGenericMethod(sourceType, destinationType, srcElem, dstElem);
                method.Invoke(this, new object[] { elemMapper, wrapper });
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to register typed collection wrapper for '{sourceType.FullName}' -> '{destinationType.FullName}'.", ex);
            }
        }

        private void WrapTypedCollection<TSource, TDestination, TSrcElem, TDstElem>(Func<object, object> elemMapper, Func<object, object> wrapper)
        {
            Func<TSource, TDestination> typed = src => (TDestination)wrapper(src!);
            _typedCache.Set(typed);
        }

        private object MapCollectionIntoExistingDestination(IEnumerable sourceEnumerable, IList destinationList)
        {
            destinationList.Clear();

            var destinationType = destinationList.GetType();
            var destinationElementType = MappingHelpers.GetEnumerableElementType(destinationType) ?? typeof(object);

            foreach (var item in sourceEnumerable)
            {
                if (item == null)
                {
                    destinationList.Add(null);
                    continue;
                }

                var mappedItem = Map(item, item.GetType(), destinationElementType);
                destinationList.Add(mappedItem);
            }

            return destinationList;
        }

        private static void CopyToExistingDestination<TDestination>(TDestination sourceValue, TDestination destination)
        {
            var destinationType = typeof(TDestination);
            foreach (var property in destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead || !property.CanWrite)
                {
                    continue;
                }

                var value = property.GetValue(sourceValue);
                property.SetValue(destination, value);
            }
        }

        private static void CopyToExistingDestination(object sourceValue, object destination)
        {
            var destinationType = destination.GetType();
            foreach (var property in destinationType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead || !property.CanWrite)
                {
                    continue;
                }

                var value = property.GetValue(sourceValue);
                property.SetValue(destination, value);
            }
        }
    }
}