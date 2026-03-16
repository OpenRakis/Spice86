# GenerateExecutionAst Implementation Tracking

**Last Updated:** 2026-03-14
**Status:** All Special and Floating Point Instructions complete

## Summary Statistics

- **Total Instructions with MoveIpToEndOfInstruction:** 64 mixins + ~25 non-mixin files = ~89 total
- **Implemented Mixins:** 35 (covering ~320+ instruction variants)
- **Remaining Mixins:** 29
- **Implemented Non-Mixin:** All non-mixin instructions complete
- **Remaining Non-Mixin:** 0
- **Target:** All instructions should have GenerateExecutionAst before enabling AST execution

**Recent Progress:**
- Implemented missing execution AST for `InvalidInstruction`, `SelectorNode`, `Grp5RmJumpFar`, `Grp5RmJumpNear`, `Interrupt`, `Interrupt3`, `JmpFarImm`, `Cpuid`, and `RetInterrupt`
- Added dedicated execution AST nodes: `SelectorNode`, `InvalidInstructionNode`, `CpuidNode`
- Special Instructions COMPLETE! MovSregRm16, BswapReg32, InterruptOverflow (INTO), Grp4Callback all implemented
- Floating Point Instructions COMPLETE! FnInit, Fnstcw, Fnstsw all implemented
- Added `CallbackNode` AST node + `ExecuteCallback` helper for Grp4Callback dispatch
- Added `ByteSwap(ValueNode)` to `BitwiseAstBuilder` for BSWAP instruction
- Added `ConditionalInterrupt(instruction, condition, vectorNumber)` to `ControlFlowAstBuilder` for INTO
- Category I COMPLETE! Control-flow operations (CALL/JMP/Jcc/LOOP/RET) implemented with dedicated control-flow AST nodes and conditional branch generation helper
- Category J COMPLETE! Flag control operations (CLC/STC/CLI/STI/CLD/STD/CMC) in FlagControl.mixin, with SetInterruptShadowingIfInterruptDisabled() helper for STI
- Category K COMPLETE! Stack frame operations (ENTER/LEAVE) with AST generation; ENTER uses for-semantics helper in ControlFlowAstBuilder
- Category H COMPLETE! All I/O operations (InAccImm, InAccDx, OutAccImm, OutAccDx) with new IoAstBuilder
- Category G COMPLETE! All special ModRM operations (LEA, LXS, BOUND, XCHG, XADD, CMPXCHG, XLAT)
- Category D COMPLETE! All multiply/divide operations with register pairs
- Category A COMPLETE! All ModRM ALU-like operations implemented
- Category B COMPLETE! All shift/rotate operations including double-precision shifts
- Move operations COMPLETE! All MOV variants including MOVSX/MOVZX
- Simple type conversions: NOP, CBW, CWDE, CWD, CDQ
- Added temporary variable infrastructure for splitting wide results

---

## Implementation Strategy

### Phase 1: Mixin-based Instructions (64 files)
Focus on mixin files that follow similar patterns. These use template parameters and are easier to implement systematically.

### Phase 2: Non-mixin Instructions (~25 files)
Handle standalone instruction classes (like Hlt, Nop, etc.) that don't use mixins.

---

## ✅ Completed Implementations

### OpRegRm Family (ALU Operations with ModRM)
**Status:** ✅ **COMPLETE** - Implements pattern for all ALU operations
**File:** [OpRegRm.mixin](src/Spice86.Core/Emulator/CPU/CfgCpu/ParsedInstruction/Instructions/Mixins/OpRegRm.mixin)
**Pattern:** ModRM + ALU operation + Conditional assignment + IP advancement

**Covers these operations:**
- ✅ ADD (AddRegRm8, AddRegRm16, AddRegRm32)
- ✅ ADC (AdcRegRm8, AdcRegRm16, AdcRegRm32)
- ✅ AND (AndRegRm8, AndRegRm16, AndRegRm32)
- ✅ OR (OrRegRm8, OrRegRm16, OrRegRm32)
- ✅ XOR (XorRegRm8, XorRegRm16, XorRegRm32)
- ✅ SUB (SubRegRm8, SubRegRm16, SubRegRm32)
- ✅ SBB (SbbRegRm8, SbbRegRm16, SbbRegRm32)
- ✅ CMP (CmpRegRm8, CmpRegRm16, CmpRegRm32) - flags only, no assignment

