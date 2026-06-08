# C# Code Generator

Turns a discovered CFG (control flow graph) into a compilable C# override class that replaces the emulated assembly at runtime.

## What it does

After Spice86 runs a DOS program, it has a complete CFG of every instruction that executed. The generator takes that graph and produces a `.cs` file where each function partition becomes a C# method — gotos instead of jumps, method calls instead of `CALL`, typed register access instead of raw bytes.

## Pipeline overview

```
┌─────────────────┐
│  CFG from CPU   │  (discovered during emulation)
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ GeneratorAnalysis│  Assign names, labels, validate node shapes
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ CfgGeneratorContext │  Frozen lookup table (partition → method name, node → label, etc.)
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ GenerationPlanBuilder │  Decide what to write: fields, registrations, method order
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  MethodEmitter  │  Walk nodes, lower AST, wrap faults, render
│  ┌────────────┐ │
│  │AstEmitter  │ │  Turn each AST node into C# (expressions + control flow)
│  │  ┌───────┐ │ │
│  │  │Transfer│ │ │  Pick goto / return / dispatcher for each edge
│  │  └───────┘ │ │
│  │  ┌───────┐ │ │
│  │  │Fault  │ │ │  Wrap in try/catch when a CPU fault was observed
│  │  └───────┘ │ │
│  └────────────┘ │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│ EmittedCodeRenderer │  Dumb printer: lines, braces, indentation
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  .cs source file │
└─────────────────┘
```

## Key concepts

### Partition = Method

The CFG is split into partitions (groups of blocks that form a logical function). Each partition becomes one C# method. Transfers between partitions become `return OtherMethod(loadOffset);`.

### Plan then write

The generator works in two phases:
1. **Plan** — decide everything up front (method order, labels, entry dispatch, segment fields)
2. **Write** — walk the plan and emit code mechanically, no re-analysis needed

### Edge lowering

Every jump/call/return in the original assembly becomes a "resolved CFG edge" — a source node, a target node, and metadata about what kind of transfer it is. The `TransferEmitter` turns each edge into the appropriate C# construct:

| Edge kind | Generated C# |
|-----------|-------------|
| Same method, next node | *(nothing — fallthrough)* |
| Same method, other node | `goto label_XXXX;` |
| Cross-partition | `return OtherMethod(0xOFFSET);` |
| Cyclic flow | `JumpDispatcher.Jump(...)` + `goto entrydispatcher;` |
| Near/far call | `NearCall(seg, offset, TargetMethod);` + continuation |
| Return | `return NearRet(...);` / `return FarRet(...);` |

### Untested paths

If an edge was never traversed during discovery, the generated code throws `FailAsUntested(...)` at that point. This makes it safe to run the generated code: it either does exactly what was observed, or fails loudly on an unobserved path.

### CPU fault handling

Instructions that triggered a hardware exception (e.g. divide by zero) during discovery get wrapped in `try/catch(CpuException)`. The catch block reproduces what the real CPU does: push flags, push return address, clear interrupts, then jump to the interrupt handler.

### Self-modifying code

Instructions at the same address with different bytes (self-modifying code) are handled through selector nodes. The generated code checks which byte signature currently matches memory and branches to the corresponding variant.

## File layout

```
CfgCodeGeneration/
├── CfgCSharpGenerator.cs       Top-level orchestrator
├── GeneratorAnalysis.cs         First pass: names, labels, validation
├── CfgGeneratorContext.cs       Shared lookup table
├── GenerationPlanBuilder.cs     Builds the plan (what to emit)
├── MethodEmitter.cs             Drives per-method emission
├── CSharpAstEmitter.cs          Lowers control-flow AST nodes
├── CSharpExpressionEmitter.cs   Lowers pure expressions
├── TransferEmitter.cs           Picks goto/return/dispatcher per edge
├── CpuFaultWrapper.cs           try/catch for CPU faults
├── EmittedCodeRenderer.cs       Prints statements to text
├── CSharpSourceWriter.cs        Indentation / brace management
├── CfgCSharpDumper.cs           Adapter for state serialization
├── GeneratedProjectScaffolder.cs  Emits .csproj + Program.cs
└── Model/
    ├── Plan/                    What to emit (decided before writing)
    ├── Statement/               Statement tree (the intermediate representation)
    └── *.cs                     Fragments, edges, emitted code algebra
```
