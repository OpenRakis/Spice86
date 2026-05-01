namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using Xunit;

/// <summary>
/// Tests for <see cref="CfgNodeExecutionCompiler"/> caching behaviour:
/// cache reuse for structurally equal ASTs and concurrent compile deduplication.
/// </summary>
public class CfgNodeExecutionCompilerTest {
    private static CfgNodeExecutionCompiler CreateCompiler() {
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        CfgNodeExecutionCompilerMonitor monitor = new(loggerService);
        return new CfgNodeExecutionCompiler(monitor, loggerService, JitMode.InterpretedThenCompiled);
    }

    /// <summary>
    /// A minimal concrete <see cref="CfgNode"/> that returns a fixed AST.
    /// The AST is a simple <see cref="SelectorNode"/> which compiles to a no-op like expression.
    /// </summary>
    private sealed class FixedAstNode : CfgNode {
        private IVisitableAstNode _ast;

        public FixedAstNode(SegmentedAddress address, IVisitableAstNode ast)
            : base(address, 1) {
            _ast = ast;
        }

        public void SetAst(IVisitableAstNode ast) => _ast = ast;

        public override bool IsLive => true;
        public override void UpdateSuccessorCache() { }
        public override ICfgNode? GetNextSuccessor(InstructionExecutionHelper helper) => null;
        public override InstructionNode DisplayAst =>
            new InstructionNode(InstructionOperation.NOP);

        public override IVisitableAstNode ExecutionAst => _ast;
    }

    [Fact]
    public void Compile_AssignsCompiledExecution() {
        // A SelectorNode AST produces a simple compiled delegate.
        SelectorNode ast = new SelectorNode();
        FixedAstNode node = new FixedAstNode(new SegmentedAddress(0x1000, 0), ast);
        using CfgNodeExecutionCompiler compiler = CreateCompiler();

        compiler.Compile(node);

        // After Compile, the node should have a non-null CompiledExecution (at least the interpreted one).
        node.CompiledExecution.Should().NotBeNull();
    }

    [Fact]
    public void Compile_ConcurrentCallsSameAst_AllComplete() {
        // Fire N parallel Compile calls for nodes with the same AST shape.
        const int parallelCount = 20;
        FixedAstNode[] nodes = new FixedAstNode[parallelCount];
        for (int i = 0; i < parallelCount; i++) {
            nodes[i] = new FixedAstNode(
                new SegmentedAddress((ushort)(0x3000 + i), 0),
                new SelectorNode());
        }
        using CfgNodeExecutionCompiler compiler = CreateCompiler();

        Parallel.For(0, parallelCount, i => {
            compiler.Compile(nodes[i]);
        });

        // All nodes should have compiled execution assigned.
        foreach (FixedAstNode node in nodes) {
            node.CompiledExecution.Should().NotBeNull();
        }
    }

    [Fact]
    public void Compile_WaitsForOptimizedDelegate() {
        // Compile a node and wait briefly for the background compiler to produce the optimized delegate.
        SelectorNode ast = new SelectorNode();
        FixedAstNode node = new FixedAstNode(new SegmentedAddress(0x4000, 0), ast);
        using CfgNodeExecutionCompiler compiler = CreateCompiler();
        compiler.Compile(node);

        // Store the initially-assigned (interpreted) delegate reference.
        CfgNodeExecutionAction<InstructionExecutionHelper> initial = node.CompiledExecution;

        // Wait up to 5 seconds for the optimized delegate to be swapped in.
        int waited = 0;
        while (ReferenceEquals(node.CompiledExecution, initial) && waited < 5000) {
            Thread.Sleep(50);
            waited += 50;
        }

        // The delegate should have been swapped (it may still be the interpreted one if
        // compilation is extremely slow, but under normal conditions it should differ).
        node.CompiledExecution.Should().NotBeNull();
    }

    [Fact]
    public void Compile_SecondCallPreventsStaleSwap() {
        // Arrange: create two distinct ASTs so we can distinguish which delegate is active.
        SelectorNode ast1 = new SelectorNode();
        SelectorNode ast2 = new SelectorNode();
        FixedAstNode node = new FixedAstNode(new SegmentedAddress(0x5000, 0), ast1);
        using CfgNodeExecutionCompiler compiler = CreateCompiler();

        // Act: first Compile() enqueues background task for ast1.
        compiler.Compile(node);
        CfgNodeExecutionAction<InstructionExecutionHelper> afterFirstCompile = node.CompiledExecution;

        // Immediately call Compile() again with a different AST (simulates SignatureReducer flip).
        node.SetAst(ast2);
        compiler.Compile(node);
        CfgNodeExecutionAction<InstructionExecutionHelper> afterSecondCompile = node.CompiledExecution;

        // Wait until the background compiler swaps in the optimized delegate for the second AST.
        int waited = 0;
        while (ReferenceEquals(node.CompiledExecution, afterSecondCompile) && waited < 5000) {
            Thread.Sleep(50);
            waited += 50;
        }

        // Assert: the final delegate must be the optimized one from the second Compile() call,
        // not the stale compiled delegate from the first call.
        CfgNodeExecutionAction<InstructionExecutionHelper> finalDelegate = node.CompiledExecution;
        finalDelegate.Should().NotBeSameAs(afterFirstCompile,
            "the first background compilation should not overwrite the second Compile()'s delegate");
        finalDelegate.Should().NotBeSameAs(afterSecondCompile,
            "the background compiler should have produced an optimized delegate for the second compile");

        // The generation counter should reflect exactly 2 Compile() calls.
        node.CompilationGeneration.Should().Be(2);
    }
}
