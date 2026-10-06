namespace Spice86.Tests.ReverseEngineer.Graph;

using FluentAssertions;
using Spice86.Core.Emulator.ReverseEngineer.Graph;
using Xunit;

public sealed class DepthFirstOrderingTest {
    private static DepthFirstOrdering<string> BuildTraversal(IEnumerable<string> roots, Dictionary<string, string[]> graph) {
        return new DepthFirstOrdering<string>(roots, node => graph.TryGetValue(node, out string[]? successors) ? successors : []);
    }

    [Fact]
    public void Chain_PostOrderIsReversedChain() {
        // Arrange
        Dictionary<string, string[]> graph = new() {
            ["A"] = ["B"],
            ["B"] = ["C"],
            ["C"] = []
        };
        string[] roots = ["A"];

        // Act
        DepthFirstOrdering<string> traversal = BuildTraversal(roots, graph);

        // Assert
        traversal.PostOrder.Should().Equal("C", "B", "A");
        traversal.ReversePostOrder.Should().Equal("A", "B", "C");
        traversal.RetreatingEdges.Should().BeEmpty();
    }

    [Fact]
    public void Diamond_JoinFinishesFirst() {
        // Arrange
        Dictionary<string, string[]> graph = new() {
            ["A"] = ["B", "C"],
            ["B"] = ["D"],
            ["C"] = ["D"],
            ["D"] = []
        };
        string[] roots = ["A"];

        // Act
        DepthFirstOrdering<string> traversal = BuildTraversal(roots, graph);

        // Assert
        traversal.PostOrder.Should().Equal("D", "B", "C", "A");
        traversal.ReversePostOrder.Should().Equal("A", "C", "B", "D");
        traversal.PostOrderIndexByNode["D"].Should().Be(0);
        traversal.RetreatingEdges.Should().BeEmpty();
    }

    [Fact]
    public void SuccessorOrder_LastExaminedSuccessorFollowsParent() {
        // Arrange
        Dictionary<string, string[]> graph1 = new() {
            ["A"] = ["B", "C"],
            ["B"] = [],
            ["C"] = []
        };
        string[] roots = ["A"];

        // Act
        DepthFirstOrdering<string> traversal1 = BuildTraversal(roots, graph1);

        // Assert
        traversal1.ReversePostOrder.Should().Equal("A", "C", "B");

        // Arrange
        Dictionary<string, string[]> graph2 = new() {
            ["A"] = ["C", "B"],
            ["B"] = [],
            ["C"] = []
        };

        // Act
        DepthFirstOrdering<string> traversal2 = BuildTraversal(roots, graph2);

        // Assert
        traversal2.ReversePostOrder.Should().Equal("A", "B", "C");
    }

    [Fact]
    public void SelfLoop_IsOneRetreatingEdge() {
        // Arrange
        Dictionary<string, string[]> graph = new() {
            ["A"] = ["A"]
        };
        string[] roots = ["A"];

        // Act
        DepthFirstOrdering<string> traversal = BuildTraversal(roots, graph);

        // Assert
        traversal.RetreatingEdges.Should().Equal(new GraphEdge<string>("A", "A"));
        traversal.PostOrder.Should().Equal("A");
    }

    [Fact]
    public void NaturalLoop_RetreatingEdgeGoesFromLatchToHeader() {
        // Arrange
        Dictionary<string, string[]> graph = new() {
            ["A"] = ["B"],
            ["B"] = ["C"],
            ["C"] = ["B", "D"],
            ["D"] = []
        };
        string[] roots = ["A"];

        // Act
        DepthFirstOrdering<string> traversal = BuildTraversal(roots, graph);

        // Assert
        traversal.RetreatingEdges.Should().Equal(new GraphEdge<string>("C", "B"));
        traversal.PostOrder.Should().Equal("D", "C", "B", "A");
    }

    [Fact]
    public void IrreducibleLoop_HasOneRetreatingEdgeInsideTheCycle() {
        // Arrange
        Dictionary<string, string[]> graph = new() {
            ["A"] = ["B", "C"],
            ["B"] = ["C"],
            ["C"] = ["B"]
        };
        string[] roots = ["A"];

        // Act
        DepthFirstOrdering<string> traversal = BuildTraversal(roots, graph);

        // Assert
        traversal.RetreatingEdges.Should().HaveCount(1);
        traversal.RetreatingEdges[0].Source.Should().BeOneOf("B", "C");
        traversal.RetreatingEdges[0].Target.Should().BeOneOf("B", "C");
        traversal.RetreatingEdges[0].Source.Should().NotBe(traversal.RetreatingEdges[0].Target);
    }

    [Fact]
    public void TwoRoots_TreesKeepRootOrder() {
        // Arrange
        Dictionary<string, string[]> graph = new() {
            ["X"] = ["Y"],
            ["Y"] = [],
            ["A"] = ["B"],
            ["B"] = []
        };
        string[] roots = ["X", "A"];

        // Act
        DepthFirstOrdering<string> traversal = BuildTraversal(roots, graph);

        // Assert
        traversal.PostOrder.Should().Equal("Y", "X", "B", "A");
        traversal.ReversePostOrder.Should().Equal("X", "Y", "A", "B");
    }

    [Fact]
    public void RootAlreadyReached_IsNotRestarted() {
        // Arrange
        Dictionary<string, string[]> graph = new() {
            ["A"] = ["B"],
            ["B"] = []
        };
        string[] roots = ["A", "B", "A"];

        // Act
        DepthFirstOrdering<string> traversal = BuildTraversal(roots, graph);

        // Assert
        traversal.PostOrder.Should().Equal("B", "A");
        traversal.ReversePostOrder.Should().Equal("A", "B");
    }

    [Fact]
    public void UnreachedNode_IsNotContained() {
        // Arrange
        Dictionary<string, string[]> graph = new() {
            ["A"] = ["B"],
            ["B"] = [],
            ["C"] = ["A"]
        };
        string[] roots = ["A"];

        // Act
        DepthFirstOrdering<string> traversal = BuildTraversal(roots, graph);

        // Assert
        traversal.Contains("B").Should().BeTrue();
        traversal.Contains("C").Should().BeFalse();
        traversal.PostOrderIndexByNode.Should().NotContainKey("C");
    }

    [Fact]
    public void LongChain_DoesNotOverflowTheStack() {
        // Arrange & Act
        DepthFirstOrdering<int> traversal = new(
            [0],
            node => node < 99999 ? [node + 1] : []);

        // Assert
        traversal.PostOrder.Count.Should().Be(100000);
        traversal.PostOrder[0].Should().Be(99999);
        traversal.ReversePostOrder[0].Should().Be(0);
    }
}