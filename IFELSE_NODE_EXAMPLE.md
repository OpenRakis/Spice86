# IfElseNode Usage Example

This document demonstrates how to use the new `IfElseNode` for implementing conditional logic in the CfgCpu AST, such as for the BsfRm instruction.

## Example: BsfRm (Bit Scan Forward)

The BsfRm instruction scans a value for the first set bit. If a bit is found, it stores the bit index in the destination and sets ZF=0. If no bit is found, ZF=1 and the destination is undefined.

```csharp
public override IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
    DataType type = builder.UType(Size);

    // Get the source operand (Rm)
    ValueNode sourceNode = builder.ModRm.RmToNode(type, ModRmContext);

    // Build condition: source != 0
    ValueNode zeroNode = builder.Constant(type, 0);
    ValueNode conditionNode = new BinaryOperationNode(
        DataType.BOOL,
        sourceNode,
        BinaryOperation.NOT_EQUAL,
        zeroNode
    );

    // TRUE case: bit found
    // - Call helper to find bit position and store in destination
    // - Set ZF = false
    BlockNode trueCase = new BlockNode(
        builder.MethodCall("FindFirstSetBit", sourceNode),  // Returns bit position
        builder.Assign(builder.ModRm.RegToNode(type, ModRmContext), /* bit position result */),
        builder.Assign(builder.CpuFlag(Flags.Zero), builder.Constant(DataType.BOOL, 0))
    );

    // FALSE case: no bit found
    // - Set ZF = true
    // - Destination remains undefined (no assignment needed)
    BlockNode falseCase = new BlockNode(
        builder.Assign(builder.CpuFlag(Flags.Zero), builder.Constant(DataType.BOOL, 1))
    );

    // Create the if/else node
    IfElseNode ifElseNode = new IfElseNode(conditionNode, trueCase, falseCase);

    return builder.WithIpAdvancement(this, ifElseNode);
}
```

## Key Points

1. **Condition**: Must be a `ValueNode` that evaluates to a boolean type (`DataType.BOOL`)
2. **True/False Cases**: Both must be `BlockNode` instances containing the statements to execute
3. **Visitor Support**: The node is registered with both `AstExpressionBuilder` (generates `Expression.IfThenElse`) and `AstInstructionRenderer` (throws `NotSupportedException` as it's not assembly)
4. **Variable Scoping**: Variables declared in the true/false blocks are scoped to those blocks

## AST Structure

```
IfElseNode
├── Condition (ValueNode)
├── TrueCase (BlockNode)
│   └── Statements (IVisitableAstNode[])
└── FalseCase (BlockNode)
    └── Statements (IVisitableAstNode[])
```

## Implementation Details

- **File**: `Spice86.Core/Emulator/CPU/CfgCpu/Ast/Instruction/IfElseNode.cs`
- **Visitor Method**: `IAstVisitor<T>.VisitIfElseNode(IfElseNode node)`
- **Expression Builder**: Generates `Expression.IfThenElse(condition, trueBlock, falseBlock)`
- **Renderer**: Not supported for assembly rendering (throws `NotSupportedException`)
