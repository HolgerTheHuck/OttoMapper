using System;
using System.ComponentModel;

namespace OttoMapper.Mapping.Generated
{
    /// <summary>
    /// Overrides the convention name matching for a single destination property, directing the
    /// source generator to read the value from the named source property instead.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public sealed class MapSourceAttribute : Attribute
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MapSourceAttribute"/> class.
        /// </summary>
        /// <param name="sourceProperty">The name of the source property to read from.</param>
        public MapSourceAttribute(string sourceProperty)
        {
            SourceProperty = sourceProperty ?? throw new ArgumentNullException(nameof(sourceProperty));
        }

        /// <summary>
        /// Gets the name of the source property to read from.
        /// </summary>
        public string SourceProperty { get; }
    }
}