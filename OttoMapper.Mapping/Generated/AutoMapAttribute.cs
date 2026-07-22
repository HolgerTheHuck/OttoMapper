using System;
using System.ComponentModel;

namespace OttoMapper.Mapping.Generated
{
    /// <summary>
    /// Declares that the decorated destination type should be mapped from the specified source type
    /// at compile time by the OttoMapper source generator. When the generator package is not
    /// referenced, this attribute is an inert marker and has no runtime effect.
    /// </summary>
    /// <remarks>
    /// Place on the destination type, e.g. <c>[AutoMap(typeof(SourceDto))] class DestinationDto { ... }</c>.
    /// Generated maps use the default convention rules (case-insensitive, underscore-ignoring name
    /// matching). Customizations registered through the fluent runtime API
    /// (<c>CreateMap&lt;S,D&gt;().ForMember(...)</c>) always take precedence over a generated map.
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public sealed class AutoMapAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AutoMapAttribute"/> class.
        /// </summary>
        /// <param name="source">The source type to map from.</param>
        public AutoMapAttribute(Type source)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
        }

        /// <summary>
        /// Gets the source type that maps to the decorated destination type.
        /// </summary>
        public Type Source { get; }

        /// <summary>
        /// Gets or sets a value indicating whether the reverse map (destination to source)
        /// should also be generated. Defaults to <c>false</c>.
        /// </summary>
        public bool Reverse { get; set; }
    }
}