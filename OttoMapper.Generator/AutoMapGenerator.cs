using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace OttoMapper.Generator
{
    /// <summary>
    /// Incremental source generator that emits compile-time convention maps for destination types
    /// decorated with <c>[AutoMap(typeof(TSource))]</c>. Generated maps are registered into
    /// <c>OttoMapper.Mapping.Generated.GeneratedMapRegistry</c> via a module initializer.
    /// </summary>
    [Generator]
    public sealed class AutoMapGenerator : IIncrementalGenerator
    {
        private const string AutoMapAttrMetadataName = "OttoMapper.Mapping.Generated.AutoMapAttribute";

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var declarations = context.SyntaxProvider
                .ForAttributeWithMetadataName(
                    AutoMapAttrMetadataName,
                    static (node, _) => node is ClassDeclarationSyntax || node is StructDeclarationSyntax,
                    static (ctx, _) => CollectDeclaration(ctx))
                .Collect();

            var pipeline = declarations.Combine(context.CompilationProvider);

            context.RegisterSourceOutput(pipeline, static (spc, pair) =>
            {
                var (decls, compilation) = pair;
                var valid = decls.Where(d => d is not null).Select(d => d!).ToImmutableArray();
                if (valid.IsEmpty)
                {
                    return;
                }

                var result = Classifier.Classify(compilation, valid);

                foreach (var (_, diagnostic) in Emitter.EmitDiagnostics(result))
                {
                    spc.ReportDiagnostic(diagnostic);
                }

                foreach (var (hintName, source) in Emitter.Emit(result))
                {
                    spc.AddSource(hintName, source);
                }
            });
        }

        private static MapDeclaration? CollectDeclaration(GeneratorAttributeSyntaxContext ctx)
        {
            if (ctx.TargetSymbol is not INamedTypeSymbol destType)
            {
                return null;
            }

            var attribute = ctx.Attributes.FirstOrDefault();
            if (attribute == null)
            {
                return null;
            }

            if (attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not INamedTypeSymbol sourceType)
            {
                return null;
            }

            var reverse = false;
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "Reverse" && named.Value.Value is bool b)
                {
                    reverse = b;
                }
            }

            var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None;

            return new MapDeclaration
            {
                Source = sourceType,
                Destination = destType,
                Reverse = reverse,
                Location = location,
            };
        }
    }
}