# C# Code Generator

Turns a discovered CFG (control flow graph) into a compilable C# override class that replaces the
emulated assembly at runtime. [codegenIdioms.md](codegenIdioms.md) shows small ASM programs and the
C# the generator produces for them.

## What it does

After Spice86 runs a DOS program, it has a complete CFG of every instruction that executed. The
generator takes that graph and produces a set of `.cs` files where each function partition becomes
a C# method: gotos instead of jumps, method calls instead of `CALL`, typed register access instead
of raw bytes.

The generated code is exact about what was observed and loud about what was not: it reproduces the
behaviour discovery saw, and throws `FailAsUntested` on any path discovery never took.

## Non-goals

Generated C# is not expected to trigger emulator or debugger breakpoints. Matching the
interpreter's breakpoint delivery, count, order or debugger-visible register snapshots is not a
correctness requirement. Diagnostic bookkeeping such as the dispatcher's last-port fields is also
outside the generated-code contract. A device operation, including a recognized packed VGA word
write, may bypass that bookkeeping when it preserves the behavior the emulated program depends on.

This does not relax program semantics: register and flag results, memory and device effects,
hardware interrupts and scheduled events still matter.

## Pipeline

`CfgCSharpGenerator.Generate` runs these stages:

1. `GeneratorAnalysis` names partitions and nodes and maps every edge to its partition transfer.
2. `CfgGeneratorContext` freezes the lookup table every later stage reads: which partition owns a
   node, method names, segment variables, entries, successor edges.
3. `GenerationPlanBuilder` builds the plan: segment fields, signature fields, override
   registrations, and one `MethodPlan` per partition (block order, labels, node plans).
4. `IrMethodEmitter` lifts each method plan to the IR, runs the IR passes, lowers the blocks and
   structures them into a method AST. The summary of every followed program target (calls, jumps,
   `int`, `int` targets of installed handlers) is the least fixed point per strongly connected
   component of the followed-target graph, computed callees first; an unknown target or an SCC
   that does not converge within the round cap gets the conservative set and is reported in the
   dump log. `ProgramAnalysis` then computes the program-wide analyses, and a second preparation
   round re-prepares methods under the observed conventions until the consumed facts cover the
   demand. Round-1 preparation, the re-preparation rounds and the two calling-convention fixed
   points run in parallel with `GeneratorOptions.MaxDegreeOfParallelism` (internal, default the
   processor count); merges in plan order, printing and branch-probe generation stay sequential
   (probe numbering is part of the output). Components used during preparation are immutable after
   construction or locked (`HardwareInterruptEffectBuilder`, the call-landing table of
   `TransferEmitter`, `DumpProgressReporter`).
5. The generator analyzes and prints each method, then the class parts: supplier, segment and
   signature fields, constructor, globals classes, table indexers, and splits the methods into the
   per-segment files.

The result is a `GeneratedCSharpProgram`: the files of one compilable override plus its manifest
(`manifest.json` written next to the sources at every dump: per method its entries and typed
parameters and outputs, the instruction address map, and the probe sites of a probes build).

## Key concepts

### Partition = method

Each partition becomes one C# method. Transfers between partitions become
`return OtherMethod(loadOffset);`. A single-entry partition prints `public virtual Action M()` and
is referenced through `Entry(M)`, a delegate cache that gives every site the same delegate
identity. Multi-entry partitions keep the `Action M(int loadOffset)` signature with an entry
dispatch `switch`.

### The IR

One method is lifted to one `IrMethod`: flat `IrBlock`s in layout order, each holding
`IrStatement`s and ending in one `IrTerminator` (`IrJump`, `IrBranch`, `IrSwitch`, or an `IrExit`
transfer out of the method). Explicit control flow of instruction ASTs becomes blocks and edges;
everything else stays one helper node.

Every statement and terminator stores one `IrEffects`: `Reads`, `MustWrite`, `MayWrite` over the
register bytes, flags and direction flag, `TokenReads`/`TokenWrites` over the `Memory` and `Io`
tokens, plus `MayFault` and `DeliversInterrupts`. The SSA, the liveness and every legality check
read these fields; no pass keeps its own list of what an instruction may do.

`IrSsa` is a sparse analysis-only side table of value versions with phis at the dominance
frontiers. A pass edits statements through `IrEditor`, the one write path, which keeps the SSA
records current or marks them stale. `SameVersions(reads, tokens, from, to)` is the one legality
rule every pass shares: an expression may move or merge between two places when every value it
reads, tokens included, has the same version at both.

### Method AST and printing

