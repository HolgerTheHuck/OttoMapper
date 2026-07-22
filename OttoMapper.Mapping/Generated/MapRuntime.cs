using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace OttoMapper.Mapping.Generated
{
    /// <summary>
    /// Typed runtime helpers invoked by source-generated maps. Kept public because generated code
    /// is compiled into the consumer assembly and therefore cannot reach the internal mapping helpers.
    /// </summary>
    /// <remarks>
    /// This type is part of the source-generator integration surface and is not intended for direct use.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public static class MapRuntime
    {
        /// <summary>
        /// Converts a source value to the destination type using the same simple-conversion rules
        /// as the runtime expression-tree path (numeric, enum, string). AOT-soft for enum conversions
        /// (boxes the value internally); numeric conversions are AOT-clean.
        /// </summary>
        /// <typeparam name="TSource">The source member type.</typeparam>
        /// <typeparam name="TDestination">The destination member type.</typeparam>
        /// <param name="value">The source value.</param>
        /// <returns>The converted destination value.</returns>
        public static TDestination Convert<TSource, TDestination>(TSource value)
        {
            var destinationType = typeof(TDestination);
            var sourceType = typeof(TSource);

            // Same-type fast path (covers nullable-of-same-underlying where TSource == TDestination).
            if (sourceType == destinationType && value is TDestination already)
            {
                return already;
            }

            var result = MappingHelpers.ConvertSimpleValue(value, destinationType);
            return (TDestination)result!;
        }

        /// <summary>
        /// Materializes a source collection into a <see cref="List{T}"/> of destination elements,
        /// mapping each element with the supplied mapper.
        /// </summary>
        public static List<TDestination> MapToList<TSource, TDestination>(IEnumerable<TSource>? source, Func<TSource, TDestination> mapper)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (source == null) return new List<TDestination>();

            if (source is ICollection<TSource> coll)
            {
                var list = new List<TDestination>(coll.Count);
                foreach (var item in coll)
                {
                    list.Add(mapper(item));
                }
                return list;
            }

            var result = new List<TDestination>();
            foreach (var item in source)
            {
                result.Add(mapper(item));
            }
            return result;
        }

        /// <summary>
        /// Materializes a source collection into an array of destination elements, mapping each
        /// element with the supplied mapper.
        /// </summary>
        public static TDestination[] MapToArray<TSource, TDestination>(IEnumerable<TSource>? source, Func<TSource, TDestination> mapper)
        {
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));
            if (source == null) return Array.Empty<TDestination>();

            if (source is ICollection<TSource> coll)
            {
                if (coll.Count == 0) return Array.Empty<TDestination>();
                var arr = new TDestination[coll.Count];
                var i = 0;
                foreach (var item in coll)
                {
                    arr[i++] = mapper(item);
                }
                return arr;
            }

            var list = new List<TDestination>();
            foreach (var item in source)
            {
                list.Add(mapper(item));
            }
            return list.ToArray();
        }
    }
}