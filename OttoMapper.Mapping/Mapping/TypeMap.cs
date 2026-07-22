using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace OttoMapper.Mapping
{
    /// <summary>
    /// Stores runtime metadata for a configured source-to-destination map.
    /// </summary>
    public class TypeMap
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TypeMap"/> class.
        /// </summary>
        /// <param name="source">The source type.</param>
        /// <param name="destination">The destination type.</param>
        public TypeMap(Type source, Type destination)
        {
            SourceType = source;
            DestinationType = destination;
        }

        /// <summary>
        /// Gets the source type.
        /// </summary>
        public Type SourceType { get; }

        /// <summary>
        /// Gets the destination type.
        /// </summary>
        public Type DestinationType { get; }

        /// <summary>
        /// Gets or sets a value indicating whether property name matching is case-insensitive.
        /// </summary>
        public bool CaseInsensitiveMapping { get; set; } = true;

        /// <summary>
        /// Gets or sets a value indicating whether underscores in property names are ignored during matching.
        /// </summary>
        public bool IgnoreUnderscoresInPropertyNames { get; set; } = true;

        /// <summary>
        /// Gets object-based member resolvers keyed by destination member name.
        /// </summary>
        public Dictionary<string, Func<object, object>> MemberResolvers { get; } = new Dictionary<string, Func<object, object>>();

        /// <summary>
        /// Gets source-only member conditions keyed by destination member name.
        /// </summary>
        public Dictionary<string, Func<object, bool>> MemberConditions { get; } = new Dictionary<string, Func<object, bool>>();

        /// <summary>
        /// Gets source-and-destination member conditions keyed by destination member name.
        /// </summary>
        public Dictionary<string, Func<object, object, bool>> MemberConditionsWithDestination { get; } = new Dictionary<string, Func<object, object, bool>>();

        /// <summary>
        /// Gets null substitute values keyed by destination member name.
        /// </summary>
        public Dictionary<string, object> NullSubstitutes { get; } = new Dictionary<string, object>();

        /// <summary>
        /// Gets ignored destination member names.
        /// </summary>
        public HashSet<string> IgnoredMembers { get; } = new HashSet<string>();

        /// <summary>
        /// Gets reversible source member paths keyed by destination member name.
        /// </summary>
        public Dictionary<string, string> ReverseSourcePaths { get; } = new Dictionary<string, string>();

        /// <summary>
        /// Gets configured path mappings.
        /// </summary>
        public List<PathMap> PathMaps { get; } = new List<PathMap>();

        /// <summary>
        /// Gets actions that run before member assignments.
        /// </summary>
        public List<Action<object, object>> BeforeMapActions { get; } = new List<Action<object, object>>();

        /// <summary>
        /// Gets actions that run after member assignments.
        /// </summary>
        public List<Action<object, object>> AfterMapActions { get; } = new List<Action<object, object>>();

        /// <summary>
        /// Gets or sets an optional object-based converter for the entire map.
        /// </summary>
        public Func<object, object>? CustomConverter { get; set; }

        /// <summary>
        /// Gets or sets an optional object-based constructor for destination instances.
        /// </summary>
        public Func<object, object>? ConstructUsing { get; set; }

        /// <summary>
        /// Gets typed member resolvers keyed by destination member name.
        /// </summary>
        public Dictionary<string, (Type srcType, Type memberType, Delegate resolver)> TypedMemberResolvers { get; } = new Dictionary<string, (Type, Type, Delegate)>();

        /// <summary>
        /// Gets the original expression-based member resolvers (<c>MapFrom(Expression&lt;Func&lt;TSource, TMember&gt;&gt;)</c>),
        /// keyed by destination member name. Populated only for genuine expression resolvers; absent for
        /// the <c>Func</c>-based <c>ForMember</c> overload. The runtime expression-tree path ignores this
        /// collection; it is consumed only by <c>ProjectTo</c>/<c>BuildProjection</c> to inline the resolver
        /// body into an EF-translatable projection.
        /// </summary>
        public Dictionary<string, LambdaExpression> MemberResolverExpressions { get; } = new Dictionary<string, LambdaExpression>();

        /// <summary>
        /// Gets or sets an optional typed converter for the entire map.
        /// </summary>
        public Delegate? TypedCustomConverter { get; set; }

        /// <summary>
        /// Gets or sets an optional typed constructor for destination instances.
        /// </summary>
        public Delegate? TypedConstructUsing { get; set; }

        /// <summary>
        /// Gets a value indicating whether this map carries any runtime customizations (resolvers,
        /// conditions, null substitutes, ignored members, path maps, hooks, converters, or custom
        /// constructors). When <c>true</c>, the runtime expression-tree path must be used instead of a
        /// source-generated convention map, because the generator cannot reproduce these customizations.
        /// </summary>
        internal bool HasCustomizations =>
            MemberResolvers.Count > 0 ||
            MemberConditions.Count > 0 ||
            MemberConditionsWithDestination.Count > 0 ||
            NullSubstitutes.Count > 0 ||
            IgnoredMembers.Count > 0 ||
            ReverseSourcePaths.Count > 0 ||
            PathMaps.Count > 0 ||
            BeforeMapActions.Count > 0 ||
            AfterMapActions.Count > 0 ||
            CustomConverter != null ||
            ConstructUsing != null ||
            TypedCustomConverter != null ||
            TypedConstructUsing != null ||
            TypedMemberResolvers.Count > 0;
    }
}
