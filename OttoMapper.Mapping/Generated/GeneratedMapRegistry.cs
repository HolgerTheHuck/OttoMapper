using System;
using System.Collections.Concurrent;
using System.ComponentModel;

namespace OttoMapper.Mapping.Generated
{
    /// <summary>
    /// Process-global registry of source-generated convention maps keyed by (source, destination) type pairs.
    /// Populated by generated <c>[ModuleInitializer]</c> code when the OttoMapper source generator is referenced.
    /// When the generator package is not referenced this registry stays empty, so the runtime expression-tree
    /// path is used unchanged.
    /// </summary>
    /// <remarks>
    /// This type is part of the source-generator integration surface and is not intended for direct use.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public static class GeneratedMapRegistry
    {
        private static readonly ConcurrentDictionary<(Type Source, Type Destination), object> _typedFuncs = new ConcurrentDictionary<(Type, Type), object>();
        private static readonly ConcurrentDictionary<(Type Source, Type Destination), Func<object, object>> _objectFuncs = new ConcurrentDictionary<(Type, Type), Func<object, object>>();

        /// <summary>
        /// Registers a typed generated map delegate for the specified type pair. Idempotent.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDestination">The destination type.</typeparam>
        /// <param name="map">The generated map delegate.</param>
        public static void Register<TSource, TDestination>(Func<TSource, TDestination> map)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            _typedFuncs[(typeof(TSource), typeof(TDestination))] = map;
        }

        /// <summary>
        /// Attempts to retrieve the typed generated map for the specified type pair.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDestination">The destination type.</typeparam>
        /// <param name="map">When this method returns <c>true</c>, the generated map delegate; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if a generated map was registered; otherwise <c>false</c>.</returns>
        public static bool TryGet<TSource, TDestination>(out Func<TSource, TDestination>? map)
        {
            if (_typedFuncs.TryGetValue((typeof(TSource), typeof(TDestination)), out var obj) && obj is Func<TSource, TDestination> typed)
            {
                map = typed;
                return true;
            }

            map = null;
            return false;
        }

        /// <summary>
        /// Attempts to retrieve an object-typed wrapper for the generated map of the specified type pair.
        /// The wrapper is built and cached on first request.
        /// </summary>
        /// <param name="sourceType">The source type.</param>
        /// <param name="destinationType">The destination type.</param>
        /// <param name="map">When this method returns <c>true</c>, the object-typed map wrapper; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> if a generated map was registered; otherwise <c>false</c>.</returns>
        public static bool TryGetObject(Type sourceType, Type destinationType, out Func<object, object>? map)
        {
            if (_objectFuncs.TryGetValue((sourceType, destinationType), out var objMap))
            {
                map = objMap;
                return true;
            }

            if (!_typedFuncs.TryGetValue((sourceType, destinationType), out var raw))
            {
                map = null;
                return false;
            }

            var wrapper = BuildObjectWrapper(sourceType, destinationType, raw);
            map = _objectFuncs.GetOrAdd((sourceType, destinationType), wrapper);
            return true;
        }

        /// <summary>
        /// Clears all registered generated maps. Intended for test isolation only.
        /// </summary>
        public static void Clear()
        {
            _typedFuncs.Clear();
            _objectFuncs.Clear();
        }

        private static Func<object, object> BuildObjectWrapper(Type sourceType, Type destinationType, object typedFunc)
        {
            // The typedFunc is Func<TSource, TDestination>; we wrap without reflection on the
            // generic delegates by building a small dynamic dispatch via Expression trees would
            // reintroduce a runtime-compile cost. Instead, since the generator always knows the
            // closed generic type at registration time, the generated aggregator registers the
            // object wrapper directly via RegisterObject as well. As a fallback for manually
            // registered typed delegates we box through a cast using Delegate.DynamicInvoke.
            return source =>
            {
                var del = (Delegate)typedFunc;
                return del.DynamicInvoke(source)!;
            };
        }

        /// <summary>
        /// Registers a pre-built object-typed generated map wrapper. Used by the generated aggregator
        /// to avoid any runtime reflection.
        /// </summary>
        /// <typeparam name="TSource">The source type.</typeparam>
        /// <typeparam name="TDestination">The destination type.</typeparam>
        /// <param name="typedMap">The typed generated map.</param>
        /// <param name="objectMap">The object-typed wrapper of <paramref name="typedMap"/>.</param>
        public static void RegisterObject<TSource, TDestination>(Func<TSource, TDestination> typedMap, Func<object, object> objectMap)
        {
            if (typedMap == null) throw new ArgumentNullException(nameof(typedMap));
            if (objectMap == null) throw new ArgumentNullException(nameof(objectMap));

            _typedFuncs[(typeof(TSource), typeof(TDestination))] = typedMap;
            _objectFuncs[(typeof(TSource), typeof(TDestination))] = objectMap;
        }
    }
}