**Implementation:**
```csharp
var (rNode, rmNode) = builder.ModRmOperands(builder.UType({{Size}}), ModRmContext);
MethodCallValueNode aluCall = new MethodCallValueNode(builder.UType({{Size}}), "Alu{{Size}}", "{{Operation}}", rNode, rmNode);
IVisitableAstNode instructionLogic = builder.ConditionalAssign(builder.UType({{Size}}), rNode, aluCall, {{Assign}});
return builder.WithIpAdvancement(this, instructionLogic);
```

---

## 🔄 In Progress

### Category: Simple ModRM Move Operations ✅ **COMPLETE**

- ✅ **MovRegRm.mixin** - MOV R, RM (simple assignment) - **COMPLETE**
- ✅ **MovRmReg.mixin** - MOV RM, R (reverse assignment) - **COMPLETE**
- ✅ **MovRmImm.mixin** - MOV RM, IMM (immediate to memory/register) - **COMPLETE**
- ✅ **MovRegImm.mixin** - MOV R, IMM (immediate to register) - **COMPLETE**
- ✅ **MovRmSignExtend.mixin** - MOVSX (sign-extend) - **COMPLETE**
- ✅ **MovRmZeroExtend.mixin** - MOVZX (zero-extend) - **COMPLETE**
- ✅ **MovRmSreg.mixin** - MOV RM, SREG (segment register move) - **COMPLETE**
- ✅ **MovAccMoffs.mixin** - MOV AL/AX/EAX, [offset] - **COMPLETE**
- ✅ **MovMoffsAcc.mixin** - MOV [offset], AL/AX/EAX - **COMPLETE**

---

## 📋 Planned Implementation Queue

### Category A: ModRM ALU-like Operations ✅ **COMPLETE**
Similar to OpRegRm but different operation types.

- ✅ **OpRmReg.mixin** - ALU RM, R (reverse operand order) - **COMPLETE**
- ✅ **OpAccImm.mixin** - ALU ACC, IMM (accumulator + immediate) - **COMPLETE**
- ✅ **Grp1.mixin** - Group 1 instructions (ADD/OR/ADC/SBB/AND/SUB/XOR/CMP with immediate) - **COMPLETE**
- ✅ **Grp45RmIncDec.mixin** - INC/DEC RM - **COMPLETE**
- ✅ **IncDecReg.mixin** - INC/DEC register - **COMPLETE**
- ✅ **Grp3NegRm.mixin** - NEG RM - **COMPLETE**
- ✅ **Grp3NotRm.mixin** - NOT RM - **COMPLETE**
- ✅ **Grp3TestRmImm.mixin** - TEST RM, IMM - **COMPLETE**

### Category B: Shift/Rotate Operations ✅ **COMPLETE**
Involve ALU shift operations.

- ✅ **Grp2RmOp.mixin** - Shift/Rotate RM by 1/CL - **COMPLETE**
- ✅ **Grp2RmOpImm.mixin** - Shift/Rotate RM by immediate - **COMPLETE**
- ✅ **ShxdCl.mixin** - SHLD/SHRD by CL - **COMPLETE**
- ✅ **ShxdImm8.mixin** - SHLD/SHRD by immediate - **COMPLETE**

### Category C: Bit Operations ✅ **COMPLETE** (MEDIUM Priority)
Specialized bit manipulation.

- ✅ **BitTestRm.mixin** - BT/BTC/BTR/BTS RM, R - **COMPLETE**
- ✅ **BitTestRmImm.mixin** - BT/BTC/BTR/BTS RM, IMM - **COMPLETE**
- ✅ **BsfRm.mixin** - BSF (bit scan forward) - **COMPLETE**
- ✅ **BsrRm.mixin** - BSR (bit scan reverse) - **COMPLETE**
- ✅ **SetRmcc.mixin** - SETcc (set byte on condition) - **COMPLETE**

