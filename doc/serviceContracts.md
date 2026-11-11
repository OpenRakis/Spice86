# Interrupt service contracts

Every built-in interrupt handler declares, per service it dispatches, a
`ServiceContract` describing what the service reads and writes: which
registers and flags it needs before the call and which it changes, whether
emulated memory or I/O ports may be touched, and the name of the implementing
method. The contracts are meant for the code generator, which can then treat a
call to a built-in service as a callee with a known convention.

## Contract model

Contract types live in
`src/Spice86.Core/Emulator/InterruptHandlers/Common/Contracts/`:

- `InterruptServiceEntry`: one record per declared service: `SelectorValue`,
  `Name` (the C# method name), `Contract`, `StringArgument`
  (`ServiceStringArgument`: `None`, `AsciiZ`, `DollarTerminated` — the kind of
  string the service reads at `DS:DX`) and `ExecutionPath`
  (`ServiceExecutionPath`: `Stub` or `Direct`). `InterruptServiceEntry.Unknown`
  is the entry of a service whose effects cannot be described.
- `ServiceContract`: the register/memory/I/O effects only —
  `Registers` (a `CallEffect` of `Reads`/`Writes` `CpuValues` bit fields,
  flags included), `Memory` (`ServiceMemoryEffect`: `None`, `Read`, `Write`,
  `ReadWrite`) and `Io` (whether the service accesses I/O ports).
  `ServiceContract.Unknown` is the single unknown-effects contract.
- `ServiceSelector`: describes how the service is selected: `Ah`, `Ax` or
  `Al` for handlers that dispatch on a register. Each `IInterruptHandler`
  exposes `Services`, a `HandlerServices` — `SingleService(entry)` for
  whole-vector handlers, `SelectedServices(selector, entries)` for handlers
  that dispatch on a register.

Contracts are declared next to the method that implements the service: the
`[InterruptService]` attribute carries the selector value (except on
whole-vector handlers), the `Reads`/`Writes` masks, `Memory`, `Io`, an
optional `Name` (defaults to the method name), `StringArgument`,
`ExecutionPath`, or `Unknown = true` (which yields a named entry carrying
`ServiceContract.Unknown`; `Unknown` with `Direct` is rejected).
A registration without any attribute — an `AddAction(index, action)` —
declares an unknown-effects entry named after the action method.

## Unknown contracts

`ServiceContract.Unknown` means the effects of the service cannot be described
precisely: it conservatively reads and writes every register and flag, memory
and I/O ports. It is used for hardware IRQ handlers that are not
program-callable services (INT 8h/9h/0Bh-0Fh/72h-74h), for handlers whose
behavior mutates emulator structures outside the CPU-visible contract (INT
20h/22h/23h/25h/26h), and for individual services that block on input or
read inputs the test cannot pin (for example the console input functions of
INT 21h, the INT 16h keystroke functions whose in-RAM stub loops until a key
arrives (AH=00h has no contract because the stub never reaches the C#
handler; AH=10h is Unknown), INT 13h AH=05h, INT 2Fh AH=15h).

A contract describes the effects the service has when it returns to its
caller, so a service that may also terminate the program or transfer control
elsewhere can still have a precise contract: on that path the typed helper's
return-address check unwinds exactly like the untyped `InterruptCall`, and
the contract's sets are only used when control comes back.

## Differential contract tests

`tests/Spice86.Tests/InterruptHandlers/` contains a test per handler group.
Each test case picks a service by vector and selector value, builds two fresh
machines with identical fixed inputs (the declared `Reads` registers and
flags, plus whatever `Setup` arranges) and different randomized values in
every undeclared register and flag, pushes a fake interrupt frame, and runs
the service's `Run()` directly. A precise contract holds if, across the two
runs:

- every register in `Writes` has the same value, and every register not in
  `Writes` is preserved,
- the frame flags outside `FlagsWritten` are preserved,
- memory 0-0x100000 outside the pushed frame is unchanged when `Memory` has no
  write, and identical across runs when it has `Write`/`ReadWrite`,
- no I/O port access happens when `Io` is false (checked with an
  `IO_ACCESS` breakpoint), and no memory read or write beyond what `Memory`
  allows happens (checked with `MEMORY_READ`/`MEMORY_WRITE` breakpoints).

A fixed `Setup` hook per case pins inputs needed to reach a deterministic path
(for example an invalid drive letter to force an error path).
`ServiceContractCoverageTest` checks that the declared non-`Unknown` contracts
and the test cases cover exactly the same set of services, and the per-vector
`*ServiceContractTest` classes supply the cases.

## Typed service helpers

For every precise contract there is a typed C# helper that generated code
calls instead of `InterruptCall(cs, ip, vector)`. Helpers live in
`src/Spice86.Core/Emulator/ReverseEngineer/InterruptServices/`: one public
class per vector (for example `DosServices` for INT 21h, exposed as
`helper.Dos`), deriving from `InterruptServiceGroup<THandler>` — the generic
argument is the installed handler type the group serves, resolved lazily on
the first call through `CSharpOverrideHelper.GetInterruptHandler<THandler>`
(an absent optional handler fails as untested only when actually called).
`InterruptServiceGroups` maps each handler type to the name of its
`CSharpOverrideHelper` property by reflecting the public group properties.

A helper resolves its service's `InterruptServiceEntry` from
`Handler.Services` by `nameof` of its own method — no vector, selector or
name is repeated. On the `Stub` path (`Call`) the helper checks first that
the vector still points to the stub installed for the built-in handler
(`CheckInterruptServiceVector`, comparing the installed handler address with
the live vector table entry); a program that hooked the vector makes it throw
`FailAsUntested` before any register is written. It then writes its
parameters into the registers and flags the service reads, writes the
selector register, and runs `InterruptServiceCall`, which does what
`InterruptCall` does. The outputs stay in the registers and flags, as after
the `int` instruction, and the helper also returns them. Direct helpers
instead return the register outputs for the caller to assign; their frame
restores the output flags.

On the `Direct` path (`CallDirect`/`CallDirectWithCarry`), the service's
installed stub is exactly `callback n; iret`
(`FE 38 <vector> 00 CF`) — a stub with real behavior is never declared
`Direct`. `DirectInterruptServiceCall` checks the vector and those stub
bytes, pushes the same frame as `ExecuteInterruptCall` (FLAGS, IF cleared,
return CS:IP), sets CS:IP to the installed callback address and invokes the
service's State-independent implementation directly — no input or selector
register is stored, no output is read back from `State`. The implementation
returns the outputs; `writeFrameFlags` updates the output flag bits on the
stacked FLAGS word and `InterruptRet` restores the frame exactly like the
stub's `iret`. A hooked vector or a modified stub byte throws
`FailAsUntested` before anything is written.

The services currently declared `Direct`: INT 10h (VGA BIOS services),
INT 11h (`GetEquipmentList`), INT 12h (`GetConventionalMemorySize`),
INT 13h (disk services), INT 14h (serial services), INT 15h (system BIOS
services), INT 17h (printer services), INT 1Ah (clock/RTC services),
INT 24h (`HandleCriticalError`), INT 28h (`Idle`), INT 2Ah and INT 2Fh
(multiplex services), INT 33h (mouse services), INT 67h (EMS services) and
INT 70h (`HandleInterrupt`).
All DOS (INT 21h) services stay `Stub`: their stub carries selector
comparisons the direct shape cannot replace, and their generated helpers keep
the CMP/JZ preamble. Each attributed INT 21h service that performs a
contained operation is split the same way: a State-independent public core on
`DosInt21Handler` takes the service's explicit parameters and returns its
outputs (`DosFileOperationResult` for the carry-flag services, plain scalars
or tuples for the rest — `WriteString`, `SetCurrentPsp`,
`SetDiskTransferAddress` and `SetInterruptVector` return `void`), while the
annotated interrupt wrapper decodes `State` inputs, calls the core and
applies the outputs. The FCB services delegate to the manager exposed as
`DosInt21Handler.FileControlBlocks`. `DosServices` also
exposes the cores as plain high-level overloads (`OpenFile`, `Print`,
`ReadTime`, `IoControl`, ...) for
callers that want the raw DOS operation: they never run the interrupt, never
write registers, and never check the frame — the generated `int` adapter
still goes through the stub.

The keyboard services (INT 16h) keep `Stub` too: their stub carries a
nontrivial body. `KeyboardInt16Handler` nevertheless exposes
State-independent cores (`SetTypematicRateAndDelay`, `ReadShiftFlags`,
`StoreKeystroke`, `ReadKeystrokeStatus`, `Unsupported`) for direct use
through `KeyboardServices` raw overloads, and `UserTimerServices.Tick`
forwards to `DummyInt1CHandler.Tick` — while its generated adapter stays
`Stub` because the stub body is a bare `iret`.

The parameter list of a helper is `ushort expectedReturnCs, ushort
expectedReturnIp` followed by `ServiceCallSignature.Parameters(contract,
selector)`: the declared `Reads` minus the selector, stack and frame
registers (`Ss`, `Sp`, `Cs`, `EspHigh`), in this order: segment registers
(`ushort ds`, `es`, `fs`, `gs`), the general registers (`ushort ax`/`bx`/`cx`/`dx`
when both halves are read, `byte al`/`ah`/... when only one half is), `ushort
si`/`di`/`bp`, the upper words (`ushort eaxHigh`, ...), then one `bool` per
`FlagsRead` flag (`carryFlag`, `parityFlag`, `auxiliaryFlag`, `zeroFlag`,
`signFlag`, `overflowFlag`, `directionFlag`, `interruptFlag`).

The return type of a helper is `ServiceCallSignature.Outputs(contract)` — the
declared `Writes`, in the same `CpuValueParameters` order: `void` for no
output, the scalar type for one (`ushort`, `uint` or `bool` for a flag), and
a named tuple in the exact outputs order for more
(`(ushort ax, bool carryFlag)`, `(ushort ax, ushort cx, ushort bx)`). The
helper returns the outputs of the call. Stub helpers also leave outputs in
`State`. Direct helpers return register outputs for the caller to assign;
their frame restores the output flags.

`ServiceHelperEquivalenceTest` proves each helper equals a real `int`: for
every contract test case, machine A executes `int N` written at a scratch
address in the interpreter, and machine B registers at the vector's target an
override — for a `Stub` service one that runs the real stub bytes in the
interpreter (set CS:IP to the stub, `ExecuteNext` until the expected return
address), for a `Direct` service one that throws if the stub is ever reached —
and then calls the helper by reflection. For a `Direct` service the test also
asserts the helper left every register at its pre-call value, then
materializes the original inputs, the selector and the returned outputs into
`State` (the generated caller's observable boundary). Every register, the
FLAGS register and memory 0-0x100000 must be identical afterwards, and the
helper's return value must carry exactly the contract outputs in signature
order (tuples are flattened through `ITuple`).
`ServiceHelperStringCheckTest` checks the literal string overloads read the
bytes at `DS:DX` exactly as the service does, and
`ServiceHelperVectorCheckTest` checks the vector check throws without
changing state — on a hooked vector and, for a `Direct` service, on a
modified stub byte — and that every `Direct` declaration really has the
callback-plus-iret stub shape.

The C16 regression tests cover these specific behaviors:

| Test | Behavior checked |
|------|------------------|
| `ServiceHelperEquivalenceTest.A20UnwrittenOutputsArePreserved` | Each A20 subfunction preserves fields it does not write, including CF for an unknown subfunction. |
| `ServiceHelperEquivalenceTest.VerifySectorsUnwrittenOutputsArePreserved` | Successful fixed-disk verification writes AH=0 and clears CF. |
| `ServiceHelperEquivalenceTest.DisketteOrHddTypeUnwrittenOutputsArePreserved` | Disk-type queries preserve CX/DX outside the fixed-disk success path. |
| `ServiceHelperEquivalenceTest.AllocatePagesUnwrittenOutputsArePreserved` | Failed EMS allocation preserves DX. |
| `ServiceHelperEquivalenceTest.HardwareInfoUnwrittenOutputsArePreserved` | EMS hardware-information subfunctions preserve unwritten AH/BX/DX. |
| `ServiceHelperVectorCheckTest.DirectServiceFailsBeforeWritingAnythingOnHookedVector` | A replaced vector is rejected before state changes. |
| `ServiceHelperVectorCheckTest.DirectServiceFailsBeforeWritingAnythingOnModifiedStubBytes` | Changed stub bytes are rejected before state changes. |
| `ServiceHelperVectorCheckTest.EveryDirectServiceHasTheCallbackAndIretStubShape` | Every direct declaration has the required installed stub. |
| `ServiceHelperVectorCheckTest.RawDosIoControlResolvesDuplicatedDosHandlesWithoutWritingRegisters` | Raw IOCTL resolves a duplicated DOS handle, rejects a closed handle and preserves registers. |
| `ServiceHelperVectorCheckTest.InvalidAttributeOperationKeepsCpuExceptionsAtInterruptBoundary` | The raw API returns an error; the interrupt wrapper preserves its legacy exception and CF behavior. |
| `EmsUnitTests.MapUnmapMultipleHandlePagesContinuesAfterAnInvalidEntry` | An invalid mapping entry does not prevent later valid entries from being mapped. |
| `IrInterruptServiceCallsTest.EffectsMustWriteTheParametersAndSelector` | Stub effects include the helper's input and selector stores. |
| `IrInterruptServiceCallsTest.EffectsReadThePreservedOutputsTheCallMustWrite` | Conditional outputs retain their pre-call values. |
| `IrInterruptServiceCallsTest.DirectEffectsWriteOnlyTheResultTargets` | Direct effects do not invent input or selector stores. |
| `CfgCSharpGeneratorLocationsTest.CallResultTargetDoesNotResolveToTheSelectorConstant` | A returned register remains an assignment target rather than becoming a selector constant. |
| `int_service_calls` ASM, `preserved_output` section | Generated DOS calls preserve AX on the unchanged handle-table-size path. |

## Typed calls in generated code

The code generator prints a typed service call `Group.Method(cs, ip, args)`
instead of `InterruptCall(cs, ip, vector)` when an `int` site meets every
condition:

- **site kind**: the lowering classifies each `int` site
  (`InstructionLowerer.ClassifyInterruptSite`). A site is analysable
  (`BuiltInNotAnalysed`) only when discovery observed at least one target and
  every observed target is the installed stub of the call's vector
  (`HardwareInterruptSetup.ServiceVectors`, one `InterruptServiceVector` per
  vector holding the stub address, the selector and the contracts). A site
  with a redirected return is `RedirectedReturn`, one with no observed target
  or an unresolvable vector is `NotObserved`, one whose targets are all
  program code is `ProgramHandler`, and one that saw a mix is `Hooked`;
- **selector known**: the `IrInterruptServiceCalls` pass (right after
  `IrConstants` in the first pass group) reads the selector register through
  the constant facts of the first SSA; a site whose selector is not a known
  constant stays `InterruptCall` with kind `SelectorUnknown`;
- **precise contract**: the selector value must key a precise contract and
  the vector must have a helper group; otherwise the site stays
  `InterruptCall` with kind `NoContract`.

A typed site becomes an `InterruptServiceCallNode` whose effects are the
contract's (`ServiceContractEffects`): reads are the contract reads minus
the parameter registers and the selector, plus the current value of every
result target (the contract only may-writes the outputs, so a stub path that
preserves one reads its pre-call value), plus SS, SP and every status flag
(the frame push and the `iret` cover them); on the `Stub` path the parameter
registers, the selector and the result targets are must-written (the helper
stores its inputs into `State` itself, so program writes to them before the
call are dead, and the call assigns its return to the outputs), while on the
`Direct` path must-write is the result targets only (the helper stores no
input or selector); the contract writes
and flag writes are may-written; tokens are
`Memory` always plus `Io` when the service does I/O. The call delivers no
hardware interrupt: the `int` frame clears IF and no precise service sets
`State.InterruptFlag` (the contract harness asserts IF stays clear through every
precise contract), so nothing can be delivered inside it; delivery after
the call is the separate `CheckExternalEvents` of the next instruction. Because the call's writes are exactly the contract's, values a
caller expects preserved stay live across it and the writes the service
makes (the parameter and selector stores, dead flags, dead register stores
before the call) are removed by the ordinary dead-write passes.

When the contract has outputs, the call's `Results` carry the output
lvalues themselves - one fresh register or flag node per `Outputs` entry -
and the printed statement assigns the helper's return straight into them:
`(AX, CarryFlag) = Dos.OpenFileOrDevice(...)`, `AL = Dos.GetCurrentDefaultDrive(...)` for a
single output, or the bare invocation for a void contract. The lvalues are
write targets everywhere it matters (`IrAstChildren` exposes them for
occurrence and target discovery but `MapChildren` only maps the argument
reads, `TypedSignatures` and `RegisterLocalPromotion` treat them like an
assignment's left), so no temporary or writeback statement is needed: the
existing register-local promotion can turn an output into a local
(`al = Dos.GetCurrentDefaultDrive(...)` with the usual write-back rules), the SSA's
conditional output versions keep a preserved path linked to its pre-call
value, and the helper is still invoked exactly once.

The runtime safety net is the helper's vector check
(`CheckInterruptServiceVector`): a typed call on a vector the program hooked
after discovery throws `FailAsUntested` before any register is written.

Generated stubs get a one-line comment before each `Callback(...)` whose
callback number has a description (`CallbackDescriptions`, built from
`callbackHandler.AllRunnables` and the `IInterruptHandler`s): `INT 21h: Dos
services selected by AH` for a handler `Run` dispatch, `INT 11h:
Equipment.GetEquipmentList` for a contract-named `Run`,
`// Spice86 callback: TimerInt8Handler.IncTickCounterValue` for a named
callback runnable.

`InterruptSiteCensus` counts the printed `int` sites by kind per generated
program; the test runner appends one line per fixture to
`bin/generated-code/interrupt-site-census.txt` (fixture, total, one count
per kind in enum order).

## Declaring a contract for a new service

Read every path of the service, including what `Run()` does before dispatch
(INT 21h's `SaveCallerStackInCurrentPsp` reads SS:SP and writes memory, so
most INT 21h contracts include `Ss | Sp` and `ReadWrite`). Then:

1. Declare the service on its implementing method with
   `[InterruptService(selector, Reads = ..., Writes = ..., Memory = ...)]`.
2. Include in `Reads` every register the service or the pre-dispatch code
   reads, the selector register included.
3. Include in `Writes` the union of registers written on any path, flag
   writes included: a flag goes there only if the service can change its
   value on the interrupt frame (via `SetCarryFlag` and friends).
4. If no single call shape makes the declared outputs deterministic, use
   `[InterruptService(selector, Unknown = true)]` with a comment saying why,
   instead of declaring a contract that only holds on some paths.
5. Mark `ExecutionPath = ServiceExecutionPath.Direct` only when the service's
   installed stub is exactly `callback n; iret` and the service's
   implementation takes explicit parameters and never reads or writes
   `State` inputs or outputs.
