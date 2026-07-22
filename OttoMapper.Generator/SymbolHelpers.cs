using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace OttoMapper.Generator
{
    /// <summary>
    /// Compile-time symbol utilities mirroring the runtime <c>MappingHelpers</c> decisions so that
    /// generated maps behave like the runtime expression-tree path for convention members.
    /// </summary>
    internal static class SymbolHelpers
    {
        private static readonly HashSet<SpecialType> NumericSpecialTypes = new HashSet<SpecialType>
        {
            SpecialType.System_Byte, SpecialType.System_SByte,
            SpecialType.System_Int16, SpecialType.System_UInt16,
            SpecialType.System_Int32, SpecialType.System_UInt32,
            SpecialType.System_Int64, SpecialType.System_UInt64,
            SpecialType.System_Decimal, SpecialType.System_Double, SpecialType.System_Single,
        };

        private static readonly HashSet<SpecialType> SimpleSpecialTypes = new HashSet<SpecialType>
        {
            SpecialType.System_String,
            SpecialType.System_Decimal,
            SpecialType.System_DateTime,
            SpecialType.System_Boolean,
            SpecialType.System_Char,
            SpecialType.System_Byte, SpecialType.System_SByte,
            SpecialType.System_Int16, SpecialType.System_UInt16,
            SpecialType.System_Int32, SpecialType.System_UInt32,
            SpecialType.System_Int64, SpecialType.System_UInt64,
            SpecialType.System_Single, SpecialType.System_Double,
            SpecialType.System_IntPtr, SpecialType.System_UIntPtr,
        };

        // Simple types without dedicated SpecialType members.
        private static readonly HashSet<string> SimpleMetadataNames = new HashSet<string>
        {
            "System.DateTimeOffset",
            "System.TimeSpan",
            "System.Guid",
        };

        public static bool IsEnum(ITypeSymbol type)
        {
            return type.TypeKind == TypeKind.Enum;
        }

        public static bool IsNullable(ITypeSymbol type)
        {
            return type is INamedTypeSymbol named && named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        }

        public static ITypeSymbol UnwrapNullable(ITypeSymbol type)
        {
            return IsNullable(type) ? ((INamedTypeSymbol)type).TypeArguments[0] : type;
        }

        public static bool IsValueType(ITypeSymbol type) => type.IsValueType;

        public static bool CanBeNull(ITypeSymbol type) => !type.IsValueType || IsNullable(type);

        public static bool IsSimpleType(ITypeSymbol type)
        {
            var candidate = UnwrapNullable(type);
            if (candidate.TypeKind == TypeKind.Enum) return true;
            if (candidate is INamedTypeSymbol named)
            {
                if (SimpleSpecialTypes.Contains(named.OriginalDefinition.SpecialType)) return true;
                if (SimpleMetadataNames.Contains(MetadataName(named.OriginalDefinition))) return true;
            }
            return false;
        }

        private static string MetadataName(INamedTypeSymbol type)
        {
            var ns = type.ContainingNamespace;
            var nsString = ns != null && !ns.IsGlobalNamespace ? ns.ToDisplayString() : string.Empty;
            return string.IsNullOrEmpty(nsString) ? type.Name : nsString + "." + type.Name;
        }

        public static bool IsNumericType(ITypeSymbol type)
        {
            var candidate = UnwrapNullable(type);
            return candidate is INamedTypeSymbol named && NumericSpecialTypes.Contains(named.OriginalDefinition.SpecialType);
        }

        public static bool CanConvertSimpleType(ITypeSymbol sourceType, ITypeSymbol destinationType)
        {
            var src = UnwrapNullable(sourceType);
            var dst = UnwrapNullable(destinationType);

            if (SymbolEqualityComparer.Default.Equals(src, dst))
            {
                return true;
            }

            var srcEnum = src.TypeKind == TypeKind.Enum;
            var dstEnum = dst.TypeKind == TypeKind.Enum;

            if (srcEnum && dstEnum)
            {
                return true;
            }

            if (srcEnum && IsNumericType(dst))
            {
                return true;
            }

            if (dstEnum && IsNumericType(src))
            {
                return true;
            }

            return IsNumericType(src) && IsNumericType(dst);
        }

        public static bool IsEnumerable(ITypeSymbol type)
        {
            if (type.SpecialType == SpecialType.System_String) return false;
            return ImplementsIEnumerable(type);
        }

        public static bool ImplementsIEnumerable(ITypeSymbol type)
        {
            return GetEnumerableElementType(type) != null;
        }

        public static ITypeSymbol? GetEnumerableElementType(ITypeSymbol type)
        {
            if (type is IArrayTypeSymbol array) return array.ElementType;
            if (type is INamedTypeSymbol named)
            {
                if (named.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)
                {
                    return named.TypeArguments[0];
                }

                var iface = named.AllInterfaces.FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
                if (iface != null) return iface.TypeArguments[0];
            }

            return null;
        }

        /// <summary>
        /// Finds a public readable instance property on the type (and base types) by name, applying
        /// the runtime case-insensitive and underscore-ignoring matching rules.
        /// </summary>
        public static IPropertySymbol? GetPropertyCaseInsensitive(INamedTypeSymbol type, string name, bool caseInsensitive = true, bool ignoreUnderscores = true)
        {
            var allProps = GetPublicInstanceProperties(type);

            // Exact match first.
            IPropertySymbol? exact = null;
            foreach (var p in allProps)
            {
                if (p.Name == name) { exact = p; break; }
            }
            if (exact != null) return exact;

            if (caseInsensitive)
            {
                IPropertySymbol? ci = null;
                foreach (var p in allProps)
                {
                    if (p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { ci = p; break; }
                }
                if (ci != null) return ci;
            }

            if (ignoreUnderscores)
            {
                var normalized = name.Replace("_", string.Empty);
                var comparison = caseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                foreach (var p in allProps)
                {
                    if (p.Name.Replace("_", string.Empty).Equals(normalized, comparison)) return p;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns all public instance properties (declared and inherited) with a public or internal getter.
        /// </summary>
        public static IReadOnlyList<IPropertySymbol> GetPublicInstanceProperties(INamedTypeSymbol type)
        {
            var result = new List<IPropertySymbol>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var current = type;
            while (current != null && current.SpecialType != SpecialType.System_Object)
            {
                foreach (var member in current.GetMembers())
                {
                    if (member is not IPropertySymbol prop) continue;
                    if (prop.IsStatic) continue;
                    if (prop.ExplicitInterfaceImplementations.Length > 0) continue;
                    if (prop.GetMethod == null) continue;
                    if (prop.GetMethod.DeclaredAccessibility != Accessibility.Public) continue;
                    if (!seen.Add(prop.Name)) continue;
                    result.Add(prop);
                }
                current = current.BaseType;
            }

            return result;
        }

        /// <summary>
        /// Returns all public writable instance properties (declared and inherited) with a public setter.
        /// </summary>
        public static IReadOnlyList<IPropertySymbol> GetPublicWritableProperties(INamedTypeSymbol type)
        {
            var result = new List<IPropertySymbol>();
            foreach (var prop in GetPublicInstanceProperties(type))
            {
                if (prop.SetMethod == null) continue;
                if (prop.SetMethod.DeclaredAccessibility != Accessibility.Public) continue;
                if (prop.SetMethod.IsInitOnly) continue; // v1: skip init-only
                if (prop.IsIndexer) continue;
                result.Add(prop);
            }
            return result;
        }

        /// <summary>
        /// Returns true when the type has a public parameterless instance constructor.
        /// </summary>
        public static bool HasPublicParameterlessCtor(INamedTypeSymbol type)
        {
            if (type.IsAbstract || type.TypeKind == TypeKind.Interface) return false;
            if (type.TypeKind == TypeKind.Struct) return true; // structs have implicit parameterless ctor
            foreach (var ctor in type.Constructors)
            {
                if (ctor.IsStatic) continue;
                if (ctor.DeclaredAccessibility != Accessibility.Public) continue;
                if (ctor.Parameters.Length == 0) return true;
            }
            return false;
        }

        public static IPropertySymbol? FindPropertyAttribute(IPropertySymbol prop, string metadataName, Compilation compilation)
        {
            var attrSymbol = compilation.GetTypeByMetadataName(metadataName);
            if (attrSymbol == null) return null;
            foreach (var attr in prop.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrSymbol))
                {
                    return prop;
                }
            }
            return null;
        }

        public static AttributeData? GetPropertyAttribute(IPropertySymbol prop, string metadataName, Compilation compilation)
        {
            var attrSymbol = compilation.GetTypeByMetadataName(metadataName);
            if (attrSymbol == null) return null;
            foreach (var attr in prop.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrSymbol))
                {
                    return attr;
                }
            }
            return null;
        }

        public enum CollectionTarget { Direct, Array, List, Ctor }

        public static bool IsAssignableTo(Compilation compilation, ITypeSymbol source, ITypeSymbol destination)
        {
            return compilation.ClassifyCommonConversion(source, destination).IsImplicit;
        }

        /// <summary>
        /// Determines how to materialize a collection value into the destination property type.
        /// Returns <c>null</c> when the destination collection type cannot be constructed from a
        /// <c>List&lt;T&gt;</c> or array of the destination element.
        /// </summary>
        public static CollectionTarget? GetCollectionTarget(Compilation compilation, ITypeSymbol dstType, ITypeSymbol dstElem)
        {
            if (dstType is IArrayTypeSymbol arr)
            {
                return SymbolEqualityComparer.Default.Equals(arr.ElementType, dstElem) ? CollectionTarget.Array : null;
            }

            var listOpen = compilation.GetTypeByMetadataName("System.Collections.Generic.List`1");
            if (listOpen != null)
            {
                var listSymbol = listOpen.Construct(dstElem);
                if (IsAssignableTo(compilation, listSymbol, dstType))
                {
                    return CollectionTarget.List;
                }
            }

            if (dstType is INamedTypeSymbol named)
            {
                var ienumOpen = compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
                if (ienumOpen != null)
                {
                    var ienumSymbol = ienumOpen.Construct(dstElem);
                    foreach (var ctor in named.Constructors)
                    {
                        if (ctor.IsStatic) continue;
                        if (ctor.DeclaredAccessibility != Accessibility.Public) continue;
                        if (ctor.Parameters.Length != 1) continue;
                        if (IsAssignableTo(compilation, ienumSymbol, ctor.Parameters[0].Type))
                        {
                            return CollectionTarget.Ctor;
                        }
                    }
                }
            }

            return null;
        }
    }
}