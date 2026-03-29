namespace Spice86.Tests;

using FluentAssertions;

using Spice86.Shared.Collections;

using Xunit;

/// <summary>
/// Tests for <see cref="TimePriorityQueue{T}"/> and <see cref="TimePriorityQueueNode"/>.
/// </summary>
public class TimePriorityQueueTests {
    private sealed class TestNode : TimePriorityQueueNode {
        public string Label { get; }

        public TestNode(string label) {
            Label = label;
        }

        public override string ToString() => Label;
    }

    [Fact]
    public void NewQueue_IsEmpty() {
        TimePriorityQueue<TestNode> queue = new(10);

        queue.Count.Should().Be(0);
        queue.MaxSize.Should().Be(10);
    }

    [Fact]
    public void Enqueue_Dequeue_SingleNode() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode node = new("A");

        queue.Enqueue(node, 5.0);

        queue.Count.Should().Be(1);
        queue.First.Should().BeSameAs(node);

        TestNode dequeued = queue.Dequeue();
        dequeued.Should().BeSameAs(node);
        queue.Count.Should().Be(0);
        node.QueueIndex.Should().Be(-1);
    }

    [Fact]
    public void Enqueue_Dequeue_AscendingOrder() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");
        TestNode c = new("C");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 2.0);
        queue.Enqueue(c, 3.0);

        queue.Dequeue().Should().BeSameAs(a);
        queue.Dequeue().Should().BeSameAs(b);
        queue.Dequeue().Should().BeSameAs(c);
    }

    [Fact]
    public void Enqueue_Dequeue_DescendingOrder() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");
        TestNode c = new("C");

        queue.Enqueue(c, 3.0);
        queue.Enqueue(b, 2.0);
        queue.Enqueue(a, 1.0);

        queue.Dequeue().Should().BeSameAs(a);
        queue.Dequeue().Should().BeSameAs(b);
        queue.Dequeue().Should().BeSameAs(c);
    }

    [Fact]
    public void First_ReturnsLowestPriority() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");

        queue.Enqueue(b, 10.0);
        queue.Enqueue(a, 1.0);

        queue.First.Should().BeSameAs(a);
    }

    [Fact]
    public void First_ThrowsWhenEmpty() {
        TimePriorityQueue<TestNode> queue = new(10);

        Action act = () => _ = queue.First;
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Dequeue_ThrowsWhenEmpty() {
        TimePriorityQueue<TestNode> queue = new(10);

        Action act = () => queue.Dequeue();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Contains_ReturnsTrueWhenEnqueued() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode node = new("A");

        queue.Enqueue(node, 1.0);

        queue.Contains(node).Should().BeTrue();
    }

    [Fact]
    public void Contains_ReturnsFalseWhenNotEnqueued() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode node = new("A");

        queue.Contains(node).Should().BeFalse();
    }

    [Fact]
    public void Contains_ReturnsFalseAfterDequeue() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode node = new("A");

        queue.Enqueue(node, 1.0);
        queue.Dequeue();

        queue.Contains(node).Should().BeFalse();
    }

    [Fact]
    public void UpdatePriority_MovesNodeUp() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");

        queue.Enqueue(a, 5.0);
        queue.Enqueue(b, 10.0);

        // Move B ahead of A
        queue.UpdatePriority(b, 1.0);

        queue.First.Should().BeSameAs(b);
        queue.Dequeue().Should().BeSameAs(b);
        queue.Dequeue().Should().BeSameAs(a);
    }

    [Fact]
    public void UpdatePriority_MovesNodeDown() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 5.0);

        // Move A behind B
        queue.UpdatePriority(a, 10.0);

        queue.Dequeue().Should().BeSameAs(b);
        queue.Dequeue().Should().BeSameAs(a);
    }

    [Fact]
    public void UpdatePriority_ThrowsWhenNotInQueue() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode node = new("A");

        Action act = () => queue.UpdatePriority(node, 1.0);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Remove_RemovesNodeByValidIndex() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");
        TestNode c = new("C");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 2.0);
        queue.Enqueue(c, 3.0);

        bool removed = queue.Remove(b);

        removed.Should().BeTrue();
        queue.Count.Should().Be(2);
        queue.Contains(b).Should().BeFalse();
        b.QueueIndex.Should().Be(-1);

        // Remaining nodes should still dequeue in order.
        queue.Dequeue().Should().BeSameAs(a);
        queue.Dequeue().Should().BeSameAs(c);
    }

    [Fact]
    public void Remove_ReturnsFalseForNodeNotInQueue() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode node = new("A");

        queue.Remove(node).Should().BeFalse();
    }

    [Fact]
    public void Remove_RemovesFirstNode() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 2.0);

        queue.Remove(a).Should().BeTrue();
        queue.Count.Should().Be(1);
        queue.First.Should().BeSameAs(b);
    }

    [Fact]
    public void Remove_RemovesLastNode() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 2.0);

        queue.Remove(b).Should().BeTrue();
        queue.Count.Should().Be(1);
        queue.First.Should().BeSameAs(a);
    }

    [Fact]
    public void Remove_FallsBackToLinearScan_WhenIndexIsStale() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");
        TestNode c = new("C");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 2.0);
        queue.Enqueue(c, 3.0);

        // Corrupt b's QueueIndex to simulate staleness.
        b.QueueIndex = 99;

        bool removed = queue.Remove(b);
        removed.Should().BeTrue();
        queue.Count.Should().Be(2);
        queue.Contains(b).Should().BeFalse();
    }

    [Fact]
    public void Clear_ResetsQueue() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 2.0);

        queue.Clear();

        queue.Count.Should().Be(0);
        a.QueueIndex.Should().Be(-1);
        b.QueueIndex.Should().Be(-1);
        a.QueueOwner.Should().BeNull();
        b.QueueOwner.Should().BeNull();
    }

    [Fact]
    public void Resize_PreservesElements() {
        TimePriorityQueue<TestNode> queue = new(4);
        TestNode a = new("A");
        TestNode b = new("B");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 2.0);

        queue.Resize(20);

        queue.MaxSize.Should().Be(20);
        queue.Count.Should().Be(2);
        queue.Dequeue().Should().BeSameAs(a);
        queue.Dequeue().Should().BeSameAs(b);
    }

    [Fact]
    public void Resize_ThrowsWhenSmallerThanCount() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");
        TestNode c = new("C");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 2.0);
        queue.Enqueue(c, 3.0);

        Action act = () => queue.Resize(2);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Resize_ThrowsWhenZeroOrNegative() {
        TimePriorityQueue<TestNode> queue = new(10);

        Action act = () => queue.Resize(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Enqueue_ThrowsWhenFull() {
        TimePriorityQueue<TestNode> queue = new(2);
        queue.Enqueue(new TestNode("A"), 1.0);
        queue.Enqueue(new TestNode("B"), 2.0);

        Action act = () => queue.Enqueue(new TestNode("C"), 3.0);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Enqueue_ThrowsWhenNodeAlreadyEnqueued() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode node = new("A");

        queue.Enqueue(node, 1.0);

        Action act = () => queue.Enqueue(node, 2.0);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ManyItems_MaintainsCorrectOrder() {
        TimePriorityQueue<TestNode> queue = new(100);
        double[] priorities = { 50, 30, 70, 10, 90, 20, 80, 40, 60, 5 };
        TestNode[] nodes = new TestNode[priorities.Length];

        for (int i = 0; i < priorities.Length; i++) {
            nodes[i] = new TestNode($"N{i}");
            queue.Enqueue(nodes[i], priorities[i]);
        }

        Array.Sort(priorities);
        for (int i = 0; i < priorities.Length; i++) {
            TestNode dequeued = queue.Dequeue();
            dequeued.Priority.Should().Be(priorities[i]);
        }
    }

    [Fact]
    public void Remove_MiddleNode_MaintainsHeapProperty() {
        TimePriorityQueue<TestNode> queue = new(100);
        TestNode[] nodes = new TestNode[10];
        for (int i = 0; i < 10; i++) {
            nodes[i] = new TestNode($"N{i}");
            queue.Enqueue(nodes[i], i * 10.0);
        }

        // Remove node with priority 50 (index 5).
        queue.Remove(nodes[5]).Should().BeTrue();

        // Verify remaining nodes dequeue in order.
        double lastPriority = double.MinValue;
        while (queue.Count > 0) {
            TestNode dequeued = queue.Dequeue();
            dequeued.Priority.Should().BeGreaterThanOrEqualTo(lastPriority);
            lastPriority = dequeued.Priority;
        }
    }

    [Fact]
    public void NodeReuse_AfterDequeue_CanBeReenqueued() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode node = new("A");

        queue.Enqueue(node, 5.0);
        queue.Dequeue();

        // Re-enqueue with a different priority.
        queue.Enqueue(node, 1.0);

        queue.Count.Should().Be(1);
        queue.First.Should().BeSameAs(node);
        node.Priority.Should().Be(1.0);
    }

    [Fact]
    public void NodeReuse_AfterRemove_CanBeReenqueued() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode node = new("A");

        queue.Enqueue(node, 5.0);
        queue.Remove(node);

        queue.Enqueue(node, 2.0);

        queue.Count.Should().Be(1);
        queue.First.Should().BeSameAs(node);
    }

    [Fact]
    public void Constructor_ThrowsOnInvalidCapacity() {
        Action act = () => new TimePriorityQueue<TestNode>(0);
        act.Should().Throw<ArgumentOutOfRangeException>();

        act = () => new TimePriorityQueue<TestNode>(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EqualPriorities_DoNotCorruptQueue() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");
        TestNode c = new("C");

        queue.Enqueue(a, 5.0);
        queue.Enqueue(b, 5.0);
        queue.Enqueue(c, 5.0);

        queue.Count.Should().Be(3);

        // All should dequeue without error; order among ties is implementation-defined.
        TestNode d1 = queue.Dequeue();
        TestNode d2 = queue.Dequeue();
        TestNode d3 = queue.Dequeue();

        new[] { d1, d2, d3 }.Should().BeEquivalentTo(new[] { a, b, c });
    }

    [Fact]
    public void DoublePrecisionPriorities_ArePreserved() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");

        // These two values would be identical if cast to float.
        double priorityA = 1000000.0001;
        double priorityB = 1000000.0002;

        queue.Enqueue(a, priorityA);
        queue.Enqueue(b, priorityB);

        queue.Dequeue().Should().BeSameAs(a);
        queue.Dequeue().Should().BeSameAs(b);
    }

    [Fact]
    public void NodeAt_ReturnsCorrectNode() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");
        TestNode c = new("C");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 2.0);
        queue.Enqueue(c, 3.0);

        // Index 1 is always the minimum-priority node.
        queue.NodeAt(1).Should().BeSameAs(a);
        // All nodes at indices 1..Count are non-null.
        for (int i = 1; i <= queue.Count; i++) {
            queue.NodeAt(i).Should().NotBeNull();
        }
    }

    [Fact]
    public void NodeAt_ThrowsWhenIndexBelowRange() {
        TimePriorityQueue<TestNode> queue = new(10);
        queue.Enqueue(new TestNode("A"), 1.0);

        Action act = () => queue.NodeAt(0);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NodeAt_ThrowsWhenIndexAboveCount() {
        TimePriorityQueue<TestNode> queue = new(10);
        queue.Enqueue(new TestNode("A"), 1.0);

        Action act = () => queue.NodeAt(2);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void NodeAt_ThrowsOnEmptyQueue() {
        TimePriorityQueue<TestNode> queue = new(10);

        Action act = () => queue.NodeAt(1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Remove_SingleElement_EmptiesQueue() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode node = new("A");

        queue.Enqueue(node, 5.0);
        bool removed = queue.Remove(node);

        removed.Should().BeTrue();
        queue.Count.Should().Be(0);
        node.QueueIndex.Should().Be(-1);
        node.QueueOwner.Should().BeNull();
    }

    [Fact]
    public void Clear_OnEmptyQueue_DoesNotThrow() {
        TimePriorityQueue<TestNode> queue = new(10);

        Action act = () => queue.Clear();
        act.Should().NotThrow();
        queue.Count.Should().Be(0);
    }

    [Fact]
    public void UpdatePriority_SamePriority_NoChange() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 2.0);

        // Updating A with its current priority should leave order unchanged.
        queue.UpdatePriority(a, 1.0);

        queue.Dequeue().Should().BeSameAs(a);
        queue.Dequeue().Should().BeSameAs(b);
    }

    [Fact]
    public void Resize_ToSameSize_Succeeds() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        queue.Enqueue(a, 1.0);

        queue.Resize(10);

        queue.MaxSize.Should().Be(10);
        queue.Count.Should().Be(1);
        queue.Dequeue().Should().BeSameAs(a);
    }

    [Fact]
    public void Contains_NodeInDifferentQueue_ReturnsFalse() {
        TimePriorityQueue<TestNode> queueA = new(10);
        TimePriorityQueue<TestNode> queueB = new(10);
        TestNode node = new("A");

        queueA.Enqueue(node, 1.0);

        queueB.Contains(node).Should().BeFalse();
    }

    [Fact]
    public void Enqueue_AfterClear_CanReenqueueNodes() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");

        queue.Enqueue(a, 1.0);
        queue.Enqueue(b, 2.0);
        queue.Clear();

        // Both nodes should be re-enqueueable after Clear.
        queue.Enqueue(a, 3.0);
        queue.Enqueue(b, 1.0);

        queue.Count.Should().Be(2);
        queue.Dequeue().Should().BeSameAs(b);
        queue.Dequeue().Should().BeSameAs(a);
    }

    [Fact]
    public void NegativePriorities_OrderCorrectly() {
        TimePriorityQueue<TestNode> queue = new(10);
        TestNode a = new("A");
        TestNode b = new("B");
        TestNode c = new("C");

        queue.Enqueue(a, -10.0);
        queue.Enqueue(b, 0.0);
        queue.Enqueue(c, -5.0);

        queue.Dequeue().Should().BeSameAs(a);
        queue.Dequeue().Should().BeSameAs(c);
        queue.Dequeue().Should().BeSameAs(b);
    }

    [Fact]
    public void Resize_ThrowsWhenNegative() {
        TimePriorityQueue<TestNode> queue = new(10);

        Action act = () => queue.Resize(-1);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
