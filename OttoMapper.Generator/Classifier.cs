using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;

namespace OttoMapper.Generator
{
    /// <summary>A declared [AutoMap] pair. Per-property [IgnoreMap]/[MapSource] attributes are read
    /// lazily during classification so the parse stage stays lightweight.</summary>
    internal sealed class MapDeclaration
    {
        public INamedTypeSymbol Source { get; set; } = null!;
        public INamedTypeSymbol Destination { get; set; } = null!;
        public bool Reverse { get; set; }
        public Location Location { get; set; } = null!;
    }

    internal enum MemberKind
    {
        None,           // emit nothing (ignored or no source member)
        DirectAssign,
        Convert,
        CollectionSame,
        CollectionConvert,
        CollectionNested,
        Nested,
    }

    internal sealed class MemberPlan
    {
        public IPropertySymbol DestProp { get; set; } = null!;
        public IPropertySymbol? SourceProp { get; set; }
        public MemberKind Kind { get; set; }
        public INamedTypeSymbol? NestedSource { get; set; }
        public INamedTypeSymbol? NestedDest { get; set; }
        public ITypeSymbol? SrcElem { get; set; }
        public ITypeSymbol? DstElem { get; set; }
        public SymbolHelpers.CollectionTarget CollectionKind { get; set; }
    }

    /// <summary>A pair to consider generating, either forward or (when Reverse) reverse.</summary>
    internal sealed class MapPair
    {
        public INamedTypeSymbol Source { get; set; } = null!;
        public INamedTypeSymbol Destination { get; set; } = null!;
        public MapDeclaration Declaration { get; set; } = null!;
        public bool IsReverse { get; set; }
    }

    internal sealed class GeneratedPair
    {
        public MapPair Pair { get; set; } = null!;
        public List<MemberPlan> Members { get; set; } = new();
    }

    internal sealed class SkippedPair
    {
        public MapPair Pair { get; set; } = null!;
        public string Reason { get; set; } = "";
    }

    internal sealed class ClassificationResult
    {
        public List<GeneratedPair> Generated { get; set; } = new();
        public List<SkippedPair> Skipped { get; set; } = new();
    }

    internal static class Classifier
    {
        private const string IgnoreAttrMetadataName = "OttoMapper.Mapping.Generated.IgnoreMapAttribute";
        private const string MapSourceAttrMetadataName = "OttoMapper.Mapping.Generated.MapSourceAttribute";

        public static ClassificationResult Classify(Compilation compilation, IReadOnlyList<MapDeclaration> declarations)
        {
            // Build candidate pairs (forward + reverse).
            var pairs = new Dictionary<(INamedTypeSymbol, INamedTypeSymbol), MapPair>(SymbolPairComparer.Instance);
            foreach (var decl in declarations)
            {
                pairs[(decl.Source, decl.Destination)] = new MapPair { Source = decl.Source, Destination = decl.Destination, Declaration = decl, IsReverse = false };
                if (decl.Reverse)
                {
                    pairs[(decl.Destination, decl.Source)] = new MapPair { Source = decl.Destination, Destination = decl.Source, Declaration = decl, IsReverse = true };
                }
            }

            // Fixpoint: a pair is generatable iff eligible and all nested-dep candidate pairs are generatable.
            var generatable = pairs.ToDictionary(p => p.Key, _ => true);
            bool changed;
            do
            {
                changed = false;
                foreach (var kv in pairs)
                {
                    if (!generatable[kv.Key]) continue;
                    if (!IsEligible(compilation, kv.Value, pairs, generatable, out _))
                    {
                        generatable[kv.Key] = false;
                        changed = true;
                    }
                }
            } while (changed);

            var result = new ClassificationResult();
            foreach (var kv in pairs)
            {
                if (generatable[kv.Key])
                {
                    result.Generated.Add(BuildGenerated(compilation, kv.Value, pairs, generatable));
                }
                else
                {
                    IsEligible(compilation, kv.Value, pairs, generatable, out var reason);
                    result.Skipped.Add(new SkippedPair { Pair = kv.Value, Reason = reason });
                }
            }

            return result;
        }

        private static bool IsEligible(Compilation compilation, MapPair pair, IReadOnlyDictionary<(INamedTypeSymbol, INamedTypeSymbol), MapPair> pairs, IReadOnlyDictionary<(INamedTypeSymbol, INamedTypeSymbol), bool> generatable, out string reason)
        {
            reason = "";
            var destType = pair.Destination;
            if (destType.IsAbstract || destType.TypeKind == TypeKind.Interface)
            {
                reason = "destination is abstract or an interface";
                return false;
            }
            if (!SymbolHelpers.HasPublicParameterlessCtor(destType))
            {
                reason = "destination has no public parameterless constructor";
                return false;
            }

            var destProps = SymbolHelpers.GetPublicWritableProperties(destType);
            foreach (var destProp in destProps)
            {
                if (ClassifyMember(compilation, pair, destProp, pairs, generatable, out var memberReason, out _) == MemberKind.None && memberReason != null)
                {
                    reason = memberReason;
                    return false;
                }
            }

            return true;
        }

