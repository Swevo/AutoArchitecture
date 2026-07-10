using System.Linq;
using Xunit;

namespace AutoArchitecture.Tests;

public class CircularDependencyDetectorTests
{
    [Fact]
    public void Analyze_TwoNodeCycle_ReturnsSingleComponentAndCycleChain()
    {
        var analysis = CircularDependencyDetector.Analyze(new[]
        {
            new NamespaceDependencyEdge("MyApp.A", "MyApp.B"),
            new NamespaceDependencyEdge("MyApp.B", "MyApp.A")
        });

        var component = Assert.Single(analysis.Components);
        Assert.Equal(new[] { "MyApp.A", "MyApp.B" }, component.OrderBy(static ns => ns).ToArray());
        Assert.True(analysis.IsCycleParticipant("MyApp.A", "MyApp.B"));
        Assert.Equal("MyApp.A -> MyApp.B -> MyApp.A", analysis.GetCycleChain("MyApp.A", "MyApp.B"));
    }

    [Fact]
    public void Analyze_ThreeNodeCycle_ReturnsCycleChainThroughAllNamespaces()
    {
        var analysis = CircularDependencyDetector.Analyze(new[]
        {
            new NamespaceDependencyEdge("MyApp.A", "MyApp.B"),
            new NamespaceDependencyEdge("MyApp.B", "MyApp.C"),
            new NamespaceDependencyEdge("MyApp.C", "MyApp.A")
        });

        Assert.True(analysis.IsCycleParticipant("MyApp.A", "MyApp.B"));
        Assert.Equal("MyApp.A -> MyApp.B -> MyApp.C -> MyApp.A", analysis.GetCycleChain("MyApp.A", "MyApp.B"));
    }

    [Fact]
    public void Analyze_AcyclicGraph_ReturnsNoCycleComponents()
    {
        var analysis = CircularDependencyDetector.Analyze(new[]
        {
            new NamespaceDependencyEdge("MyApp.A", "MyApp.B"),
            new NamespaceDependencyEdge("MyApp.A", "MyApp.C"),
            new NamespaceDependencyEdge("MyApp.B", "MyApp.D"),
            new NamespaceDependencyEdge("MyApp.C", "MyApp.D")
        });

        Assert.Empty(analysis.Components);
        Assert.False(analysis.IsCycleParticipant("MyApp.A", "MyApp.B"));
    }
}