### Category D: Multiply/Divide ✅ **COMPLETE**
Complex ALU operations with register pairs.

- ✅ **Grp3MulRmAcc.mixin** - MUL/IMUL RM - **COMPLETE**
- ✅ **Grp3DivRmAcc.mixin** - DIV/IDIV RM - **COMPLETE**
- ✅ **ImulRm.mixin** - IMUL R, RM - **COMPLETE** (from previous session)
- ✅ **ImulImmRm.mixin** - IMUL R, RM, IMM - **COMPLETE** (from previous session)

### Category E: Stack Operations (MEDIUM Priority)
Push/Pop instructions.

- ✅ **PushReg.mixin** - PUSH R
- ✅ **PopReg.mixin** - POP R
- ✅ **PushImm.mixin** - PUSH IMM
- ✅ **PushImm8SignExtended.mixin** - PUSH IMM8 (sign-extended)
- ✅ **PopRm.mixin** - POP RM
- ✅ **Grp5RmPush.mixin** - PUSH RM
- ✅ **Pusha.mixin** - PUSHA/PUSHAD
- ✅ **Popa.mixin** - POPA/POPAD
- ✅ **PushF.mixin** - PUSHF/PUSHFD
- ✅ **PopF.mixin** - POPF/POPFD

### Category F: String Operations (MEDIUM Priority)
REP-prefixed operations.

- ✅ **Movs.mixin** - MOVS (move string)
- ✅ **Stos.mixin** - STOS (store string)
- ✅ **Lods.mixin** - LODS (load string)
- ✅ **Cmps.mixin** - CMPS (compare string)
- ✅ **Scas.mixin** - SCAS (scan string)
- ✅ **InsDx.mixin** - INS (input string)
- ✅ **OutsDx.mixin** - OUTS (output string)

### Category G: Special ModRM Operations (LOW Priority) ✅ **COMPLETE**
Complex or specialized operations.

- ✅ **Lea.mixin** - LEA (load effective address) - **COMPLETE**
- ✅ **Lxs.mixin** - LDS/LES/LFS/LGS/LSS (load pointer) - **COMPLETE**
- ✅ **Bound.mixin** - BOUND (check array bounds) - **COMPLETE**
- ✅ **XchgRm.mixin** - XCHG RM, R - **COMPLETE**
- ✅ **XchgRegAcc.mixin** - XCHG R, AX/EAX - **COMPLETE**
- ✅ **XaddRm.mixin** - XADD RM, R (exchange and add) - **COMPLETE**
- ✅ **CmpxchgRm.mixin** - CMPXCHG RM, R (compare and exchange) - **COMPLETE**
- ✅ **Xlat.mixin** - XLAT (translate byte) - **COMPLETE**

### Category H: I/O Operations ✅ **COMPLETE** (LOW Priority)
Port I/O instructions implemented via `IoAstBuilder` (analogous to `StringOperationAstBuilder` for INS/OUTS).

- ✅ **InAccImm.mixin** - IN AL/AX/EAX, IMM - **COMPLETE**
- ✅ **InAccDx.mixin** - IN AL/AX/EAX, DX - **COMPLETE**
- ✅ **OutAccImm.mixin** - OUT IMM, AL/AX/EAX - **COMPLETE**
- ✅ **OutAccDx.mixin** - OUT DX, AL/AX/EAX - **COMPLETE**

### Category I: Control Flow (SPECIAL - Already have AST nodes)
These need special handling with existing control flow nodes.

