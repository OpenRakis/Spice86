---
name: spice86-override
description: "Step-by-step workflow for writing C# code overrides in Spice86. Use when: creating IOverrideSupplier, defining function overrides with DefineFunction, hooking instructions with OverrideInstruction/DoOnTopOfInstruction, building MemoryBasedDataStructure accessors, or debugging override registration and CFG jump behavior."
---

# Spice86 Code Override Workflow

## Prerequisites

- Decompiled C code from Ghidra (via spice86-ghidra-plugin)
- Dumped memory + CFG from a prior run (`spice86dumpMemoryDump.bin`, `spice86dumpExecutionFlow.json`)
- Understanding of the target function's role, inputs, outputs, and side effects

## Step 1: Create the Override Supplier Class

```csharp
namespace Spice86.Tests.Overrides;

using Spice86.Core.CLI;
using Spice86.Core.Emulator.Function;
using Spice86.Core.Emulator.Machine;
using Spice86.Logging;

public class MyOverrideSupplier : IOverrideSupplier {
    public IDictionary<SegmentedAddress, FunctionInformation> GenerateFunctionInformations(
        ILoggerService loggerService, Configuration configuration, 
        ushort programStartSegment, Machine machine) {
        return new MyOverrides(new(), machine, loggerService, configuration).FunctionInformations;
    }
}
```

Register at launch: `-o MyOverrideSupplier -u true`

## Step 2: Create the Helper Class

```csharp
public class MyOverrides : CSharpOverrideHelper {
    public MyOverrides(IDictionary<SegmentedAddress, FunctionInformation> funcInfos,
                       Machine machine, ILoggerService logger, Configuration config)
        : base(funcInfos, machine, logger, config) {
        
        // Register functions (full control)
        DefineFunction(0xF000, 0xA1E8, MyFunction_F000_A1E8_FA1E8);
        
        // Replace instructions entirely
        OverrideInstruction(0xF000, 0xFFF0, MyInstructionOverride);
        
        // Hook: run custom code, then continue with original
        DoOnTopOfInstruction(0xF000, 0xFFF3, MyInstructionHook);
    }
```

## Step 3: Implement Functions

```csharp
    public Action MyFunction_F000_A1E8_FA1E8(int loadOffset) {
        // Read state via State, Stack, Memory indexers
        ushort ax = State.AX;
        ushort ds = State.DS;
        
        // Write state
        State.AX = (ushort)(ax + 1);
        
        // Return control to interpreter or jump elsewhere
        return NearRet();      // Return to next instruction after call
        // or FarRet();         // Far return with CS:IP
        // or FarJump(0x1234, 0x5678);  // Jump to specific segment:offset
        // or Hlt();            // Halt execution
    }
```

**Key rules:**
- Function name must match the pattern `MyFunction_<segment>_<offset>_<linear>`
- Signature is always `public Action <name>(int loadOffset)`
- Return an `Action` that transfers control back
- Use `loadOffset` for stack-based parameter extraction when needed

## Step 4: Implement Instruction Overrides

```csharp
    public Action MyInstructionOverride(int loadOffset) {
        // Override replaces the instruction at its address
        // Must explicitly transfer control or halt
        State.AX = 0;
        return NearRet();
    }
    
    public Action MyInstructionHook(int loadOffset) {
        // Hook runs before the original instruction
        // Return to continue with original behavior
        _loggerService.Info("Hooked at F000:FFF3");
        return ContinueWithOriginal();
    }
```

**Key rules:**
- Override = replaces the instruction entirely (CFG jumps here instead of original)
- Hook = runs before, then continues with original (CFG sees both)
- Both must return an `Action` — never fall through

## Step 5: Build Memory Data Structures

```csharp
public class GlobalsOnDs : MemoryBasedDataStructureWithDsBaseAddress {
    public GlobalsOnDs(IByteReaderWriter memory, SegmentRegisters segmentRegisters) 
        : base(memory, segmentRegisters) { }
    
    public int GetDialogueCount47A8() => UInt16[0x47A8];
    public void SetDialogueCount47A8(int value) => UInt16[0x47A8] = (ushort)value;
}

// Usage in a function:
var globals = new GlobalsOnDs(Memory, State.SegmentRegisters);
globals.SetDialogueCount47A8(42);
```

