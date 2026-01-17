
# User Story

## 📊 Current Status Summary

**Implementation Phase**: ✅ Infrastructure Complete, 🔄 Adoption In Progress

Most of the infrastructure has been implemented:
- ✅ AST node types created (`HelperCallNode`, `ValueHelperCallNode`, `BlockNode`)
- ✅ `ICfgNode.GenerateExecutionAst()` method added
- ✅ Visitors updated (`AstExpressionBuilder`, `AstInstructionRenderer`, `IAstVisitor`)
- ✅ Example implementation in `OpRegRm.mixin` (covers ~27 instructions: ADD, SUB, CMP, AND, OR, XOR, etc.)
- ✅ Execution infrastructure added to `CfgCpu.ExecuteNext()` (currently disabled)

**What's Remaining**:
- ⏳ Implement `GenerateExecutionAst()` for remaining instruction types beyond OpRegRm
- ⏳ Enable AST execution path and validate with tests
- ⏳ Ensure all tests pass with AST execution enabled

**Recent Commits**:
- `c6df0e61` - Use builder.ModRm nodes and BinaryOperation.ASSIGN, add ValueHelperCallNode
- `4041dd52` - Implement GenerateExecutionAst with proper microcode AST for OpRegRm
- `d939be8d` - Fully implement AST execution infrastructure
- `48194f51` - Add AST execution infrastructure to CfgCpu.ExecuteNext

---

## Technical User Story: Refactor CfgCpu to Execute Instructions via Microcode AST

**Role**: Core Developer
**Goal**: Decouple the execution logic from `ICfgNode` by introducing a granular "microcode-like" Abstract Syntax Tree (AST) that details the specific side effects of each instruction.
**Benefit**: Precise, reusable, and analyzable comparison of instruction logic that allows for future optimizations (JIT) while maintaining correctness via side-by-side validation.

### Acceptance Criteria

