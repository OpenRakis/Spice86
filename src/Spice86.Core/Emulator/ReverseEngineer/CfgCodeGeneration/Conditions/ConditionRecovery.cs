namespace Spice86.Core.Emulator.ReverseEngineer.CfgCodeGeneration.Conditions;

using Spice86.Core.Emulator.CPU.CfgCpu.Ast;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Builder;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Operations;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value;
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Value.Constant;

/// <summary>
/// Rewrites the flag condition of a <c>jcc</c> as a relation between the operands of the <c>cmp</c> or
/// <c>test</c> right before it, when the relation is exact for the emulator's ALU. Also rewrites E / NE / S / NS
/// after an ALU operation as a test of the assigned result.
/// </summary>
internal static class ConditionRecovery {
    private static readonly AstBuilder Builder = new();

    /// <summary>
    /// The relation equivalent to <paramref name="jumpCondition"/> evaluated after <paramref name="setterAst"/>,
    /// or <c>null</c> when <paramref name="setterAst"/> is not a <c>cmp</c> / <c>test</c> or the condition has
    /// no equivalent relation. Each operand appears once in the result.
    /// </summary>
    public static ValueNode? TryRecover(IVisitableAstNode setterAst, ValueNode jumpCondition) {
        Setter? setter = MatchSetter(setterAst);
        if (setter is null) {
            return null;
        }
        int? conditionCode = ConditionCodeOf(jumpCondition);
        if (conditionCode is null) {
            return null;
        }
        if (setter.Operation == "Sub") {
            return CompareCondition(conditionCode.Value, setter.Left, setter.Right);
        }
        return TestCondition(conditionCode.Value, setter.Left, setter.Right);
    }

    /// <summary>
    /// The test of the assigned result equivalent to <paramref name="jumpCondition"/> evaluated right after
    /// <paramref name="setterAst"/>, or <c>null</c>. Only for an ALU operation that sets the zero and sign flags
    /// from its result, a general-purpose register destination, and E / NE / S / NS.
    /// </summary>
    public static ValueNode? TryRecoverFromResult(IVisitableAstNode setterAst, ValueNode jumpCondition) {
        RegisterNode? destination = MatchResultSetter(setterAst);
        if (destination is null) {
            return null;
        }
        return ConditionCodeOf(jumpCondition) switch {
            4 => Relation(destination, BinaryOperation.EQUAL, Zero(destination)),
            5 => Relation(destination, BinaryOperation.NOT_EQUAL, Zero(destination)),
            8 => SignTest(destination, BinaryOperation.LESS_THAN),
            9 => SignTest(destination, BinaryOperation.GREATER_THAN_OR_EQUAL),
            _ => null
        };
    }

    private static RegisterNode? MatchResultSetter(IVisitableAstNode setterAst) {
        if (setterAst is not BlockNode { Statements: [BinaryOperationNode { BinaryOperation: BinaryOperation.ASSIGN } assignment, MoveIpNextNode] }) {
            return null;
        }
        if (assignment.Left is not RegisterNode destination || destination is SegmentRegisterNode) {
            return null;
        }
        if (assignment.Right is not MethodCallValueNode {
                CallNode: {
                    PropertyPath: "Alu8" or "Alu16" or "Alu32",
                    MethodName: "Add" or "Adc" or "Sub" or "Sbb" or "And" or "Or" or "Xor" or "Inc" or "Dec"
                }
            }) {
            return null;
        }
        return destination;
    }

    private static Setter? MatchSetter(IVisitableAstNode setterAst) {
        if (setterAst is not BlockNode { Statements: [MethodCallValueNode call, MoveIpNextNode] }) {
            return null;
        }
        MethodCallNode callNode = call.CallNode;
        if (callNode.PropertyPath is not ("Alu8" or "Alu16" or "Alu32") || callNode.MethodName is not ("Sub" or "And")) {
            return null;
        }
        if (callNode.Arguments is not [ValueNode left, ValueNode right]) {
            return null;
        }
        return new Setter(callNode.MethodName, left, right);
    }

    private static int? ConditionCodeOf(ValueNode condition) {
        for (int code = 0; code < 16; code++) {
            if (Builder.Flag.BuildSetCondition(code).Equals(condition)) {
                return code;
            }
        }
        return null;
    }

    private static ValueNode? CompareCondition(int code, ValueNode left, ValueNode right) => code switch {
        2 => Relation(left, BinaryOperation.LESS_THAN, right),
        3 => Relation(left, BinaryOperation.GREATER_THAN_OR_EQUAL, right),
        4 => Relation(left, BinaryOperation.EQUAL, right),
        5 => Relation(left, BinaryOperation.NOT_EQUAL, right),
        6 => Relation(left, BinaryOperation.LESS_THAN_OR_EQUAL, right),
        7 => Relation(left, BinaryOperation.GREATER_THAN, right),
        8 => SignTest(Difference(left, right), BinaryOperation.LESS_THAN),
        9 => SignTest(Difference(left, right), BinaryOperation.GREATER_THAN_OR_EQUAL),
        12 => Relation(Signed(left), BinaryOperation.LESS_THAN, Signed(right)),
        13 => Relation(Signed(left), BinaryOperation.GREATER_THAN_OR_EQUAL, Signed(right)),
        14 => Relation(Signed(left), BinaryOperation.LESS_THAN_OR_EQUAL, Signed(right)),
        15 => Relation(Signed(left), BinaryOperation.GREATER_THAN, Signed(right)),
        _ => null
    };

    private static ValueNode? TestCondition(int code, ValueNode left, ValueNode right) => code switch {
        4 => Relation(Masked(left, right), BinaryOperation.EQUAL, Zero(left)),
        5 => Relation(Masked(left, right), BinaryOperation.NOT_EQUAL, Zero(left)),
        8 => SignTest(Masked(left, right), BinaryOperation.LESS_THAN),
        9 => SignTest(Masked(left, right), BinaryOperation.GREATER_THAN_OR_EQUAL),
        _ => null
    };

    private static ValueNode Masked(ValueNode left, ValueNode right) {
        if (left is RegisterNode && left.Equals(right)) {
            return left;
        }
        return new BinaryOperationNode(left.DataType, left, BinaryOperation.BITWISE_AND, right);
    }

    private static BinaryOperationNode Relation(ValueNode left, BinaryOperation operation, ValueNode right) =>
        new(DataType.BOOL, left, operation, right);

    private static BinaryOperationNode Difference(ValueNode left, ValueNode right) =>
        new(left.DataType, left, BinaryOperation.MINUS, right);

    private static BinaryOperationNode SignTest(ValueNode value, BinaryOperation operation) =>
        Relation(Signed(value), operation, new ConstantNode(DataType.SignedFromBitWidth(value.DataType.BitWidth), 0));

    private static TypeConversionNode Signed(ValueNode value) =>
        new(DataType.SignedFromBitWidth(value.DataType.BitWidth), value);

    private static ConstantNode Zero(ValueNode value) => new(value.DataType, 0);

    private sealed record Setter(string Operation, ValueNode Left, ValueNode Right);
}
