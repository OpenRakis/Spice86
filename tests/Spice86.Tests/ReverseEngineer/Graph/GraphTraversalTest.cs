namespace Spice86.Tests.ReverseEngineer.Graph;

using FluentAssertions;

using Spice86.Core.Emulator.ReverseEngineer.Graph;

using Xunit;

/// <summary>
/// Direct tests for <see cref="DepthFirstSearch"/>, <see cref="BreadthFirstSearch"/>,
/// and <see cref="GraphTraversal"/> utilities.
/// </summary>
public sealed class GraphTraversalTest {
    private static readonly Dictionary<string, string[]> Diamond = new() {
        ["A"] = ["B", "C"],
        ["B"] = ["D"],
        ["C"] = ["D"],
        ["D"] = []
    };

    private static IEnumerable<string> Neighbors(string node) =>
        Diamond.TryGetValue(node, out string[]? neighbors) ? neighbors : [];

    [Fact]
    public void DepthFirstSearch_Enumerate_VisitsAllReachableNodes() {
        List<string> visited = DepthFirstSearch.Enumerate("A", Neighbors).ToList();

        visited.Should().HaveCount(4);
        visited.Should().Contain(["A", "B", "C", "D"]);
        visited[0].Should().Be("A", "seed is always first");
    }

    [Fact]
    public void BreadthFirstSearch_Enumerate_VisitsAllReachableNodes() {
        List<string> visited = BreadthFirstSearch.Enumerate("A", Neighbors).ToList();

        visited.Should().HaveCount(4);
        visited.Should().Contain(["A", "B", "C", "D"]);
        visited[0].Should().Be("A", "seed is always first");
    }

    [Fact]
    public void BreadthFirstSearch_Enumerate_YieldsInBfsOrder() {
        List<string> visited = BreadthFirstSearch.Enumerate("A", Neighbors).ToList();

        int indexA = visited.IndexOf("A");
        int indexB = visited.IndexOf("B");
        int indexC = visited.IndexOf("C");
        int indexD = visited.IndexOf("D");
        indexA.Should().BeLessThan(indexB);
        indexA.Should().BeLessThan(indexC);
        indexB.Should().BeLessThan(indexD);
        indexC.Should().BeLessThan(indexD);
    }

    [Fact]
    public void DepthFirstSearch_Enumerate_WithMultipleSeeds_VisitsAll() {
        Dictionary<string, string[]> disconnected = new() {
            ["X"] = ["Y"],
            ["Y"] = [],
            ["P"] = ["Q"],
            ["Q"] = []
        };
        IEnumerable<string> GetNeighbors(string n) =>
            disconnected.TryGetValue(n, out string[]? neighbors) ? neighbors : [];

        List<string> visited = DepthFirstSearch.Enumerate(["X", "P"], GetNeighbors).ToList();

        visited.Should().HaveCount(4);
        visited.Should().Contain(["X", "Y", "P", "Q"]);
    }

    [Fact]
    public void BreadthFirstSearch_Enumerate_WithMultipleSeeds_VisitsAll() {
        Dictionary<string, string[]> disconnected = new() {
            ["X"] = ["Y"],
            ["Y"] = [],
            ["P"] = ["Q"],
            ["Q"] = []
        };
        IEnumerable<string> GetNeighbors(string n) =>
            disconnected.TryGetValue(n, out string[]? neighbors) ? neighbors : [];

        List<string> visited = BreadthFirstSearch.Enumerate(["X", "P"], GetNeighbors).ToList();

        visited.Should().HaveCount(4);
        visited.Should().Contain(["X", "Y", "P", "Q"]);
    }

    [Fact]
    public void DepthFirstSearch_Enumerate_DoesNotVisitNodeTwice() {
        List<string> visited = DepthFirstSearch.Enumerate("A", Neighbors).ToList();

        visited.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void DepthFirstSearch_Enumerate_RespectsPrePopulatedVisitedSet() {
        HashSet<string> visited = new() { "C" };

        List<string> result = DepthFirstSearch.Enumerate("A", Neighbors, visited).ToList();

        result.Should().Contain(["A", "B", "D"]);
        result.Should().NotContain("C");
    }

    [Fact]
    public void CanReach_ReturnsTrueForReachableTarget() {
        bool result = DepthFirstSearch.CanReach("A", "D", Neighbors);

        result.Should().BeTrue();
    }

    [Fact]
    public void CanReach_ReturnsFalseForUnreachableTarget() {
        bool result = DepthFirstSearch.CanReach("D", "A", Neighbors);

        result.Should().BeFalse();
    }

    [Fact]
    public void CanReach_ReturnsTrueForSelf() {
        bool result = DepthFirstSearch.CanReach("A", "A", Neighbors);

        result.Should().BeTrue();
    }

    [Fact]
    public void DepthFirstSearch_Enumerate_HandlesIsolatedNode() {
        List<string> visited = DepthFirstSearch.Enumerate("D", Neighbors).ToList();

        visited.Should().ContainSingle().Which.Should().Be("D");
    }

    [Fact]
    public void DepthFirstSearch_Enumerate_HandlesCycle() {
        Dictionary<string, string[]> cyclic = new() {
            ["A"] = ["B"],
            ["B"] = ["C"],
            ["C"] = ["A"]
        };
        IEnumerable<string> GetNeighbors(string n) =>
            cyclic.TryGetValue(n, out string[]? neighbors) ? neighbors : [];

        List<string> visited = DepthFirstSearch.Enumerate("A", GetNeighbors).ToList();

        visited.Should().HaveCount(3);
        visited.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void StronglyConnectedComponents_Find_GroupsCyclesAndSingletons() {
        Dictionary<string, string[]> graph = new() {
            ["A"] = ["B"],
            ["B"] = ["A", "C"],
            ["C"] = ["D"],
            ["D"] = ["C"],
            ["E"] = []
        };
        IEnumerable<string> GetNeighbors(string node) =>
            graph.TryGetValue(node, out string[]? neighbors) ? neighbors : [];

        Dictionary<string, int> componentByNode = StronglyConnectedComponents.Find(graph.Keys, GetNeighbors);

        componentByNode.Should().HaveCount(5);
        componentByNode["A"].Should().Be(componentByNode["B"]);
        componentByNode["C"].Should().Be(componentByNode["D"]);
        componentByNode["A"].Should().NotBe(componentByNode["C"]);
        componentByNode["E"].Should().NotBe(componentByNode["A"]);
        componentByNode["E"].Should().NotBe(componentByNode["C"]);
    }

    [Fact]
    public void StronglyConnectedComponents_Find_HandlesLargeLinearGraphWithoutRecursion() {
        const int nodeCount = 10_000;
        int[] nodes = Enumerable.Range(0, nodeCount).ToArray();
        static IEnumerable<int> GetNeighbors(int node) =>
            node + 1 < nodeCount ? [node + 1] : [];

        Dictionary<int, int> componentByNode = StronglyConnectedComponents.Find(nodes, GetNeighbors);

        componentByNode.Should().HaveCount(nodeCount);
        componentByNode.Values.Should().OnlyHaveUniqueItems();
    }
}