- ✅ **CallNearImm.mixin** - CALL near immediate - **COMPLETE**
- ✅ **CallFarImm.mixin** - CALL far immediate - **COMPLETE**
- ✅ **Grp5RmCallNear.mixin** - CALL near RM - **COMPLETE**
- ✅ **Grp5RmCallFar.mixin** - CALL far RM - **COMPLETE**
- ✅ **JmpNearImm.mixin** - JMP near immediate - **COMPLETE**
- ✅ **JccNearImm.mixin** - Jcc (conditional jump) - **COMPLETE**
- ✅ **Loop.mixin** - LOOP/LOOPE/LOOPNE - **COMPLETE**
- ✅ **RetNear.mixin** - RET near - **COMPLETE**
- ✅ **RetNearImm.mixin** - RET near IMM - **COMPLETE**
- ✅ **RetFar.mixin** - RET far - **COMPLETE**
- ✅ **RetFarImm.mixin** - RET far IMM - **COMPLETE**

### Category J: Flag Operations (LOW Priority) ✅ **COMPLETE**
Simple flag manipulation.

- ✅ **FlagControl.mixin** - CLC/STC/CLI/STI/CLD/STD/CMC - **COMPLETE**

### Category K: Stack Frame Operations (LOW Priority)
- ✅ **Enter.mixin** - ENTER - **COMPLETE**
- ✅ **Leave.mixin** - LEAVE - **COMPLETE**

---

## 📦 Non-Mixin Instructions (Standalone Classes)

### Simple Instructions (HIGH Priority)
- ✅ **Nop.cs** - NOP (no operation) - **COMPLETE**
- ✅ **Hlt.cs** - HLT (halt) - **COMPLETE** (dedicated `HltNode`)
- ✅ **Cbw16.cs** - CBW (convert byte to word) - **COMPLETE**
- ✅ **Cbw32.cs** - CWDE (convert word to doubleword) - **COMPLETE**
- ✅ **Cwd16.cs** - CWD (convert word to doubleword) - **COMPLETE**
- ✅ **Cwd32.cs** - CDQ (convert doubleword to quadword) - **COMPLETE**
- ✅ **Salc.cs** - SALC (set AL on carry) - **COMPLETE**
- ✅ **Sahf.cs** - SAHF (store AH into flags) - **COMPLETE**
- ✅ **Lahf.cs** - LAHF (load AH from flags) - **COMPLETE**
- ✅ **Daa.cs** - DAA (decimal adjust after addition) - **COMPLETE**
- ✅ **Das.cs** - DAS (decimal adjust after subtraction) - **COMPLETE**
- ✅ **Aaa.cs** - AAA (ASCII adjust after addition) - **COMPLETE**
- ✅ **Aas.cs** - AAS (ASCII adjust after subtraction) - **COMPLETE**
- ✅ **Aam.cs** - AAM (ASCII adjust after multiplication) - **COMPLETE**
- ✅ **Aad.cs** - AAD (ASCII adjust before division) - **COMPLETE**

### Special Instructions (MEDIUM Priority)
- ✅ **MovSregRm16.cs** - MOV SREG, RM16 - **COMPLETE** (emits SetInterruptShadowing when SS is written)
- ✅ **Bswap.cs** - BSWAP (byte swap) - **COMPLETE** (uses new `BitwiseAstBuilder.ByteSwap`)
- ✅ **InterruptOverflow.cs** - INTO (interrupt on overflow) - **COMPLETE** (uses `ConditionalInterrupt` helper)
- ✅ **Grp4Callback.cs** - Special callback handling - **COMPLETE** (uses new `CallbackNode` + `ExecuteCallback`)

### Control-Flow / CPU-Fault Special Cases
- ✅ **Interrupt.cs** - INT imm8 - **COMPLETE** (uses `InterruptCallNode`)
- ✅ **Interrupt3.cs** - INT3 - **COMPLETE** (uses `InterruptCallNode`)
- ✅ **JmpFarImm.cs** - JMP far immediate - **COMPLETE** (uses `JumpFarNode`)
- ✅ **Grp5RmJumpNear.cs** - JMP near r/m16 - **COMPLETE** (uses `JumpNearNode`)
- ✅ **Grp5RmJumpFar.cs** - JMP far m16:16 - **COMPLETE** (uses `JumpFarNode`)
- ✅ **RetInterrupt.cs** - IRET - **COMPLETE** (uses `ReturnInterruptNode`)
- ✅ **Cpuid.cs** - CPUID fault path - **COMPLETE** (uses dedicated `CpuidNode`)
- ✅ **InvalidInstruction.cs** - Deferred CPU exception instruction - **COMPLETE** (uses dedicated `InvalidInstructionNode`)
- ✅ **SelfModifying/SelectorNode.cs** - Selector dispatch node - **COMPLETE** (uses dedicated execution `SelectorNode`)