        private static GeneratedPair BuildGenerated(Compilation compilation, MapPair pair, IReadOnlyDictionary<(INamedTypeSymbol, INamedTypeSymbol), MapPair> pairs, IReadOnlyDictionary<(INamedTypeSymbol, INamedTypeSymbol), bool> generatable)
        {
            var generated = new GeneratedPair { Pair = pair };
            foreach (var destProp in SymbolHelpers.GetPublicWritableProperties(pair.Destination))
            {
                var kind = ClassifyMember(compilation, pair, destProp, pairs, generatable, out _, out var plan);
                if (plan != null && kind != MemberKind.None)
                {
                    generated.Members.Add(plan);
                }
            }
            return generated;
        }

        /// <summary>
        /// Classifies a single destination member. Returns <see cref="MemberKind.None"/> when nothing
        /// should be emitted (ignored member, or no matching source member). When the member cannot be
        /// statically resolved and <paramref name="reason"/> is set, the pair is not generatable.
        /// </summary>
        private static MemberKind ClassifyMember(Compilation compilation, MapPair pair, IPropertySymbol destProp, IReadOnlyDictionary<(INamedTypeSymbol, INamedTypeSymbol), MapPair> pairs, IReadOnlyDictionary<(INamedTypeSymbol, INamedTypeSymbol), bool> generatable, out string? reason, out MemberPlan? plan)
        {
            reason = null;
            plan = null;

            // For reverse maps, per-property attributes (IgnoreMap/MapSource) on the original destination
            // type do not apply; fall back to pure convention matching.
            string? overrideName = null;
            if (!pair.IsReverse)
            {
                if (HasAttribute(compilation, destProp, IgnoreAttrMetadataName))
                {
                    return MemberKind.None;
                }

                overrideName = GetStringAttribute(compilation, destProp, MapSourceAttrMetadataName);
            }

            IPropertySymbol? sourceProp;
            if (overrideName != null)
            {
                sourceProp = SymbolHelpers.GetPropertyCaseInsensitive(pair.Source, overrideName, caseInsensitive: true, ignoreUnderscores: true);
            }
            else
            {
                sourceProp = SymbolHelpers.GetPropertyCaseInsensitive(pair.Source, destProp.Name, caseInsensitive: true, ignoreUnderscores: true);
            }

            if (sourceProp == null || sourceProp.GetMethod == null)
            {
                // No matching source member: leave at default, same as runtime convention without a resolver.
                return MemberKind.None;
            }

            var srcType = sourceProp.Type;
            var dstType = destProp.Type;

            // Same type -> direct assign.
            if (SymbolEqualityComparer.Default.Equals(srcType, dstType))
            {
                plan = new MemberPlan { DestProp = destProp, SourceProp = sourceProp, Kind = MemberKind.DirectAssign };
                return MemberKind.DirectAssign;
            }

            // Simple / enum conversions.
            if (SymbolHelpers.IsSimpleType(srcType) && SymbolHelpers.IsSimpleType(dstType))
            {
                if (SymbolHelpers.CanConvertSimpleType(srcType, dstType))
                {
                    plan = new MemberPlan { DestProp = destProp, SourceProp = sourceProp, Kind = MemberKind.Convert };
                    return MemberKind.Convert;
                }
                reason = $"member '{destProp.Name}' has incompatible simple types '{srcType}' -> '{dstType}'";
                return MemberKind.None;
            }

            // Collections.
            if (SymbolHelpers.IsEnumerable(srcType) && SymbolHelpers.IsEnumerable(dstType))
            {
                var srcElem = SymbolHelpers.GetEnumerableElementType(srcType)!;
                var dstElem = SymbolHelpers.GetEnumerableElementType(dstType)!;

                if (SymbolEqualityComparer.Default.Equals(srcElem, dstElem))
                {
                    if (SymbolHelpers.IsAssignableTo(compilation, srcType, dstType))
                    {
                        plan = new MemberPlan { DestProp = destProp, SourceProp = sourceProp, Kind = MemberKind.CollectionSame, SrcElem = srcElem, DstElem = dstElem, CollectionKind = SymbolHelpers.CollectionTarget.Direct };
                        return MemberKind.CollectionSame;
                    }

                    var target = SymbolHelpers.GetCollectionTarget(compilation, dstType, dstElem);
                    if (target == null)
                    {
                        reason = $"member '{destProp.Name}' collection type '{dstType}' cannot be materialized from a List<{dstElem}>";
                        return MemberKind.None;
                    }

                    plan = new MemberPlan { DestProp = destProp, SourceProp = sourceProp, Kind = MemberKind.CollectionSame, SrcElem = srcElem, DstElem = dstElem, CollectionKind = target.Value };
                    return MemberKind.CollectionSame;
                }

                if (SymbolHelpers.IsSimpleType(srcElem) && SymbolHelpers.IsSimpleType(dstElem) && SymbolHelpers.CanConvertSimpleType(srcElem, dstElem))
                {
                    var target = SymbolHelpers.GetCollectionTarget(compilation, dstType, dstElem);
                    if (target == null)
                    {
                        reason = $"member '{destProp.Name}' collection type '{dstType}' cannot be materialized from a List<{dstElem}>";
                        return MemberKind.None;
                    }

                    plan = new MemberPlan { DestProp = destProp, SourceProp = sourceProp, Kind = MemberKind.CollectionConvert, SrcElem = srcElem, DstElem = dstElem, CollectionKind = target.Value };
                    return MemberKind.CollectionConvert;
                }

                if (TryGetGeneratedNested(pairs, generatable, srcElem, dstElem, out var nestedReason))
                {
                    var target = SymbolHelpers.GetCollectionTarget(compilation, dstType, dstElem);
                    if (target == null)
                    {
                        reason = $"member '{destProp.Name}' collection type '{dstType}' cannot be materialized from a List<{dstElem}>";
                        return MemberKind.None;
                    }

                    plan = new MemberPlan { DestProp = destProp, SourceProp = sourceProp, Kind = MemberKind.CollectionNested, SrcElem = srcElem, DstElem = dstElem, NestedSource = (INamedTypeSymbol)srcElem, NestedDest = (INamedTypeSymbol)dstElem, CollectionKind = target.Value };
                    return MemberKind.CollectionNested;
                }

                reason = nestedReason ?? $"member '{destProp.Name}' collection element map '{srcElem}' -> '{dstElem}' is not generated";
                return MemberKind.None;
            }

            // Nested object.
            if (!SymbolHelpers.IsEnumerable(srcType) && !SymbolHelpers.IsEnumerable(dstType)
                && srcType is INamedTypeSymbol srcNamed && dstType is INamedTypeSymbol dstNamed)
            {
                if (TryGetGeneratedNested(pairs, generatable, srcNamed, dstNamed, out var nestedReason))
                {
                    plan = new MemberPlan { DestProp = destProp, SourceProp = sourceProp, Kind = MemberKind.Nested, NestedSource = srcNamed, NestedDest = dstNamed };
                    return MemberKind.Nested;
                }

                reason = nestedReason ?? $"member '{destProp.Name}' nested map '{srcType}' -> '{dstType}' is not generated";
                return MemberKind.None;
            }

            reason = $"member '{destProp.Name}' of type '{dstType}' has no static mapping from '{srcType}'";
            return MemberKind.None;
        }

