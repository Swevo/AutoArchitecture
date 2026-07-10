using System.IO;
using System.Linq;
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

    [Fact]
    public void TwoNamespaceCycle_WithAttribute_EmitsAA002()
    {
        var source = """
            [assembly: AutoArchitecture.DetectCircularDependencies]

            namespace MyApp.A
            {
                public class AType
                {
                    private readonly B.BType _dependency = new B.BType();
                }
            }

            namespace MyApp.B
            {
                public class BType
                {
                    private readonly A.AType _dependency = new A.AType();
                }
            }
            """;

        var diagnostics = RunGenerator(source).Where(d => d.Id == "AA002").ToList();
        Assert.True(diagnostics.Count >= 2);
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("MyApp.A -> MyApp.B -> MyApp.A"));
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("MyApp.B -> MyApp.A -> MyApp.B"));
    }

    [Fact]
    public void ThreeNamespaceCycle_WithAttribute_EmitsAA002WithFullCycleChain()
    {
        var source = """
            [assembly: AutoArchitecture.DetectCircularDependencies]

            namespace MyApp.A
            {
                public class AType
                {
                    private readonly B.BType _dependency = new B.BType();
                }
            }

            namespace MyApp.B
            {
                public class BType
                {
                    private readonly C.CType _dependency = new C.CType();
                }
            }

            namespace MyApp.C
            {
                public class CType
                {
                    private readonly A.AType _dependency = new A.AType();
                }
            }
            """;

        var diagnostics = RunGenerator(source).Where(d => d.Id == "AA002").ToList();
        Assert.True(diagnostics.Count >= 3);
        Assert.Contains(diagnostics, diagnostic => diagnostic.GetMessage().Contains("MyApp.A -> MyApp.B -> MyApp.C -> MyApp.A"));
    }

    [Fact]
    public void DetectCircularDependenciesAttribute_OnAcyclicDiamondGraph_EmitsNoWarning()
    {
        var source = """
            [assembly: AutoArchitecture.DetectCircularDependencies]

            namespace MyApp.B
            {
                public class BType { }
            }

            namespace MyApp.C
            {
                public class CType { }
            }

            namespace MyApp.D
            {
                public class DType { }
            }

            namespace MyApp.A
            {
                public class AType
                {
                    private readonly B.BType _b = new B.BType();
                    private readonly C.CType _c = new C.CType();
                }
            }

            namespace MyApp.B
            {
                public class BUsesD
                {
                    private readonly D.DType _dependency = new D.DType();
                }
            }

            namespace MyApp.C
            {
                public class CUsesD
                {
                    private readonly D.DType _dependency = new D.DType();
                }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AA002");
    }

    [Fact]
    public void CircularDependencyWithoutAttribute_NoAA002()
    {
        var source = """
            namespace MyApp.A
            {
                public class AType
                {
                    private readonly B.BType _dependency = new B.BType();
                }
            }

            namespace MyApp.B
            {
                public class BType
                {
                    private readonly A.AType _dependency = new A.AType();
                }
            }
            """;

        var diagnostics = RunGenerator(source);
        Assert.DoesNotContain(diagnostics, d => d.Id == "AA002");
    }

    [Fact]
    public void ForbidDependencyAndCircularDependencyRules_CanCoexist()
    {
        var source = """
            [assembly: AutoArchitecture.ForbidDependency("MyApp.A", "MyApp.B", Because = "A must not directly call B")]
            [assembly: AutoArchitecture.DetectCircularDependencies]

            namespace MyApp.A
            {
                public class AType
                {
                    private readonly B.BType _dependency = new B.BType();
                }
            }

            namespace MyApp.B
            {
                public class BType
                {
                    private readonly A.AType _dependency = new A.AType();
                }
            }
            """;

        var diagnostics = RunGenerator(source).ToList();
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AA001");
        Assert.Contains(diagnostics, diagnostic => diagnostic.Id == "AA002");
    }

    private static IReadOnlyList<Microsoft.CodeAnalysis.Diagnostic> RunGenerator(string source) => TestCompiler.RunGenerator(source);
}
