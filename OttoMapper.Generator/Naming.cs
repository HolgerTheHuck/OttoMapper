using System;
using System.Text;
using Microsoft.CodeAnalysis;

namespace OttoMapper.Generator
{
    /// <summary>
    /// Builds unique, identifier-safe names for generated map classes and registry keys.
    /// </summary>
    internal static class Naming
    {
        /// <summary>
        /// Returns a stable, identifier-safe string uniquely identifying the given type, encoding
        /// namespace, containing types, simple name, generic arity and generic type arguments.
        /// </summary>
        public static string TypeKey(INamedTypeSymbol type)
        {
            var sb = new StringBuilder();
            AppendContaining(sb, type);
            sb.Append(type.Name);
            if (type.Arity > 0)
            {
                sb.Append('_').Append(type.Arity);
                sb.Append("__of__");
                for (var i = 0; i < type.TypeArguments.Length; i++)
                {
                    if (i > 0) sb.Append("_and_");
                    sb.Append(TypeKey((INamedTypeSymbol)type.TypeArguments[i]));
                }
            }

            return Sanitize(sb.ToString());
        }

        /// <summary>
        /// Returns the generated map class name for a (source, destination) pair.
        /// </summary>
        public static string MapClassName(INamedTypeSymbol source, INamedTypeSymbol destination)
        {
            return TypeKey(source) + "_To_" + TypeKey(destination);
        }

        /// <summary>
        /// Returns the fully-qualified display name (with global:: prefix) usable in emitted code.
        /// </summary>
        public static string FullyQualified(ITypeSymbol type)
        {
            return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        private static void AppendContaining(StringBuilder sb, INamedTypeSymbol type)
        {
            if (type.ContainingType != null)
            {
                AppendContaining(sb, type.ContainingType);
                sb.Append(type.ContainingType.Name).Append('_');
            }
            else if (type.ContainingNamespace is { IsGlobalNamespace: false } ns)
            {
                AppendNamespace(sb, ns);
            }
        }

        private static void AppendNamespace(StringBuilder sb, INamespaceSymbol ns)
        {
            if (ns.IsGlobalNamespace) return;
            if (ns.ContainingNamespace is { IsGlobalNamespace: false } outer)
            {
                AppendNamespace(sb, outer);
                sb.Append(ns.Name).Append('_');
            }
            else
            {
                sb.Append(ns.Name).Append('_');
            }
        }

        private static string Sanitize(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                sb.Append(char.IsLetterOrDigit(c) || c == '_' ? c : '_');
            }
            return sb.ToString();
        }
    }
}