        private static bool TryGetGeneratedNested(IReadOnlyDictionary<(INamedTypeSymbol, INamedTypeSymbol), MapPair> pairs, IReadOnlyDictionary<(INamedTypeSymbol, INamedTypeSymbol), bool> generatable, ITypeSymbol srcElem, ITypeSymbol dstElem, out string? reason)
        {
            reason = null;
            if (srcElem is not INamedTypeSymbol srcNamed || dstElem is not INamedTypeSymbol dstNamed)
            {
                reason = "nested element types are not named types";
                return false;
            }

            if (!pairs.TryGetValue((srcNamed, dstNamed), out var depPair))
            {
                reason = $"nested map '{srcNamed}' -> '{dstNamed}' is not declared with [AutoMap]";
                return false;
            }

            if (!generatable[(srcNamed, dstNamed)])
            {
                reason = $"depends on nested map '{srcNamed}' -> '{dstNamed}' which itself falls back to runtime";
                return false;
            }

            return true;
        }

        private sealed class SymbolPairComparer : IEqualityComparer<(INamedTypeSymbol, INamedTypeSymbol)>
        {
            public static readonly SymbolPairComparer Instance = new SymbolPairComparer();
            public bool Equals((INamedTypeSymbol, INamedTypeSymbol) x, (INamedTypeSymbol, INamedTypeSymbol) y)
                => SymbolEqualityComparer.Default.Equals(x.Item1, y.Item1) && SymbolEqualityComparer.Default.Equals(x.Item2, y.Item2);
            public int GetHashCode((INamedTypeSymbol, INamedTypeSymbol) obj)
                => unchecked(SymbolEqualityComparer.Default.GetHashCode(obj.Item1!) * 397 ^ SymbolEqualityComparer.Default.GetHashCode(obj.Item2!));
        }

        private static bool HasAttribute(Compilation compilation, ISymbol symbol, string metadataName)
        {
            var attrSymbol = compilation.GetTypeByMetadataName(metadataName);
            if (attrSymbol == null) return false;
            foreach (var attr in symbol.GetAttributes())
            {
                if (SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrSymbol)) return true;
            }
            return false;
        }

        private static string? GetStringAttribute(Compilation compilation, ISymbol symbol, string metadataName)
        {
            var attrSymbol = compilation.GetTypeByMetadataName(metadataName);
            if (attrSymbol == null) return null;
            foreach (var attr in symbol.GetAttributes())
            {
                if (!SymbolEqualityComparer.Default.Equals(attr.AttributeClass, attrSymbol)) continue;
                if (attr.ConstructorArguments.Length > 0 && attr.ConstructorArguments[0].Value is string s) return s;
            }
            return null;
        }
    }
}