### Floating Point Instructions (LOW Priority)
- ✅ **FnInit.cs** - FINIT/FNINIT - **COMPLETE** (no FPU emulation, just advances IP)
- ✅ **Fnstcw.cs** - FNSTCW - **COMPLETE** (stores hardcoded 0x37F control word to RM16)
- ✅ **Fnstsw.cs** - FNSTSW - **COMPLETE** (stores hardcoded 0xFF status word to RM16)

### Push/Pop Special (LOW Priority)
- ✅ **PushF16.cs** - PUSHF
- ✅ **PushF32.cs** - PUSHFD

---

## 🎯 Next Actions

### Immediate Next Steps (Today)
1. ✅ Create this tracking file
2. ⏳ Implement Category: Simple ModRM Move Operations (9 files)
3. ⏳ Implement Category A: ModRM ALU-like Operations (8 files)
4. Build and verify all implementations

### This Week
- Complete Categories A-D (all ALU/bit operations)
- Start Category E (stack operations)

### Next Week
- Complete Categories E-H
- Begin control flow instructions (Category I)
- Handle non-mixin instructions

---

## 📝 Implementation Notes

### Common Patterns Discovered

**Pattern 1: Simple Assignment (MOV-like)**
```csharp
var (destNode, srcNode) = builder.ModRmOperands(dataType, modRmContext);
return builder.WithIpAdvancement(this, builder.Assign(dataType, destNode, srcNode));
```

**Pattern 2: ALU Operation (ADD/SUB/etc.)**
```csharp
var (rNode, rmNode) = builder.ModRmOperands(dataType, modRmContext);
MethodCallValueNode aluCall = new MethodCallValueNode(dataType, "AluN", "Operation", rNode, rmNode);
return builder.WithIpAdvancement(this, builder.ConditionalAssign(dataType, rNode, aluCall, assign));
```

**Pattern 3: Unary Operation (INC/DEC/NEG/NOT)**
```csharp
ValueNode operand = builder.ModRm.RmToNode(dataType, modRmContext);
MethodCallValueNode operation = new MethodCallValueNode(dataType, "AluN", "Operation", operand);
return builder.WithIpAdvancement(this, builder.Assign(dataType, operand, operation));
```

**Pattern 4: Immediate Value Move (MOV RM/R, IMM)**
```csharp
ValueNode destNode = builder.ModRm.RmToNode(dataType, modRmContext); // or builder.Register.Reg(dataType, registerIndex)
ValueNode immNode = builder.InstructionField.ToNode(ValueField);
return builder.WithIpAdvancement(this, builder.Assign(dataType, destNode, immNode));
```

**Pattern 5: Temporary Variables (MUL/DIV with register pair results)**
```csharp
// Declare a temporary variable to store intermediate result
VariableDeclarationNode resultDecl = builder.DeclareVariable(wideType, "result", aluCall);
VariableReferenceNode resultRef = builder.VariableReference(wideType, "result");

// Use the result multiple times without duplicating the ALU call
// Example: AH = (byte)(result >> 8), AL = (byte)result
```

### Infrastructure: Temporary Variable Support

**Added in 2026-01-11** to support multiply/divide operations that need to split wide results across register pairs.

**New AST Node Types:**
- `VariableDeclarationNode` - Declares and initializes a local variable
- `VariableReferenceNode` - References a previously declared variable