The lowered blocks are structured into one AST per method: runtime nodes shared with the
interpreter plus code-generation nodes (`InstructionBoundaryNode`, `GotoNode`, `SwitchNode`,
`CpuFaultTryCatchNode`, `FailAsUntestedNode`, ...) visited through `ICodeGenerationAstVisitor<T>`.
`CSharpStatementPrinter` and `CSharpExpressionPrinter` are the only producers of generated code
text. Branches the structurizer cannot nest and irreducible regions stay `goto`.

### Conventions, typed signatures and register locals

`IrCallingConventions` answers, per method entry, which machine values it reads (inputs), which a
return may change (changes) and which of those its callers observe (outputs). It runs as one
program-wide fixed point, twice: the code demand drives the `Inputs:`/`Outputs:`/`Changes:` doc
comment and the typed signatures, the observed demand adds what the final machine state reads.
A method whose signature is fully typed prints as `(outputs) = MethodName(parameters)` with the
register operands becoming C# parameters and tuple elements.

A register web whose SSA versions stay consistent across a region prints as a C# local
(`word1 = ...; ... CX = word1;`) instead of the register property, with write-backs at the points
the machine register must hold the value again. Values are named by the first rule that yields a
name: a sink (a service argument, a typed callee parameter), a source (a named output, a frame
slot), a role (`index`, `count`, `segment`, `result`), or the generic fallback (`word1`, `byte2`).

### Edge lowering

Every jump, call and return in the original assembly becomes a resolved CFG edge that
`TransferEmitter` turns into C#: fallthrough, `goto` of a same-method block, `return` of another
method, a near or far `NearCall`/`FarCall` with its observed continuation, an `InterruptCall` of a
program handler, or `return NearRet();` of a return.

## Passes

In pipeline order; the SSA is built once before the first group and rebuilt once after
`IrBranchSimplification`:

| pass | what it does |
|---|---|
| `IrPlainTemporaryPropagation` | inlines a temporary into its same-block, same-instruction readers and drops unread declarations |
| `IrInstructionIdioms` | prints `xchg` as a tuple swap, `mul`/`imul` as one multiplication call, composes byte-half pairs and XOR swaps |
| `IrFlagPropagation` | prints a conditional jump as the relation of the ALU setter that defines its flags |
| `IrEntryDirectionSteps` | folds the direction-flag selects into `SI += n` / `DI -= n` where every path into a partition entry agrees on the direction |
| `IrConstants` | sparse conditional constant propagation over the general registers; substitutes the known constants and removes dead declarations |
| `IrInterruptServiceCalls` | prints an `int` site bound to a declared service implementation as one typed `ServiceCall` |
| `IrBranchSimplification` | folds boolean identities and constant conditions, removes unreachable blocks |
| `IrIfConversion` | folds a one-instruction branch with pure single-value arms into `x = c ? a : b` |
| `IrJumpThreading` | folds empty jump blocks into their predecessors' edges |
| `IrRepCompareLowering` | compares the operands of a `rep cmps`/`scas` directly when the flags are unused |
| `IrAluLowering` | expands an ALU call into plain operators, keeping the flag writes still needed |
| `IrDeadFlagWrites` | removes a `clc`/`stc`/`cmc`/`sahf` whose flag version is dead |
| `IrStoreForwarding` | replaces a memory read by the value of an earlier covering store the alias analysis proves equal |
| `IrOverwrittenStores` | removes a store a later store covers before anything can observe it |
| `IrDeadRegisterWrites` | removes a register write whose defined versions are all dead |
| `IrExpressionPropagation` | merges a single-use assignment into its reader, bounded in size and chain length |
| `IrConstantFolder` | folds constant chains and algebra on the expressions merging creates |
| `IrJoinedMemory` | joins adjacent byte or word memory accesses into one wider access |
| `IrDeviceOperations` | prints a constant-port sequence matching a device protocol as one call on the device |
| `IrDoublePrecision` | joins a register pair's 32/64-bit operation sequence into one wide declaration and two half writes |
| `IrWideComparisons` | packs the tests of a 32-bit comparison spread over several jumps into one wide comparison |
| `IrSpeculativeGuardMerge` | merges the speculative guards of a straight-line run into one guard when nothing in between can write the code bytes |

## Generation policy and options

`GenerationPolicy` is the immutable set of named relaxations the whole run shares; the default is
strict mode with no relaxations. Each relaxation is one command-line option:

- `AssumeUnknownMemoryIsRam` (`UnknownMemoryIsRam`): memory the analysis cannot
  classify is treated as ordinary RAM.
- `AssumeAccessesStayInSegment` (`AccessesStayInSegment`): an indexed access whose
  offset range is unproved is treated as staying inside its segment.
