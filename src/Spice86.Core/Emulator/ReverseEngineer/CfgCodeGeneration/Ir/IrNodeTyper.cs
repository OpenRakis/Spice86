namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ir;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Visitor;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Ast;
using Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Propagation;
using Spice86.Shared.Emulator.Memory;

using System.Linq;

/// <summary>
/// Rebuilds a lowered statement with every helper call kept as its typed call node, recursively in
/// arguments and operands, and renames the temporaries with the instruction's local suffix. ALU calls
/// become <see cref="AluOperationNode"/> / <see cref="AluStatementNode"/> with their flag effect. A
/// helper call that has no typed node makes the whole statement an <see cref="OpaqueNode"/>; an unknown
/// call inside a condition or switch value throws instead, since a branch cannot be opaque.
/// </summary>
internal static class IrNodeTyper {
    /// <summary>Types <paramref name="statement"/> without temporary renaming.</summary>
    public static IVisitableAstNode TypeStatement(IVisitableAstNode statement) => TypeStatement(statement, string.Empty);

    /// <summary>Types <paramref name="statement"/>, renaming temporaries with <paramref name="localSuffix"/>.
    /// Statement containers are retyped through <see cref="AstTrampoline"/> so deeply nested <c>if</c> /
    /// <c>else</c> chains cannot overflow the thread's stack.</summary>
    public static IVisitableAstNode TypeStatement(IVisitableAstNode statement, string localSuffix) {
        try {
            AstResult<IVisitableAstNode> result = new();
            AstTrampoline.Run(RetypeIt(statement, localSuffix, result));
            return result.Value;
        } catch (NotSupportedException) {
            return new OpaqueNode(statement);
        }
    }

    private static IEnumerable<AstSubcall> RetypeIt(IVisitableAstNode node, string localSuffix,
        AstResult<IVisitableAstNode> result) {
        switch (node) {
            case OpaqueNode opaque: {
                AstResult<IVisitableAstNode> inner = new();
                yield return AstTrampoline.Call(RetypeIt(opaque.Original, localSuffix, inner));
                result.Value = IrAstChildren.MapChildren(opaque, _ => inner.Value);
                yield break;
            }
            case BlockNode or IfElseNode or PreTestLoopNode or WhileNode or WhileWithExitNode
                or DoWhileNode or InfiniteLoopNode or SwitchNode or CpuFaultTryCatchNode
                or InstructionBoundaryNode: {
                Dictionary<IVisitableAstNode, IVisitableAstNode> mapped =
                    new(ReferenceEqualityComparer.Instance);
                foreach (IVisitableAstNode child in IrAstChildren.ChildrenOf(node)) {
                    AstResult<IVisitableAstNode> childResult = new();
                    yield return AstTrampoline.Call(RetypeIt(child, localSuffix, childResult));
                    mapped[child] = childResult.Value;
                }
                result.Value = IrAstChildren.MapChildren(node, child => mapped[child]);
                yield break;
            }
            default:
                result.Value = Retype(node, localSuffix);
                yield break;
        }
    }

    /// <summary>Types a condition or switch <paramref name="value"/>.</summary>
    /// <exception cref="NotSupportedException">A call in the value has no typed node.</exception>
    public static ValueNode TypeValue(ValueNode value) => TypeValue(value, string.Empty);

    /// <summary>Types a condition or switch <paramref name="value"/>, renaming temporaries with
    /// <paramref name="localSuffix"/>.</summary>
    /// <exception cref="NotSupportedException">A call in the value has no typed node.</exception>
    public static ValueNode TypeValue(ValueNode value, string localSuffix) =>
        (ValueNode)Retype(value, localSuffix);

    /// <summary>
    /// Renames the temporaries of <paramref name="node"/> with <paramref name="localSuffix"/>, without
    /// typing calls: the transfer of an <see cref="IrExit"/> keeps its untyped call nodes, only its variable
    /// names must be the renamed ones.
    /// </summary>
    public static IVisitableAstNode RenameTemporaries(IVisitableAstNode node, string localSuffix) =>
        localSuffix.Length == 0 ? node : Rename(node, localSuffix);