**Variants:** `MemoryBasedDataStructureWithCsBaseAddress`, `WithSsBaseAddress`, `WithEsBaseAddress`

## Step 6: Register in the Helper Constructor

```csharp
    public MyOverrides(...) : base(funcInfos, machine, logger, config) {
        DefineFunction(0xF000, 0xA1E8, MyFunction_F000_A1E8_FA1E8);
        OverrideInstruction(0xF000, 0xFFF0, MyInstructionOverride);
        DoOnTopOfInstruction(0xF000, 0xFFF3, MyInstructionHook);
        
        // For computed jumps, use JumpDispatcher
        DefineFunction(0x1234, 0x5678, DispatchJump_1234_5678);
    }
    
    public Action DispatchJump_1234_5678(int loadOffset) {
        ushort target = State.BX; // computed from register state
        return JumpDispatcher(target);
    }
```

## Step 7: Test & Verify

```bash
# Run with overrides
dotnet run --project src/Spice86 -- -e game.exe -o MyOverrideSupplier -u true

# With heavy logging to trace override execution
dotnet run --project src/Spice86 -- -e game.exe -o MyOverrideSupplier -u true --CpuHeavyLog true --CpuHeavyLogExpressions "AX=AX" "BX=BX"

# Verify CFG shows override jumps (when CodeOverridesActive, CFG is not representative of original asm)
```

## CSharpOverrideHelper API Reference

### State Accessors
| Property | Type | Description |
|----------|------|-------------|
| `State` | `CpuState` | CPU register state (AX, BX, CX, DX, DS, ES, CS, IP, SP, BP, SI, DI, flags) |
| `Stack` | `IStack` | Stack operations (Push, Pop, Peek) |
| `Memory` | `IByteReaderWriter` | Segmented memory access |
| `UInt8[addr]` | `byte` | Byte read at linear address |
| `UInt16[addr]` | `ushort` | Word read at linear address |
| `UInt32[addr]` | `uint` | Dword read at linear address |

### Control Flow Returns
| Method | Behavior |
|--------|----------|
| `NearRet()` | Return to next instruction after call (IP decremented by caller) |
| `FarRet()` | Far return with CS:IP from stack |
| `NearCall(addr)` | Near call to linear address |
| `FarCall(seg, off)` | Far call to segment:offset |
| `FarJump(seg, off)` | Unconditional far jump |
| `Hlt()` | Halt execution |
| `ContinueWithOriginal()` | For hooks — continue with original instruction |
| `JumpDispatcher(targetOffset)` | Dispatch to computed offset in current DS |

### Override Registration Methods
| Method | Behavior |
|--------|----------|
| `DefineFunction(seg, off, actionFactory)` | Full function replacement (CFG jumps here) |
| `OverrideInstruction(seg, off, action)` | Replace single instruction (CFG sees override) |
| `DoOnTopOfInstruction(seg, off, action)` | Hook before original (CFG sees both) |

## Common Pitfalls

1. **Generated vs hand-written overrides**: Hand-written use `DefineFunction` (interpreter fallback). Generated code uses `DefineFunctionWithoutInterpreterFallback` — transfers must land on another override or expected return address, never fall back to interpreter.

2. **CodeOverridesActive = true**: When active, CFG and execution flow dumps are NOT representative of original asm. The CFG jumps between overrides.

3. **SegmentedAddress linear property**: Can roll over incorrectly. Use `IMmu.TranslateAddress` for address translation instead of computing from `.Linear`.

4. **No optional parameters**: Override methods must have explicit signatures. No default values.

5. **One top-level type per file**: Each override class goes in its own file.

## Verification Checklist

- [ ] OverrideSupplier implements IOverrideSupplier
- [ ] Helper class extends CSharpOverrideHelper
- [ ] All DefineFunction names match `MyFunction_<seg>_<off>_<linear>` pattern
- [ ] Every override/hook returns an Action (never falls through)
- [ ] Memory access uses proper data structure variants, not raw addresses
- [ ] No sync-over-async patterns in override code
- [ ] Test with `-u true` and verify CFG shows expected jumps
- [ ] Run full test suite: `dotnet test tests/Spice86.Tests --filter-not-class '*SingleStepTest*'`