- `AssumeAbandonedStackSlotsUnobserved` (`AbandonedStackSlotsUnobserved`): the bytes
  of a proved private typed stack argument are not observed after the call returns, so the site
  reserves stack space without writing them.
- `AssumeSavedSlotsWrittenOnlyByPops` (`SavedSlotsWrittenOnlyByPops`): a slot
  written only by a register's save push survives call boundaries, so the restoring pop proves the
  pushed bytes.

An assumption fills missing knowledge and never overrides a contrary fact: a constant offset or a
proved range is a fact and never relaxed. The supplier file's header prints the run's relaxations
as `// Relaxations: none` or the enabled names.

`GeneratorOptions` carries the other switches. `PrintAsmComments` keeps the `// SSSS:OOOO asm`
comment of every instruction (off by default; the manifest's address map keeps the navigation).
The command line selects these through one option, `--GeneratedCode <LIST>`: a comma list of
`+name` / `-name` entries (no sign enables) applied left to right. The names are AsmComments,
BranchProbes, ServiceCallNotifications, DeviceOperations, the four relaxations above and the
`CheckBuild` preset, which enables the service-call notifications and the branch probes and splits
device operations back into single port accesses, so a check build can be compared access-by-access
with the emulated original; a later entry may override part of the preset.

## How to add a pass

An IR pass reads the `IrMethod`, edits through `IrEditor` and declares whether it needs the SSA
current. The pipeline rebuilds the SSA when an edit marked it stale and, in verify mode, compares
the maintained SSA with a fresh build after every pass. A pass that moves or merges an expression
proves `SameVersions` on the values it reads. New legality conditions go into the pass's own XML
doc: the condition and what goes wrong without it.

## How the generated code is tested

- **Oracles**: every catalogue scenario is an ASM program with an oracle derived from its source
  (result bytes or a test port). The interpreted run must match it.
- **Interpreted-vs-generated differential**: the generated program is compiled and run against the
  same oracle, and its final machine state is compared with the interpreted run's. A difference in
  a byte either run could have written fails the gate.
- **Check lines**: scenarios carry `Contains`/`DoesNotContain` assertions on the program-only
  generated text, each proved to fail when the feature line is wrong.
- **Readability bounds**: per-scenario bounds on generated-code metrics (statement counts, goto
  counts, expression sizes) live in `Resources/GeneratedCodeReadability/bounds.tsv`, one row per
  scenario key and part. A missing minimum means 0, a missing optional maximum means no bound, and a
  regression past a present bound fails. The bounds tighten only through the deliberate writer pass
  described in AGENTS.md.
- `doc/codegenIdioms.md` is rendered by a test from the idiom fixtures; the committed document must
  match the rendering.

## Untested paths

If an edge was never traversed during discovery, the generated code throws `FailAsUntested(...)` at
that point: generated code either does exactly what was observed or fails loudly. A conditional arm
without a CFG edge fails as untested, and a `VerifySpeculativeEntryOrFail` guard re-reads the bytes
of each speculative instruction before it runs (merged into `VerifySpeculativeRunOrFail` for a
straight-line run). A far call dispatches on the observed target pairs in an `if`/`else if` chain
whose final `else` fails as untested.

## CPU fault handling

Instructions that faulted during discovery are wrapped in `try/catch (CpuException)`. The `catch`
calls `DispatchCpuFault` with the observed handler arms and resume targets; the helper performs the
fault entry sequence, invokes the handler partition, and the resumed address is checked against the
observed list — a handler returning anywhere else fails as untested. When the observed handlers and
resume targets all resolve to partition entries, the faulting statement reads only their entry
inputs plus the frame registers instead of every value, so dead writes on the fault path can still
be removed.

## Self-modifying code

Instructions at the same address with different bytes are handled through selector nodes: the
generated code checks which byte signature currently matches memory and branches to the
corresponding variant. The speculative guards of a run detect modification of the speculated bytes;
where the run merge cannot prove a mid-run write disjoint from the code bytes, the per-instruction
guard stays.

## Reusable analyses

Before writing a new traversal, graph or naming helper, check the shared ones: `BlockGraph<TBlock>`,
`DominatorTreeBuilder`, `NaturalLoopForest<T>`, `ForwardDataflowSolver`/`BackwardDataflowSolver`
under `ControlFlowGraph/Analysis/`, `DepthFirstOrdering<T>` under `Graph/`, and `CodeGenerationAst`,
`CompletesNormallyVisitor`, `GotoTargetCollector` under `CfgCodeGeneration/Ast/`.

The dependency rule is `CfgCodeGeneration -> ControlFlowGraph.Analysis -> Graph`, never the other
way.
