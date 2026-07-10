using System;
using System.Collections.Generic;
using System.Linq;

namespace AutoArchitecture;

internal static class CircularDependencyDetector
{
    public static CircularDependencyAnalysis Analyze(IEnumerable<NamespaceDependencyEdge> edges)
    {
        var adjacency = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var edge in edges)
        {
            if (string.IsNullOrEmpty(edge.FromNamespace) || string.IsNullOrEmpty(edge.ToNamespace))
            {
                continue;
            }

            if (!adjacency.TryGetValue(edge.FromNamespace, out var outgoing))
            {
                outgoing = new HashSet<string>(StringComparer.Ordinal);
                adjacency[edge.FromNamespace] = outgoing;
            }

            outgoing.Add(edge.ToNamespace);

            if (!adjacency.ContainsKey(edge.ToNamespace))
            {
                adjacency[edge.ToNamespace] = new HashSet<string>(StringComparer.Ordinal);
            }
        }

        var components = FindStronglyConnectedComponents(adjacency)
            .Where(component => component.Count > 1)
            .Select(component => (IReadOnlyList<string>)component)
            .ToList();

        var componentByNamespace = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < components.Count; i++)
        {
            foreach (var ns in components[i])
            {
                componentByNamespace[ns] = i;
            }
        }

        return new CircularDependencyAnalysis(adjacency, components, componentByNamespace);
    }

    internal static IReadOnlyList<IReadOnlyList<string>> FindStronglyConnectedComponents(
        IReadOnlyDictionary<string, HashSet<string>> adjacency)
    {
        var index = 0;
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var lowLinks = new Dictionary<string, int>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        var onStack = new HashSet<string>(StringComparer.Ordinal);
        var components = new List<IReadOnlyList<string>>();

        foreach (var node in adjacency.Keys.OrderBy(static ns => ns, StringComparer.Ordinal))
        {
            if (!indices.ContainsKey(node))
            {
                StrongConnect(node);
            }
        }

        return components;

        void StrongConnect(string node)
        {
            indices[node] = index;
            lowLinks[node] = index;
            index++;
            stack.Push(node);
            onStack.Add(node);

            foreach (var neighbor in adjacency[node].OrderBy(static ns => ns, StringComparer.Ordinal))
            {
                if (!indices.ContainsKey(neighbor))
                {
                    StrongConnect(neighbor);
                    lowLinks[node] = Math.Min(lowLinks[node], lowLinks[neighbor]);
                }
                else if (onStack.Contains(neighbor))
                {
                    lowLinks[node] = Math.Min(lowLinks[node], indices[neighbor]);
                }
            }

            if (lowLinks[node] != indices[node])
            {
                return;
            }

            var component = new List<string>();
            string current;
            do
            {
                current = stack.Pop();
                onStack.Remove(current);
                component.Add(current);
            }
            while (!string.Equals(current, node, StringComparison.Ordinal));

            component.Sort(StringComparer.Ordinal);
            components.Add(component);
        }
    }
}

internal sealed class CircularDependencyAnalysis
{
    private readonly IReadOnlyDictionary<string, HashSet<string>> adjacency;
    private readonly IReadOnlyDictionary<string, int> componentByNamespace;
    private readonly Dictionary<NamespaceDependencyEdge, string> cycleCache;

    public CircularDependencyAnalysis(
        IReadOnlyDictionary<string, HashSet<string>> adjacency,
        IReadOnlyList<IReadOnlyList<string>> components,
        IReadOnlyDictionary<string, int> componentByNamespace)
    {
        this.adjacency = adjacency;
        this.componentByNamespace = componentByNamespace;
        cycleCache = new Dictionary<NamespaceDependencyEdge, string>();
        Components = components;
    }

    public IReadOnlyList<IReadOnlyList<string>> Components { get; }

    public bool IsCycleParticipant(string fromNamespace, string toNamespace)
    {
        return componentByNamespace.TryGetValue(fromNamespace, out var fromComponent)
            && componentByNamespace.TryGetValue(toNamespace, out var toComponent)
            && fromComponent == toComponent;
    }

    public string GetCycleChain(string fromNamespace, string toNamespace)
    {
        var edge = new NamespaceDependencyEdge(fromNamespace, toNamespace);
        if (cycleCache.TryGetValue(edge, out var cached))
        {
            return cached;
        }

        if (!componentByNamespace.TryGetValue(fromNamespace, out var componentId)
            || !componentByNamespace.TryGetValue(toNamespace, out var targetComponentId)
            || componentId != targetComponentId)
        {
            throw new InvalidOperationException($"No circular dependency exists for '{fromNamespace}' -> '{toNamespace}'.");
        }

        var component = new HashSet<string>(Components[componentId], StringComparer.Ordinal);
        var pathBackToSource = FindPathWithinComponent(toNamespace, fromNamespace, component);
        var cycle = string.Join(" -> ", new[] { fromNamespace }.Concat(pathBackToSource));
        cycleCache[edge] = cycle;
        return cycle;
    }

    private IReadOnlyList<string> FindPathWithinComponent(
        string startNamespace,
        string targetNamespace,
        ISet<string> component)
    {
        var queue = new Queue<string>();
        var previous = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [startNamespace] = null
        };

        queue.Enqueue(startNamespace);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (string.Equals(current, targetNamespace, StringComparison.Ordinal))
            {
                return ReconstructPath(current, previous);
            }

            if (!adjacency.TryGetValue(current, out var outgoing))
            {
                continue;
            }

            foreach (var neighbor in outgoing.OrderBy(static ns => ns, StringComparer.Ordinal))
            {
                if (!component.Contains(neighbor) || previous.ContainsKey(neighbor))
                {
                    continue;
                }

                previous[neighbor] = current;
                queue.Enqueue(neighbor);
            }
        }

        throw new InvalidOperationException(
            $"Unable to construct a cycle path from '{startNamespace}' back to '{targetNamespace}'.");
    }

    private static IReadOnlyList<string> ReconstructPath(
        string targetNamespace,
        IReadOnlyDictionary<string, string?> previous)
    {
        var path = new List<string>();
        var current = targetNamespace;

        while (current is not null)
        {
            path.Add(current);
            current = previous[current];
        }

        path.Reverse();
        return path;
    }
}

internal readonly struct NamespaceDependencyEdge : IEquatable<NamespaceDependencyEdge>
{
    public NamespaceDependencyEdge(string fromNamespace, string toNamespace)
    {
        FromNamespace = fromNamespace;
        ToNamespace = toNamespace;
    }

    public string FromNamespace { get; }

    public string ToNamespace { get; }

    public bool Equals(NamespaceDependencyEdge other)
    {
        return string.Equals(FromNamespace, other.FromNamespace, StringComparison.Ordinal)
            && string.Equals(ToNamespace, other.ToNamespace, StringComparison.Ordinal);
    }

    public override bool Equals(object? obj)
    {
        return obj is NamespaceDependencyEdge other && Equals(other);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            return ((FromNamespace != null ? StringComparer.Ordinal.GetHashCode(FromNamespace) : 0) * 397)
                ^ (ToNamespace != null ? StringComparer.Ordinal.GetHashCode(ToNamespace) : 0);
        }
    }
}