**Scope Management:**
- Variables are **properly scoped** to their declaring block (lexical scoping)
- Supports **nested blocks** with inner scopes having access to outer scope variables
- AstExpressionBuilder uses a **scope stack** (`Stack<Dictionary<string, ParameterExpression>>`)
- Each block pushes a new scope on entry and pops it on exit (using try/finally)
- Variable lookup searches from innermost to outermost scope
- Variables are automatically cleaned up when exiting their declaring block

**Usage Example:**
```csharp
// For MUL RM8: ushort result = Alu8.Mul(AL, RM8); AH = high byte; AL = low byte
DataType wideType = builder.UType(16);
MethodCallValueNode mulCall = new MethodCallValueNode(wideType, "Alu8", "Mul", alNode, rmNode);
VariableDeclarationNode resultDecl = builder.DeclareVariable(wideType, "result", mulCall);
VariableReferenceNode resultRef = builder.VariableReference(wideType, "result");

// Extract high and low bytes
BinaryOperationNode shiftRight = new BinaryOperationNode(wideType, resultRef,
    BinaryOperation.RIGHT_SHIFT, builder.Constant.ToNode(8));
TypeConversionNode highByte = builder.TypeConversion.Convert(builder.UType(8), shiftRight);
TypeConversionNode lowByte = builder.TypeConversion.Convert(builder.UType(8), resultRef);
BinaryOperationNode assignHigh = builder.Assign(builder.UType(8), ahNode, highByte);
BinaryOperationNode assignLow = builder.Assign(builder.UType(8), alNode, lowByte);

return builder.WithIpAdvancement(this, resultDecl, assignHigh, assignLow);
```

### Blockers & Special Cases

- **Control flow instructions:** Need to use existing ControlFlow nodes (CallNearNode, JumpNearNode, etc.)
- **String instructions:** May need REP prefix handling in AST
- **Floating point:** May need special FPU stack handling

---

## 📦 New Infrastructure Added (2026-03-14)

### CallbackNode
**File:** `Ast/Instruction/ControlFlow/CallbackNode.cs`
Represents a callback dispatch (Grp4Callback / CALLBACK instruction). Calls `ExecuteCallback(instruction, callbackNumber)` on the helper, which:
1. Calls `CallbackHandler.Run(callbackNumber)`
2. Advances IP only if the callback did not perform a jump

### SelectorNode (Execution AST)
**File:** `Ast/Instruction/ControlFlow/SelectorNode.cs`
Represents selector dispatch points in execution AST. It is intentionally a no-op execution node; successor selection remains handled by CFG runtime signature matching.

### InvalidInstructionNode
**File:** `Ast/Instruction/ControlFlow/InvalidInstructionNode.cs`
Carries the `CpuException` payload into execution AST and delegates to `HandleCpuException` at runtime.

### CpuidNode
**File:** `Ast/Instruction/ControlFlow/CpuidNode.cs`
Represents CPUID execution in AST and routes to helper fault behavior (`ExecuteCpuid`).

### ExecuteCallback in InstructionExecutionHelper
New helper method that combines callback execution with conditional IP advancement. Used by `CallbackNode` via reflection in `AstExpressionBuilder`.

### BitwiseAstBuilder.ByteSwap
Builds the full 32-bit byte-swap expression:
`(v >> 24) | ((v >> 8) & 0x0000FF00) | ((v << 8) & 0x00FF0000) | (v << 24)`

### ControlFlowAstBuilder.ConditionalInterrupt
Creates an `IfElseNode` that conditionally calls an interrupt vector on a boolean condition, with IP fallthrough when the condition is false. Used by INTO (InterruptOverflow).

---

## 🏗️ Build Status

**Last Build:** ✅ Success (0 warnings, 0 errors)
**Test Status:** ✅ 1018 passed, 1 skipped (TestCpu - AST execution not yet enabled)

---

## 📚 References

- [Implementation Plan](code-review-findings-issue-2-plan.md)
- [Code Review Findings](code-review-findings.md) - Issue #2
- [AstBuilder Helper Methods](src/Spice86.Core/Emulator/CPU/CfgCpu/Ast/Builder/AstBuilder.cs#L52-L98)