    private static IVisitableAstNode Retype(IVisitableAstNode node, string localSuffix) {
        switch (node) {
            case MethodCallNode call:
                return RetypeStatementCall(call, localSuffix);
            case MethodCallValueNode callValue:
                return RetypeValueCall(callValue, localSuffix);
            case VariableDeclarationNode declaration:
                IVisitableAstNode initializer = Retype(declaration.Initializer, localSuffix);
                return localSuffix.Length == 0 && ReferenceEquals(initializer, declaration.Initializer) ? node
                    : new VariableDeclarationNode(declaration.DataType,
                        Renamed(declaration.VariableName, localSuffix), (ValueNode)initializer);
            case VariableReferenceNode reference when node is not MethodScopedVariableReferenceNode:
                return localSuffix.Length == 0 ? node
                    : new VariableReferenceNode(reference.DataType,
                        Renamed(reference.VariableName, localSuffix));
            // The division pattern binds its own variable names; the originals must stay.
            case DivisionNode:
                return node;
            default:
                return IrAstChildren.MapChildren(node, child => Retype(child, localSuffix));
        }
    }

    private static IVisitableAstNode Rename(IVisitableAstNode node, string localSuffix) {
        switch (node) {
            case VariableDeclarationNode declaration:
                IVisitableAstNode initializer = Rename(declaration.Initializer, localSuffix);
                return ReferenceEquals(initializer, declaration.Initializer) ? node
                    : new VariableDeclarationNode(declaration.DataType,
                        Renamed(declaration.VariableName, localSuffix), (ValueNode)initializer);
            case VariableReferenceNode reference when node is not MethodScopedVariableReferenceNode:
                return new VariableReferenceNode(reference.DataType,
                    Renamed(reference.VariableName, localSuffix));
            // Untyped call arguments and the division pattern's bound names keep their spelling.
            case MethodCallNode or MethodCallValueNode or DivisionNode or OpaqueNode:
                return node;
            default:
                return IrAstChildren.MapChildren(node, child => Rename(child, localSuffix));
        }
    }

    private static string Renamed(string name, string localSuffix) =>
        localSuffix.Length == 0 ? name : $"{name}_{localSuffix}";

    private static IVisitableAstNode RetypeStatementCall(MethodCallNode call, string localSuffix) {
        IVisitableAstNode[] arguments = RetypedArguments(call, localSuffix);
        switch (call) {
            case AluCallNode alu:
                AluCallNode typedAlu = new(alu.Operation, alu.Width, arguments);
                return new AluStatementNode(typedAlu, AluFlags(alu.Operation, alu.Width, typedAlu),
                    alu.Operation, alu.Width);
            case StackCallNode { Operation: StackOperation.Push or StackOperation.Leave
                    or StackOperation.PushAll or StackOperation.PopAll }:
            case PortWriteCallNode or BoundCheckCallNode or ProcedureCallHelperNode
                or InterruptCallHelperNode or CallbackHelperNode:
                return call.WithArguments(arguments);
            default:
                throw UnknownCall(call);
        }
    }

    private static ValueNode RetypeValueCall(MethodCallValueNode call, string localSuffix) {
        IVisitableAstNode[] arguments = RetypedArguments(call.CallNode, localSuffix);
        switch (call) {
            case AluCallValueNode alu:
                AluCallValueNode typedAlu = new(call.DataType, alu.Operation, alu.Width, arguments);
                return new AluOperationNode(typedAlu, AluFlags(alu.Operation, alu.Width, typedAlu.CallNode),
                    alu.Operation, alu.Width);
            case StackCallValueNode { Operation: StackOperation.Pop or StackOperation.Peek }:
            case PortReadCallNode or BitScanCallNode or CallbackMovedCsIpHelperNode
                or SignatureMatchesHelperNode:
                return call.WithArguments(arguments);
            default:
                throw UnknownCall(call.CallNode);
        }
    }

    private static IVisitableAstNode[] RetypedArguments(MethodCallNode call, string localSuffix) {
        IVisitableAstNode[]? arguments = null;
        for (int i = 0; i < call.Arguments.Count; i++) {
            IVisitableAstNode argument = Retype(call.Arguments[i], localSuffix);
            if (!ReferenceEquals(argument, call.Arguments[i])) {
                arguments ??= [.. call.Arguments];
                arguments[i] = argument;
            }
        }
        return arguments ?? [.. call.Arguments];
    }

    private static FlagEffect AluFlags(AluOperation operation, BitWidth width, MethodCallNode call) {
        ulong? count = call.Arguments.Count > 0 ? ConstantEvaluation.Resolved(call.Arguments[^1])?.Value : null;
        if (AluFlagTable.TryGetEffect(operation, width, count, out FlagEffect effect)) {
            return effect;
        }
        throw new NotSupportedException($"Alu operation {operation} has no flag effect in the table.");
    }

    private static NotSupportedException UnknownCall(MethodCallNode call) =>
        new($"Helper call {(call.PropertyPath == null ? string.Empty : call.PropertyPath + ".")}{call.MethodName} has no typed node.");
}
