using System;
using System.ComponentModel;

namespace OttoMapper.Mapping.Generated
{
    /// <summary>
    /// Excludes the decorated destination property from compile-time convention mapping.
    /// The source generator leaves the property at its default value.
    /// </summary>
    [AttributeUsage(AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public sealed class IgnoreMapAttribute : Attribute
    {
    }
}