using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AutoArchitecture;

/// <summary>
/// Enforces compile-time namespace dependency rules declared via
/// <c>[assembly: ForbidDependency(fromNamespace, toNamespace)]</c>. Reports <c>AA001</c>
/// whenever a type in a forbidden "from" namespace references a type in the forbidden
/// "to" namespace.
/// </summary>
[Generator]
public sealed class AutoArchitectureGenerator : IIncrementalGenerator
{
    private const string AttributeSource = """
        #nullable enable
        namespace AutoArchitecture
        {
            /// <summary>
            /// Declares a forbidden dependency direction between two namespaces (and their
            /// sub-namespaces). Apply at assembly level, one attribute per rule:
            /// <c>[assembly: AutoArchitecture.ForbidDependency("MyApp.UI", "MyApp.DataAccess")]</c>
            /// </summary>
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
            public sealed class ForbidDependencyAttribute : System.Attribute
            {
                public ForbidDependencyAttribute(string fromNamespace, string toNamespace)
                {
                    FromNamespace = fromNamespace;
                    ToNamespace = toNamespace;
                }

                /// <summary>The namespace (and sub-namespaces) that must not depend on <see cref="ToNamespace"/>.</summary>
                public string FromNamespace { get; }

                /// <summary>The namespace (and sub-namespaces) that <see cref="FromNamespace"/> must not reference.</summary>
                public string ToNamespace { get; }

                /// <summary>Optional human-readable justification included in the diagnostic message.</summary>
                public string? Because { get; set; }
            }
        }
        """;

    private static readonly DiagnosticDescriptor ForbiddenDependencyRule = new(
        id: "AA001",
        title: "Forbidden namespace dependency",
        messageFormat: "'{0}' in namespace '{1}' references '{2}' in namespace '{3}', which is forbidden by [assembly: ForbidDependency(\"{1}\", \"{3}\")]{4}",
        category: "AutoArchitecture",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        context.RegisterPostInitializationOutput(static ctx =>
            ctx.AddSource("AutoArchitectureAttributes.g.cs", SourceText.From(AttributeSource, Encoding.UTF8)));

        context.RegisterSourceOutput(context.CompilationProvider, static (spc, compilation) =>
            Analyze(compilation, spc));
    }

    private static void Analyze(Compilation compilation, SourceProductionContext context)
    {
        var rules = GetRules(compilation);
        if (rules.Count == 0)
        {
            return;
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(context.CancellationToken);

            foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, context.CancellationToken) as INamedTypeSymbol;
                if (typeSymbol is null)
                {
                    continue;
                }

                var fromNamespace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;
                var matchingRules = rules.Where(r => IsInNamespace(fromNamespace, r.FromNamespace)).ToList();
                if (matchingRules.Count == 0)
                {
                    continue;
                }

                foreach (var nameNode in typeDecl.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    var symbol = semanticModel.GetSymbolInfo(nameNode, context.CancellationToken).Symbol;
                    var referencedType = ResolveRelevantType(symbol);
                    if (referencedType is null)
                    {
                        continue;
                    }

                    var toNamespace = referencedType.ContainingNamespace?.ToDisplayString() ?? string.Empty;

                    foreach (var rule in matchingRules)
                    {
                        if (!IsInNamespace(toNamespace, rule.ToNamespace))
                        {
                            continue;
                        }

                        var because = string.IsNullOrEmpty(rule.Because) ? string.Empty : $" ({rule.Because})";
                        var diagnostic = Diagnostic.Create(
                            ForbiddenDependencyRule,
                            nameNode.GetLocation(),
                            typeSymbol.Name,
                            fromNamespace,
                            referencedType.Name,
                            toNamespace,
                            because);
                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }

    private static INamedTypeSymbol? ResolveRelevantType(ISymbol? symbol)
    {
        return symbol switch
        {
            INamedTypeSymbol namedType => namedType,
            IMethodSymbol method => method.ContainingType,
            IFieldSymbol field => field.ContainingType,
            IPropertySymbol property => property.ContainingType,
            IEventSymbol @event => @event.ContainingType,
            _ => null,
        };
    }

    private static bool IsInNamespace(string ns, string prefix)
    {
        if (string.IsNullOrEmpty(prefix))
        {
            return false;
        }

        return ns.Equals(prefix, StringComparison.Ordinal)
            || ns.StartsWith(prefix + ".", StringComparison.Ordinal);
    }

    private static List<(string FromNamespace, string ToNamespace, string? Because)> GetRules(Compilation compilation)
    {
        var rules = new List<(string, string, string?)>();

        foreach (var attribute in compilation.Assembly.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "AutoArchitecture.ForbidDependencyAttribute")
            {
                continue;
            }

            if (attribute.ConstructorArguments.Length != 2)
            {
                continue;
            }

            var from = attribute.ConstructorArguments[0].Value as string;
            var to = attribute.ConstructorArguments[1].Value as string;
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to))
            {
                continue;
            }

            string? because = null;
            foreach (var named in attribute.NamedArguments)
            {
                if (named.Key == "Because")
                {
                    because = named.Value.Value as string;
                }
            }

            rules.Add((from!, to!, because));
        }

        return rules;
    }
}