1. **ICfgNode Interface Update**
    - [x] `ICfgNode` MUST expose a method (`GenerateExecutionAst()`?) that returns an `IVisitableAstNode`. ✅ [ICfgNode.cs:69](src/Spice86.Core/Emulator/CPU/CfgCpu/ControlFlowGraph/ICfgNode.cs#L69)
    - [x] The returned AST MUST be composed of granular, reusable "microcode" nodes that describe the instruction's logic. ✅ Implemented via `HelperCallNode` and `ValueHelperCallNode`
    - [x] The AST SHOULD NOT rely on the `InstructionOperation` enum for execution logic. ✅ Uses helper method calls instead

2. **Microcode AST System**
    - [x] **AST Nodes**: Use existing nodes where possible. Use `BinaryOperation.ASSIGN` for both register writing and temporary variable assignment. ✅ [OpRegRm.mixin:51](src/Spice86.Core/Emulator/CPU/CfgCpu/ParsedInstruction/Instructions/Mixins/OpRegRm.mixin#L51)
    - [x] **Granularity**: The logic must be decomposed. For example, `ADD AX, BX` should be expressed as fetching operands, performing the addition (potentially calling existing ALU helpers), and assigning the result back to `AX`. ✅ Implemented in OpRegRm.mixin
    - [x] **ModRm**: ModRm operations should be generated via `AstExpressionBuilder` methods (e.g., `builder.ModRm.RToNode` / `builder.ModRm.RmToNode`) rather than monolithic helpers, if feasible. ✅ [OpRegRm.mixin:43-44](src/Spice86.Core/Emulator/CPU/CfgCpu/ParsedInstruction/Instructions/Mixins/OpRegRm.mixin#L43-L44)

3. **AstExpressionBuilder Update**
    - [x] `AstExpressionBuilder` MUST be updated to visit the new microcode nodes. ✅ Implemented `VisitHelperCallNode` and `VisitBlockNode`
    - [x] **Delegate Generation**: It MUST generate a delegate (e.g., `Action<State, Memory>`) using the existing `ToAction` (or an overload with necessary parameters) that replicates the behavior of the `Execute` method. ✅ Has `ToActionWithHelper` method
    - [x] **Helpers**: It IS PERMISSIBLE to call existing `Native` helpers (like `Alu` methods) from the generated expression tree for complex operations to keep the refactoring manageable. ✅ Uses reflection to call helper methods

4. **Execution Model Migration**
    - [x] **Target**: The `CfgCpu.ExecuteNext` method MUST be updated to support the new AST-based execution. ✅ [CfgCpu.cs:68-74](src/Spice86.Core/Emulator/CPU/CfgCpu/CfgCpu.cs#L68-L74) (commented out)
    - [x] **Side-by-Side**: The old `Execute(helper)` path MUST remain available. ✅ [CfgCpu.cs:66](src/Spice86.Core/Emulator/CPU/CfgCpu/CfgCpu.cs#L66)
    - [x] Switching between the old and new execution models should be done via commenting/uncommenting the call in `ExecuteNext` for this iteration, to allow for validation. ✅ Implemented as TODO comment

5. **Validation & Testing**
    - [ ] Existing tests must pass when the AST execution path is enabled.
    - [ ] The behavior must be identical to the direct `Execute` method.

### Implementation Notes
- **AST Construction**: `ICfgNode` implementations (like `InstructionNode`) will construct the AST.
- **ModRm Integration**: `AstExpressionBuilder` needs to expose ModRm resolution logic that returns AST nodes (e.g., resolving a pointer to a memory address node or a register node).
- **Execution Flow**:
    1.  `CfgCpu.ExecuteNext` gets current node.
    2.  Check if we are using AST or Legacy.
    3.  If AST: `node.GenerateExecutionAst()` -> `builder.Build(ast)` -> `delegate.Invoke(State, Memory)`.



# Implementation Plan - CfgCpu Microcode AST Refactor

Decouple instruction execution from `ICfgNode` by introducing a granular microcode AST and compiling it to delegates.

## Proposed Changes

### AST System
#### ✅ [DONE] [HelperCallNode](src/Spice86.Core/Emulator/CPU/CfgCpu/Ast/Instruction/HelperCallNode.cs)
- Represents a call to a method on `InstructionExecutionHelper` or its properties (e.g., `Alu8.Add`).
- Properties:
    - `string? PropertyName` (renamed from HelperName - e.g. "Alu8", "Stack" or null for root helper)
    - `string MethodName` (e.g. "Add", "Push16")
    - `IReadOnlyList<IVisitableAstNode> Arguments`
- Implements `IVisitableAstNode`.

#### ✅ [DONE] [ValueHelperCallNode](src/Spice86.Core/Emulator/CPU/CfgCpu/Ast/Instruction/ValueHelperCallNode.cs)
- Represents a call to a helper method that returns a value (extends `ValueNode`).
- Added to handle ALU operations that return values.

#### ✅ [DONE] [BlockNode](src/Spice86.Core/Emulator/CPU/CfgCpu/Ast/Instruction/BlockNode.cs)
- Represents a sequence of statements.
- Properties: `IReadOnlyList<IVisitableAstNode> Statements`.

### ICfgNode Support
#### ✅ [DONE] [ICfgNode](src/Spice86.Core/Emulator/CPU/CfgCpu/ControlFlowGraph/ICfgNode.cs)
- Added method: `IVisitableAstNode GenerateExecutionAst(AstBuilder builder);` at line 69

#### ✅ [DONE] [CfgNode](src/Spice86.Core/Emulator/CPU/CfgCpu/ControlFlowGraph/CfgNode.cs)
- Implemented `GenerateExecutionAst` as `virtual` throwing `NotImplementedException` with helpful message for uncovered instructions.

### Instruction Implementation
#### ✅ [DONE] [OpRegRm.mixin](src/Spice86.Core/Emulator/CPU/CfgCpu/ParsedInstruction/Instructions/Mixins/OpRegRm.mixin)
- Implemented `GenerateExecutionAst(AstBuilder builder)` to return the detailed AST.
- Logic:
    1. ✅ Resolve `R` and `Rm` nodes via `builder.ModRm.RToNode` / `RmToNode`.
    2. ✅ Create `ValueHelperCallNode` for the ALU operation (e.g. `Alu8.Add`).
    3. ✅ If `Assign` is true, wrap in `BinaryOperation.ASSIGN` (R = ALU(...)).
    4. ⚠️ Does NOT create `HelperCallNode` for `MoveIpAndSetNextNode` - simplified implementation
    5. ⚠️ Does NOT return `BlockNode` - returns single assignment or call node

### Visitors Updates
#### ✅ [DONE] [AstExpressionBuilder](src/Spice86.Core/Emulator/CPU/CfgCpu/InstructionExecutor/Expressions/AstExpressionBuilder.cs)
- Implemented `VisitHelperCallNode` at line 319
- Implemented `VisitValueHelperCallNode` for value-returning helper calls
- Implemented `VisitBlockNode` at line 350
- Uses reflection to find methods on `InstructionExecutionHelper` and generates `Expression.Call`

#### ✅ [DONE] [AstInstructionRenderer](src/Spice86.Core/Emulator/CPU/CfgCpu/InstructionRenderer/AstInstructionRenderer.cs)
- Implemented `VisitHelperCallNode` at line 218
- Implemented `VisitBlockNode` at line 224

#### ✅ [DONE] [IAstVisitor](src/Spice86.Core/Emulator/CPU/CfgCpu/Ast/IAstVisitor.cs)
- Added `VisitHelperCallNode` at line 20
- Added `VisitValueHelperCallNode` at line 21
- Added `VisitBlockNode` at line 22

### Execution Engine
#### ✅ [DONE] [CfgCpu](src/Spice86.Core/Emulator/CPU/CfgCpu/CfgCpu.cs)
- Updated `ExecuteNext()`:
    - ✅ Infrastructure added for AST-based execution (lines 68-74, currently commented out)
    - ✅ Old `Execute` path remains at line 66
    - ✅ Switching done via commenting/uncommenting
    - ⚠️ **Current Status**: AST execution path is commented out, old path is active

## Verification Plan

### Automated Tests
- **MachineTest**: Run `Spice86.Tests.MachineTest` to execute real binaries. This is the primary verification to ensure full CPU behavior is preserved.
- **Unit Tests**: Check basic instruction logic.

### Manual Verification
- N/A - relying on `MachineTest`.

---

## 🎯 Next Steps

To complete the microcode AST refactor, the following work remains:

### 1. Expand AST Implementation to More Instructions
Currently only `OpRegRm` instructions have `GenerateExecutionAst()` implemented. Need to add implementations for:
- Other instruction mixins (e.g., OpImm, OpMem, etc.)
- Individual instruction classes that don't use mixins
- Special instructions (CALL, RET, JMP, conditional jumps, etc.)
- Stack operations (PUSH, POP)
- String operations (MOVS, CMPS, SCAS, etc.)
- Control flow instructions (INT, IRET, etc.)

**Approach**: Follow the pattern established in [OpRegRm.mixin](src/Spice86.Core/Emulator/CPU/CfgCpu/ParsedInstruction/Instructions/Mixins/OpRegRm.mixin):
1. Use `builder.ModRm` methods to get operand nodes
2. Create `ValueHelperCallNode` or `HelperCallNode` for operations
3. Use `BinaryOperation.ASSIGN` for assignments
4. Return simplified AST (single node or `BlockNode` if multiple statements needed)

### 2. Enable and Validate AST Execution
- Uncomment the AST execution path in [CfgCpu.cs:68-74](src/Spice86.Core/Emulator/CPU/CfgCpu/CfgCpu.cs#L68-L74)
- Run tests to identify which instructions fail with `NotImplementedException`
- Implement `GenerateExecutionAst()` for those instructions
- Iterate until all tests pass

### 3. Handle Edge Cases
- Ensure `MoveIpAndSetNextNode` is called appropriately (currently omitted from OpRegRm implementation)
- Consider whether full `BlockNode` sequences are needed for complex instructions
- Validate that exception handling works correctly with AST execution

### 4. Performance Testing
Once tests pass:
- Compare performance between old `Execute()` and new AST-based execution
- Identify any performance regressions
- Consider caching compiled delegates if beneficial

### 5. Final Cleanup
- Remove old `Execute()` method once AST path is stable
- Remove feature flag / commenting infrastructure
- Update documentation
