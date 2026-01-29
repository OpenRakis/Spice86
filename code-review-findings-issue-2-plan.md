# Implementation Plan: Fix Issue #2 - Missing MoveIpToEndOfInstruction in AST Execution

## Summary

Add `MoveIpNextNode` to `OpRegRm.mixin`'s `GenerateExecutionAst()` method to include IP advancement in the generated AST. This fixes the critical issue where AST-based instruction execution would not advance the instruction pointer, causing programs to hang or execute incorrect code.

**Approach:** Wrap instruction logic + IP advancement in a `BlockNode` (Option A from code review)

**Scope:** Fix the single existing `GenerateExecutionAst()` implementation as proof of concept. Document the pattern for future rollout to 88 other instruction types.

---

## Context

Issue #2 in [code-review-findings.md](code-review-findings.md) identifies that the `GenerateExecutionAst()` method in instruction mixins (like `OpRegRm.mixin`) does not include the call to `MoveIpToEndOfInstruction()` that appears in the corresponding `Execute()` method.

### Current State

- The `Execute()` method calls `helper.MoveIpToEndOfInstruction(this)` after executing the instruction logic
- The `GenerateExecutionAst()` method only generates AST for the instruction's core logic (e.g., ALU operations, assignments)
- The IP advancement is missing from the AST, which would cause instructions to not advance when AST execution is enabled

### What Has Been Done

1. Control flow AST nodes have been created under [Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow](src/Spice86.Core/Emulator/CPU/CfgCpu/Ast/Instruction/ControlFlow):
   - `MoveIpNextNode` - represents moving IP to a next value
   - `CfgInstructionNode` - base class for control flow nodes that hold a reference to the instruction
   - Other control flow nodes: `CallNearNode`, `CallFarNode`, `ReturnNearNode`, `ReturnFarNode`, `JumpNearNode`, `JumpFarNode`, `InterruptCallNode`, `ReturnInterruptNode`

2. Visitor interface `IAstVisitor<T>` includes `VisitMoveIpNextNode` method

3. Visitors have been implemented:
   - `AstExpressionBuilder.VisitMoveIpNextNode()` - compiles to `State.IP = NextIp` assignment
   - `AstInstructionRenderer.VisitMoveIpNextNode()` - throws NotSupportedException (should not be rendered as assembly)

### Key Insight

`MoveIpToEndOfInstruction()` is simple:
```csharp
public void MoveIpToEndOfInstruction(CfgInstruction instruction) {
    State.IP = instruction.NextInMemoryAddress.Offset;
}
```

It sets IP to the offset of the next instruction in memory.

## Investigation Findings

### Key Discoveries

1. **Only [OpRegRm.mixin](src/Spice86.Core/Emulator/CPU/CfgCpu/ParsedInstruction/Instructions/Mixins/OpRegRm.mixin) has `GenerateExecutionAst()` implemented** - no control flow instructions have it yet
2. **IP advancement is CRITICAL for graph traversal** - `GetNextSuccessor()` uses current IP to determine next node
3. **AST should be COMPLETE** (include all side effects) - matches existing OpRegRm pattern
4. **Infrastructure is ready**:
   - `MoveIpNextNode` is defined and visitor is implemented
   - `BlockNode` is ready for wrapping multiple statements
   - `AstExpressionBuilder` handles control flow nodes correctly

### Control Flow Instruction Patterns

- **Regular instructions** (OpRegRm, etc.): Call `MoveIpToEndOfInstruction()` at end of `Execute()`
- **CALL instructions**: Call helper method which internally moves IP to end before jumping to target
- **JMP instructions**: Set IP directly to target (no advancement)
- **Conditional JMP**: Move IP to end if condition false, else jump to target
- **RET instructions**: Pop return address from stack and set IP

### Architecture Decision

The AST must include IP advancement because:
1. `GetNextSuccessor()` (called after AST execution) uses `State.IP` to look up the next graph node
2. This is critical for conditional jumps and returns to work correctly
3. The AST should be "complete" - representing all side effects of instruction execution

**Selected Approach: Option A with modifications**

Add `MoveIpNextNode` to the generated AST, wrapped in `BlockNode` when needed.

## Detailed Implementation Plan

### Phase 1: Fix `OpRegRm.mixin` (the only existing GenerateExecutionAst implementation) ✅ COMPLETED

**File:** [src\Spice86.Core\Emulator\CPU\CfgCpu\ParsedInstruction\Instructions\Mixins\OpRegRm.mixin](src/Spice86.Core/Emulator/CPU/CfgCpu/ParsedInstruction/Instructions/Mixins/OpRegRm.mixin)

