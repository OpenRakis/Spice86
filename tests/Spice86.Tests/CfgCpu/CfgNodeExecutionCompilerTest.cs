namespace Spice86.Tests.CfgCpu;

using FluentAssertions;

using NSubstitute;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.ControlFlowGraph;
using Spice86.Shared.Utils;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor;
using Spice86.Core.Emulator.CPU.CfgCpu.InstructionExecutor.Expressions;
using Spice86.Shared.Emulator.Memory;
using Spice86.Shared.Interfaces;

using System.Linq.Expressions;
using System.Threading;

using Xunit;

using CfgSelectorNode = Spice86.Core.Emulator.CPU.CfgCpu.ParsedInstruction.SelfModifying.SelectorNode;

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
        private static readonly SequentialIdAllocator _allocator = new();
        private IVisitableAstNode _ast;

        public FixedAstNode(SegmentedAddress address, IVisitableAstNode ast)
            : base(_allocator.AllocateId(), address, 1) {
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

    private sealed class ExpressionAstNode : IVisitableAstNode {
        private readonly Expression _expression;

        public ExpressionAstNode(Expression expression) {
            _expression = expression;
        }

        public T Accept<T>(IAstVisitor<T> astVisitor) {
            if (astVisitor is AstExpressionBuilder) {
                return (T)(object)_expression;
            }
            throw new InvalidOperationException($"Unsupported visitor {astVisitor.GetType().Name}");
        }
    }

    [Fact]
    public void Compile_AssignsCompiledExecution() {
        // A SelectorNode AST produces a simple compiled delegate.
        SelectorNode ast = new SelectorNode(new CfgSelectorNode(0, new SegmentedAddress(0x1000, 0)));
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
                new SelectorNode(new CfgSelectorNode(0, new SegmentedAddress((ushort)(0x3000 + i), 0))));
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
    public void Compile_SecondCallPreventsStaleSwap() {
        // Arrange: create two distinct ASTs so we can distinguish which delegate is active.
        IVisitableAstNode ast1 = CreateAssignmentAst(10_000);
        IVisitableAstNode ast2 = CreateAssignmentAst(1);
        FixedAstNode node = new FixedAstNode(new SegmentedAddress(0x5000, 0), ast1);
        // Inline monitor creation so we can observe TotalSwapped for reliable synchronisation.
        ILoggerService loggerService = Substitute.For<ILoggerService>();
        CfgNodeExecutionCompilerMonitor monitor = new(loggerService);
        using CfgNodeExecutionCompiler compiler = new(monitor, loggerService, JitMode.InterpretedThenCompiled);

        // Act: first Compile() enqueues background task for ast1.
        compiler.Compile(node);
        CfgNodeExecutionAction<InstructionExecutionHelper> afterFirstCompile = node.CompiledExecution;

        // Immediately call Compile() again with a different AST (simulates SignatureReducer flip).
        node.SetAst(ast2);
        compiler.Compile(node);
        CfgNodeExecutionAction<InstructionExecutionHelper> afterSecondCompile = node.CompiledExecution;

        SpinWait.SpinUntil(() => monitor.TotalSwapped >= 1, TimeSpan.FromSeconds(30)).Should().BeTrue(
            "the second background compilation should complete and swap the delegate within 30 seconds");

        // Assert: the final delegate must be the optimised one from the second Compile() call.
        // Use ReferenceEquals booleans rather than NotBeSameAs to avoid a FluentAssertions crash
        // when formatting compiled-expression delegates whose Method.DeclaringType is null.
        CfgNodeExecutionAction<InstructionExecutionHelper> finalDelegate = node.CompiledExecution;
        ReferenceEquals(finalDelegate, afterFirstCompile).Should().BeFalse(
            "the first background compilation should not overwrite the second Compile()'s delegate");
        ReferenceEquals(finalDelegate, afterSecondCompile).Should().BeFalse(
            "the background compiler should have produced an optimised delegate for the second compile");

        // The generation counter should reflect exactly 2 Compile() calls.
        node.CompilationGeneration.Should().Be(2);
    }

    private static IVisitableAstNode CreateAssignmentAst(int assignmentCount) {
        ParameterExpression marker = Expression.Variable(typeof(int), "marker");
        List<Expression> expressions = new(assignmentCount + 1);
        for (int i = 0; i < assignmentCount; i++) {
            expressions.Add(Expression.Assign(marker, Expression.Constant(i)));
        }
        expressions.Add(Expression.Empty());
        return new ExpressionAstNode(Expression.Block([marker], expressions));
    }
}
