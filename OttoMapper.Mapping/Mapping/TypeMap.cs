using System;
using System.Collections.Generic;

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
        /// Gets or sets an optional typed converter for the entire map.
        /// </summary>
        public Delegate? TypedCustomConverter { get; set; }

        /// <summary>
        /// Gets or sets an optional typed constructor for destination instances.
        /// </summary>
        public Delegate? TypedConstructUsing { get; set; }
    }
}