**Status:** ✅ **Implemented with enhanced helper methods**

**Final implementation using AstBuilder helpers:**
```csharp
public override IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
    // Get both R and RM operands in one call
    var (rNode, rmNode) = builder.ModRmOperands(builder.UType({{Size}}), ModRmContext);

    // Create ALU operation call: Alu.Operation(R, RM)
    MethodCallValueNode aluCall = new MethodCallValueNode(builder.UType({{Size}}), "Alu{{Size}}", "{{Operation}}", rNode, rmNode);

    // Conditionally assign result: R = aluCall (if Assign) or just aluCall (for flags-only like CMP)
    IVisitableAstNode instructionLogic = builder.ConditionalAssign(builder.UType({{Size}}), rNode, aluCall, {{Assign}});

    // Wrap with IP advancement and return
    return builder.WithIpAdvancement(this, instructionLogic);
}
```

**Helper methods added to [AstBuilder.cs](src/Spice86.Core/Emulator/CPU/CfgCpu/Ast/Builder/AstBuilder.cs):**

1. **`WithIpAdvancement(instruction, instructionLogic)`** - Wraps logic with IP advancement in a BlockNode
2. **`ModRmOperands(dataType, modRmContext)`** - Returns tuple of (R, RM) nodes
3. **`ConditionalAssign(dataType, destination, source, assign)`** - Conditionally creates assignment
4. **`Assign(dataType, destination, source)`** - Creates assignment node

**Benefits of this approach:**
- ✅ **Reduced from 11 lines to 5 lines** (54% reduction)
- ✅ **No manual BlockNode/MoveIpNextNode creation needed**
- ✅ **No if/else branching in mixin** - handled by `ConditionalAssign`
- ✅ **Single line for ModRM operands** - using tuple deconstruction
- ✅ **Pattern is reusable for all 89 instruction files**

**Required using statements (already present in OpRegRm.mixin):**
```csharp
using Spice86.Core.Emulator.CPU.CfgCpu.Ast.Instruction.ControlFlow;
```

### Phase 2: Update all other mixins that call `MoveIpToEndOfInstruction` but don't have `GenerateExecutionAst`

**Affected files (89 files total):**
Files found by grep search for `MoveIpToEndOfInstruction` in mixins directory:
- `MovRegRm.mixin` (and many others - see grep results)

**Pattern for simple instructions using the new helper methods:**

**Example 1: Simple move (MovRegRm.mixin)**
```csharp
public override IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
    var (rNode, rmNode) = builder.ModRmOperands(builder.UType({{Size}}), ModRmContext);
    return builder.WithIpAdvancement(this, builder.Assign(builder.UType({{Size}}), rNode, rmNode));
}
```

**Example 2: ALU operation with accumulator (OpAccImm.mixin)**
```csharp
public override IVisitableAstNode GenerateExecutionAst(AstBuilder builder) {
    ValueNode accNode = builder.Register.Accumulator(builder.UType({{Size}}));
    ValueNode immNode = builder.InstructionField.ToNode(ValueField);
    MethodCallValueNode aluCall = new MethodCallValueNode(builder.UType({{Size}}), "Alu{{Size}}", "{{Operation}}", accNode, immNode);

    IVisitableAstNode instructionLogic = builder.ConditionalAssign(builder.UType({{Size}}), accNode, aluCall, {{Assign}});
    return builder.WithIpAdvancement(this, instructionLogic);
}
```

**Key Patterns:**
1. **Always end with:** `return builder.WithIpAdvancement(this, instructionLogic);`
2. **For ModRM instructions:** Use `builder.ModRmOperands(dataType, modRmContext)` tuple
3. **For conditional assignment:** Use `builder.ConditionalAssign(...)`
4. **For unconditional assignment:** Use `builder.Assign(...)`

**Note:** This is a LARGE task (89 files). For the initial fix:
1. ✅ Fixed `OpRegRm.mixin` with helper methods (proof of concept)
2. ✅ Created comprehensive helper methods in AstBuilder
3. ⏳ Roll out to other instructions incrementally

### Phase 3: Update CfgCpu.cs commented code (optional documentation)

**File:** [src\Spice86.Core\Emulator\CPU\CfgCpu\CfgCpu.cs](src/Spice86.Core/Emulator/CPU/CfgCpu/CfgCpu.cs) (lines 68-74)

