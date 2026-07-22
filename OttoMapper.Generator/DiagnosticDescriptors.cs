using Microsoft.CodeAnalysis;

namespace OttoMapper.Generator
{
    internal static class DiagnosticDescriptors
    {
        // OTTOMAP001: a declared [AutoMap] pair cannot be statically generated and falls back to runtime.
        public static readonly DiagnosticDescriptor PairFallsBackToRuntime = new DiagnosticDescriptor(
            id: "OTTOMAP001",
            title: "OttoMapper map falls back to runtime",
            messageFormat: "OttoMapper: the map '{0}' -> '{1}' cannot be generated at compile time ({2}) and will use the runtime expression-tree path",
            category: "OttoMapper",
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true,
            description: "Emitted when a declared [AutoMap] pair contains a member that is not statically resolvable (custom resolvers, runtime-nested maps, init-only properties, records without a parameterless constructor, etc.).");
    }
}