namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Shared.Emulator.Memory;

using Xunit;

/// <summary>
/// Tests that <see cref="CfgNode"/> caches the execution AST and that
/// <see cref="CfgNode.InvalidateExecutionAstCache"/> forces a rebuild.
/// </summary>
public class CfgNodeAstCacheTests {
    /// <summary>
    /// A minimal concrete CfgNode for testing the AST cache.
    /// Counts how many times BuildExecutionAst is called.
    /// </summary>
    private sealed class TestCfgNode : CfgNode {
        private readonly IVisitableAstNode _astToReturn;
        public int BuildCallCount { get; private set; }

        public TestCfgNode(IVisitableAstNode astToReturn)
            : base(new SegmentedAddress(0, 0), 1) {
            _astToReturn = astToReturn;
        }

        public override bool IsLive => true;
        public override void UpdateSuccessorCache() { }
        public override ICfgNode? GetNextSuccessor(InstructionExecutionHelper helper) => null;
        public override InstructionNode ToInstructionAst(AstBuilder builder) =>
            new InstructionNode(InstructionOperation.NOP);

        protected override IVisitableAstNode BuildExecutionAst(AstBuilder builder) {
            BuildCallCount++;
            return _astToReturn;
        }
    }

    [Fact]
    public void GenerateExecutionAst_CachesResult_BuildCalledOnce() {
        IVisitableAstNode expectedAst = new BlockNode(new MethodCallNode(null, "Nop"));
        TestCfgNode node = new TestCfgNode(expectedAst);
        AstBuilder builder = new();

        IVisitableAstNode first = node.GenerateExecutionAst(builder);
        IVisitableAstNode second = node.GenerateExecutionAst(builder);

        first.Should().BeSameAs(second, "second call should return cached instance");
        node.BuildCallCount.Should().Be(1, "BuildExecutionAst should be called exactly once");
    }

    [Fact]
    public void InvalidateExecutionAstCache_ClearsCache_RebuildOnNextCall() {
        IVisitableAstNode expectedAst = new BlockNode(new MethodCallNode(null, "Nop"));
        TestCfgNode node = new TestCfgNode(expectedAst);
        AstBuilder builder = new();

        IVisitableAstNode first = node.GenerateExecutionAst(builder);
        node.BuildCallCount.Should().Be(1);

        node.InvalidateExecutionAstCache();

        IVisitableAstNode second = node.GenerateExecutionAst(builder);
        node.BuildCallCount.Should().Be(2, "BuildExecutionAst should be called again after invalidation");
        second.Should().BeSameAs(expectedAst);
    }

    [Fact]
    public void InvalidateExecutionAstCache_IsIdempotent() {
        IVisitableAstNode expectedAst = new BlockNode(new MethodCallNode(null, "Nop"));
        TestCfgNode node = new TestCfgNode(expectedAst);
        AstBuilder builder = new();

        node.GenerateExecutionAst(builder);
        node.InvalidateExecutionAstCache();
        node.InvalidateExecutionAstCache(); // second invalidation should be harmless

        IVisitableAstNode result = node.GenerateExecutionAst(builder);
        node.BuildCallCount.Should().Be(2);
        result.Should().BeSameAs(expectedAst);
    }
}
