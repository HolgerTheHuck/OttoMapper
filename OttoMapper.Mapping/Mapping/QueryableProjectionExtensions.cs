using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace OttoMapper.Mapping
{
    /// <summary>
    /// <see cref="IQueryable"/> projection extensions — the OttoMapper counterpart to AutoMapper's
    /// <c>ProjectTo</c>. Projects a query server-side by translating the configured map into an
    /// <see cref="Expression{TDelegate}"/> that LINQ providers (e.g. EF Core) can translate to SQL.
    /// </summary>
    public static class QueryableProjectionExtensions
    {
        private static readonly MethodInfo QueryableSelectMethod =
            typeof(Queryable).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == nameof(Queryable.Select) && m.GetGenericArguments().Length == 2
                            && m.GetParameters().Length == 2);

        /// <summary>
        /// Projects <paramref name="source"/> onto <typeparamref name="TDestination"/> server-side using the
        /// map registered on <paramref name="mapper"/>. Only the columns referenced by the destination type
        /// are selected; the configured map's convention members and <c>MapFrom</c> expressions are inlined
        /// into the projection so the database does the work. Throws <see cref="ProjectionException"/> when the
        /// map uses customizations that cannot be translated to SQL.
        /// </summary>
        /// <typeparam name="TDestination">The destination type to project onto.</typeparam>
        /// <param name="source">The source query.</param>
        /// <param name="mapper">The mapper providing the projection expression.</param>
        /// <returns>A query yielding projected <typeparamref name="TDestination"/> instances.</returns>
        /// <remarks>
        /// This overload infers the source element type at runtime from the query, mirroring AutoMapper's
        /// <c>ProjectTo&lt;T&gt;</c>. Specify both type arguments explicitly via
        /// <see cref="ProjectTo{TSource, TDestination}"/> for compile-time type safety.
        /// </remarks>
        public static IQueryable<TDestination> ProjectTo<TDestination>(
            this IQueryable source, IMapper mapper)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));

            var sourceType = source.ElementType;
            var lambda = mapper.BuildProjection(sourceType, typeof(TDestination));
            var selectCall = Expression.Call(
                QueryableSelectMethod.MakeGenericMethod(sourceType, typeof(TDestination)),
                source.Expression,
                Expression.Quote(lambda));
            return source.Provider.CreateQuery<TDestination>(selectCall);
        }

        /// <summary>
        /// Projects <paramref name="source"/> onto <typeparamref name="TDestination"/> server-side using the
        /// map registered on <paramref name="mapper"/>. Compile-time type-safe variant: both source and
        /// destination types are specified explicitly. Throws <see cref="ProjectionException"/> when the map
        /// uses customizations that cannot be translated to SQL.
        /// </summary>
        /// <typeparam name="TSource">The source element type of the query.</typeparam>
        /// <typeparam name="TDestination">The destination type to project onto.</typeparam>
        /// <param name="source">The source query.</param>
        /// <param name="mapper">The mapper providing the projection expression.</param>
        /// <returns>A query yielding projected <typeparamref name="TDestination"/> instances.</returns>
        public static IQueryable<TDestination> ProjectTo<TSource, TDestination>(
            this IQueryable<TSource> source, IMapper mapper)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (mapper == null) throw new ArgumentNullException(nameof(mapper));

            return source.Select(mapper.BuildProjection<TSource, TDestination>());
        }
    }
}