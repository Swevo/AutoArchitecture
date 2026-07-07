using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace AutoArchitecture.Tests;

/// <summary>
/// Generator-driver tests that verify AA001 is emitted correctly for forbidden
/// namespace dependencies declared via <c>[assembly: ForbidDependency(...)]</c>.
/// </summary>
public class DiagnosticTests
{
    [Fact]
    public void DirectTypeReference_ToForbiddenNamespace_EmitsAA001()
    {
        var source = """
            [assembly: AutoArchitecture.ForbidDependency("MyApp.UI", "MyApp.DataAccess")]

            namespace MyApp.DataAccess
            {
                public class OrderRepository { }
            }

            namespace MyApp.UI
            {
                public class OrderController
                {
                    private readonly DataAccess.OrderRepository _repository = new DataAccess.OrderRepository();
                }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AA001");
    }

    [Fact]
    public void MethodCall_OnForbiddenNamespaceType_EmitsAA001()
    {
        var source = """
            [assembly: AutoArchitecture.ForbidDependency("MyApp.UI", "MyApp.DataAccess")]

            namespace MyApp.DataAccess
            {
                public static class Db
                {
                    public static void Save() { }
                }
            }

            namespace MyApp.UI
            {
                public class OrderController
                {
                    public void Handle()
                    {
                        DataAccess.Db.Save();
                    }
                }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AA001");
    }

    [Fact]
    public void ReferenceOutsideForbiddenPair_NoWarning()
    {
        var source = """
            [assembly: AutoArchitecture.ForbidDependency("MyApp.UI", "MyApp.DataAccess")]

            namespace MyApp.DataAccess
            {
                public class OrderRepository { }
            }

            namespace MyApp.Services
            {
                public class OrderService
                {
                    private readonly DataAccess.OrderRepository _repository = new DataAccess.OrderRepository();
                }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AA001");
    }

    [Fact]
    public void SubNamespace_OfForbiddenTarget_StillMatches()
    {
        var source = """
            [assembly: AutoArchitecture.ForbidDependency("MyApp.UI", "MyApp.DataAccess")]

            namespace MyApp.DataAccess.Sql
            {
                public class SqlOrderRepository { }
            }

            namespace MyApp.UI
            {
                public class OrderController
                {
                    private readonly DataAccess.Sql.SqlOrderRepository _repository = new DataAccess.Sql.SqlOrderRepository();
                }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.Contains(diagnostics, d => d.Id == "AA001");
    }

    [Fact]
    public void SimilarButUnrelatedNamespacePrefix_DoesNotMatch()
    {
        // "MyApp.DataAccessLegacy" should NOT be treated as a sub-namespace of "MyApp.DataAccess".
        var source = """
            [assembly: AutoArchitecture.ForbidDependency("MyApp.UI", "MyApp.DataAccess")]

            namespace MyApp.DataAccessLegacy
            {
                public class LegacyRepository { }
            }

            namespace MyApp.UI
            {
                public class OrderController
                {
                    private readonly DataAccessLegacy.LegacyRepository _repository = new DataAccessLegacy.LegacyRepository();
                }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AA001");
    }

    [Fact]
    public void NoForbidDependencyAttribute_NoWarning()
    {
        var source = """
            namespace MyApp.DataAccess
            {
                public class OrderRepository { }
            }

            namespace MyApp.UI
            {
                public class OrderController
                {
                    private readonly DataAccess.OrderRepository _repository = new DataAccess.OrderRepository();
                }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AA001");
    }

    [Fact]
    public void BecauseReason_IsIncludedInDiagnosticMessage()
    {
        var source = """
            [assembly: AutoArchitecture.ForbidDependency("MyApp.UI", "MyApp.DataAccess", Because = "UI must go through the service layer")]

            namespace MyApp.DataAccess
            {
                public class OrderRepository { }
            }

            namespace MyApp.UI
            {
                public class OrderController
                {
                    private readonly DataAccess.OrderRepository _repository = new DataAccess.OrderRepository();
                }
            }
            """;

        var diagnostics = RunGenerator(source);
        var diagnostic = diagnostics.First(d => d.Id == "AA001");
        Assert.Contains("UI must go through the service layer", diagnostic.GetMessage());
    }

    private static IReadOnlyList<Diagnostic> RunGenerator(string source)
    {
        var references = ((string?)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
            ?.Split(Path.PathSeparator)
            .Select(path => (MetadataReference)MetadataReference.CreateFromFile(path))
            .ToList()
            ?? new List<MetadataReference>();

        var compilation = CSharpCompilation.Create(
            assemblyName: "DiagnosticTestAssembly",
            syntaxTrees: new[] { CSharpSyntaxTree.ParseText(source) },
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new AutoArchitectureGenerator();
        var driver = CSharpGeneratorDriver
            .Create(generator)
            .RunGenerators(compilation);

        var result = driver.GetRunResult();
        return result.Diagnostics;
    }
}