**Update TODO comment to reflect that IP advancement is now included in AST:**
```csharp
// TODO: Enable AST execution once all instructions implement GenerateExecutionAst
//
// Current status:
// - OpRegRm instructions (ADD, SUB, AND, OR, XOR, CMP, ADC, SBB) are implemented with IP advancement
// - IP advancement is included in the AST via MoveIpNextNode wrapped in BlockNode
// - Remaining instructions will throw NotImplementedException
//
// To enable:
// 1. Uncomment the block below
// 2. Comment out the direct Execute() call above
// 3. Run MachineTest to identify missing implementations
// 4. Implement GenerateExecutionAst() for failing instructions following the OpRegRm pattern
// 5. Iterate until all tests pass
//
// See code-review-findings.md Issue #2 for MoveIpAndSetNextNode handling
// See microcode-ast.md for full plan and remaining work
//
// IVisitableAstNode executionAst = toExecute.GenerateExecutionAst(new Ast.Builder.AstBuilder());
// ...
```

### Phase 4: Testing and Validation

**Test approach:**
1. **Unit test for AST generation:**
   - Create test that calls `GenerateExecutionAst()` on a simple OpRegRm instruction
   - Verify the returned AST is a `BlockNode` with 2 children
   - Verify first child is the instruction logic (assignment or ALU call)
   - Verify second child is `MoveIpNextNode` with correct offset

2. **Integration test for AST execution:**
   - Uncomment the AST execution code in `CfgCpu.cs`
   - Run a simple program with OpRegRm instructions only
   - Verify IP advances correctly
   - Verify program executes without hanging
   - Re-comment the code (not ready for full rollout yet)

3. **Comparison test:**
   - Execute same instruction sequence with both `Execute()` and AST paths
   - Verify State.IP ends up at same value
   - Verify all register/memory values match

## Critical Files

### Files to Modify
1. [src\Spice86.Core\Emulator\CPU\CfgCpu\ParsedInstruction\Instructions\Mixins\OpRegRm.mixin](src/Spice86.Core/Emulator/CPU/CfgCpu/ParsedInstruction/Instructions/Mixins/OpRegRm.mixin) - Add MoveIpNextNode
2. [src\Spice86.Core\Emulator\CPU\CfgCpu\CfgCpu.cs](src/Spice86.Core/Emulator/CPU/CfgCpu/CfgCpu.cs) - Update TODO comment (optional)

### Files to Reference
1. [src\Spice86.Core\Emulator\CPU\CfgCpu\Ast\Instruction\ControlFlow\MoveIpNextNode.cs](src/Spice86.Core/Emulator/CPU/CfgCpu/Ast/Instruction/ControlFlow/MoveIpNextNode.cs) - IP advancement node
2. [src\Spice86.Core\Emulator\CPU\CfgCpu\Ast\Instruction\BlockNode.cs](src/Spice86.Core/Emulator/CPU/CfgCpu/Ast/Instruction/BlockNode.cs) - Statement sequence wrapper
3. [src\Spice86.Core\Emulator\CPU\CfgCpu\InstructionExecutor\Expressions\AstExpressionBuilder.cs](src/Spice86.Core/Emulator/CPU/CfgCpu/InstructionExecutor/Expressions/AstExpressionBuilder.cs) - Visitor implementation
4. [src\Spice86.Core\Emulator\CPU\CfgCpu\ParsedInstruction\CfgInstruction.cs](src/Spice86.Core/Emulator/CPU/CfgCpu/ParsedInstruction/CfgInstruction.cs) - Understanding NextInMemoryAddress

## Scope Considerations

**For this task, we will:**
1. ✅ Fix `OpRegRm.mixin` as proof of concept
2. ✅ Update TODO comment in `CfgCpu.cs` for documentation
3. ✅ Build and verify no compilation errors

**Future work (separate tasks):**
1. Roll out pattern to remaining 88 mixin files
2. Implement GenerateExecutionAst for control flow instructions
3. Enable AST execution by default once all instructions are covered
4. Add comprehensive unit and integration tests

## Verification Steps

1. **Build the project** - ensure no compilation errors
2. **Run existing tests** - ensure no regressions in Execute() path
3. **Code review** - verify AST structure matches architectural intent
4. **Manual verification (optional):**
   - Temporarily uncomment AST execution code in CfgCpu.cs
   - Run a simple test program with ADD/SUB instructions
   - Verify IP advances correctly
   - Verify program completes without hanging
   - Re-comment the code
