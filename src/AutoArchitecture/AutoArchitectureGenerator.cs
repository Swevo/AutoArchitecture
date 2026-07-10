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

            /// <summary>
            /// Enables opt-in circular dependency detection between namespaces in the current
            /// compilation. Apply at assembly level:
            /// <c>[assembly: AutoArchitecture.DetectCircularDependencies]</c>
            /// </summary>
            [System.AttributeUsage(System.AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
            public sealed class DetectCircularDependenciesAttribute : System.Attribute
            {
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

    private static readonly DiagnosticDescriptor CircularDependencyRule = new(
        id: "AA002",
        title: "Circular namespace dependency",
        messageFormat: "'{0}' in namespace '{1}' references '{2}' in namespace '{3}', creating a circular dependency: {4}",
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
        var detectCircularDependencies = HasCircularDependencyDetectionEnabled(compilation);
        if (rules.Count == 0 && !detectCircularDependencies)
        {
            return;
        }

        var references = GetNamespaceReferences(compilation, context.CancellationToken);

        if (rules.Count > 0)
        {
            ReportForbiddenDependencyDiagnostics(references, rules, context);
        }

        if (detectCircularDependencies)
        {
            ReportCircularDependencyDiagnostics(references, context);
        }
    }

    private static IReadOnlyList<NamespaceReference> GetNamespaceReferences(
        Compilation compilation,
        System.Threading.CancellationToken cancellationToken)
    {
        var references = new List<NamespaceReference>();

        foreach (var tree in compilation.SyntaxTrees)
        {
            var semanticModel = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot(cancellationToken);

            foreach (var typeDecl in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                var typeSymbol = semanticModel.GetDeclaredSymbol(typeDecl, cancellationToken) as INamedTypeSymbol;
                if (typeSymbol is null)
                {
                    continue;
                }

                var fromNamespace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? string.Empty;

                foreach (var nameNode in typeDecl.DescendantNodes().OfType<SimpleNameSyntax>())
                {
                    var symbol = semanticModel.GetSymbolInfo(nameNode, cancellationToken).Symbol;
                    var referencedType = ResolveRelevantType(symbol);
                    if (referencedType is null)
                    {
                        continue;
                    }

                    references.Add(new NamespaceReference(
                        typeSymbol.Name,
                        fromNamespace,
                        referencedType.Name,
                        referencedType.ContainingNamespace?.ToDisplayString() ?? string.Empty,
                        nameNode.GetLocation()));
                }
            }
        }

        return references;
    }

    private static void ReportForbiddenDependencyDiagnostics(
        IReadOnlyList<NamespaceReference> references,
        IReadOnlyList<DependencyRule> rules,
        SourceProductionContext context)
    {
        foreach (var reference in references)
        {
            var matchingRules = rules.Where(rule => IsInNamespace(reference.FromNamespace, rule.FromNamespace)).ToList();
            if (matchingRules.Count == 0)
            {
                continue;
            }

            foreach (var rule in matchingRules)
            {
                if (!IsInNamespace(reference.ToNamespace, rule.ToNamespace))
                {
                    continue;
                }

                var because = string.IsNullOrEmpty(rule.Because) ? string.Empty : $" ({rule.Because})";
                context.ReportDiagnostic(Diagnostic.Create(
                    ForbiddenDependencyRule,
                    reference.Location,
                    reference.SourceTypeName,
                    reference.FromNamespace,
                    reference.ReferencedTypeName,
                    reference.ToNamespace,
                    because));
            }
        }
    }

    private static void ReportCircularDependencyDiagnostics(
        IReadOnlyList<NamespaceReference> references,
        SourceProductionContext context)
    {
        var analysis = CircularDependencyDetector.Analyze(
            references
                .Where(static reference => !string.IsNullOrEmpty(reference.FromNamespace)
                    && !string.IsNullOrEmpty(reference.ToNamespace)
                    && !string.Equals(reference.FromNamespace, reference.ToNamespace, StringComparison.Ordinal))
                .Select(static reference => new NamespaceDependencyEdge(reference.FromNamespace, reference.ToNamespace)));

        if (analysis.Components.Count == 0)
        {
            return;
        }

        foreach (var reference in references)
        {
            if (!analysis.IsCycleParticipant(reference.FromNamespace, reference.ToNamespace))
            {
                continue;
            }

            context.ReportDiagnostic(Diagnostic.Create(
                CircularDependencyRule,
                reference.Location,
                reference.SourceTypeName,
                reference.FromNamespace,
                reference.ReferencedTypeName,
                reference.ToNamespace,
                analysis.GetCycleChain(reference.FromNamespace, reference.ToNamespace)));
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

    private static bool HasCircularDependencyDetectionEnabled(Compilation compilation)
    {
        return compilation.Assembly
            .GetAttributes()
            .Any(static attribute => attribute.AttributeClass?.ToDisplayString() == "AutoArchitecture.DetectCircularDependenciesAttribute");
    }

    private static List<DependencyRule> GetRules(Compilation compilation)
    {
        var rules = new List<DependencyRule>();

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

            rules.Add(new DependencyRule(from!, to!, because));
        }

        return rules;
    }

    private readonly struct DependencyRule
    {
        public DependencyRule(string fromNamespace, string toNamespace, string? because)
        {
            FromNamespace = fromNamespace;
            ToNamespace = toNamespace;
            Because = because;
        }

        public string FromNamespace { get; }

        public string ToNamespace { get; }

        public string? Because { get; }
    }

    private readonly struct NamespaceReference
    {
        public NamespaceReference(
            string sourceTypeName,
            string fromNamespace,
            string referencedTypeName,
            string toNamespace,
            Location location)
        {
            SourceTypeName = sourceTypeName;
            FromNamespace = fromNamespace;
            ReferencedTypeName = referencedTypeName;
            ToNamespace = toNamespace;
            Location = location;
        }

        public string SourceTypeName { get; }

        public string FromNamespace { get; }

        public string ReferencedTypeName { get; }

        public string ToNamespace { get; }

        public Location Location { get; }
    }
}
