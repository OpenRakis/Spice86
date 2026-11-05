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

- `ServiceContract`: one record per service: `Name` (the C# method name),
  `Reads` and `Writes` (`ServiceRegisters` bit fields), `FlagsRead` and
  `FlagsWritten` (`ServiceFlags` bit fields over the flags restored by
  `iret`), `Memory` (`ServiceMemoryEffect`: `None`, `Read`, `Write`,
  `ReadWrite`) and `Io` (whether the service accesses I/O ports).
- `ServiceSelector`: describes how the service is selected: `None` for
  whole-vector handlers, `Ah`, `Ax` or `Al` for handlers that dispatch on a
  register. Each `IInterruptHandler` exposes one `Selector` plus a
  `Contracts` map from selector value to `ServiceContract`.
- `ServiceContractRegistry`: populated by `InterruptInstaller` at startup;
  maps each interrupt vector to the handler and its declared contracts.

Contracts are declared next to the dispatch table they describe: each
`AddAction(index, action, contract)` call carries the contract of the service
it registers, and whole-vector handlers call `DeclareVectorContract` once.
`AddAction(index, action)` without a contract declares
`ServiceContract.Unknown`.

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
and the test cases cover exactly the same set of services, and
`ServiceContractRegistryTest` checks the registry wiring.

## Typed service helpers

For every precise contract there is a typed C# helper that generated code
calls instead of `InterruptCall(cs, ip, vector)`. Helpers live in
`src/Spice86.Core/Emulator/ReverseEngineer/InterruptServices/`: one public
class per vector (for example `DosServices` for INT 21h, exposed as
`helper.Dos`), deriving from `InterruptServiceGroup`, and one public method
per precise contract named `contract.Name`. `InterruptServiceGroups`
maps each vector to the name of its `CSharpOverrideHelper` property.

Each helper checks first that the vector still points to the stub installed
for the built-in handler (`CheckInterruptServiceVector`, comparing
`ServiceContractRegistry.TryGetInstalledAddress` with the live vector table
entry); a program that hooked the vector makes it throw `FailAsUntested`
before any register is written. It then writes its parameters into the
registers and flags the service reads, writes the selector register, and runs
`InterruptServiceCall`, which does what `InterruptCall` does. Outputs stay in
the registers and flags, as after the `int` instruction; helpers return
`void`.

The parameter list of a helper is `ushort expectedReturnCs, ushort
expectedReturnIp` followed by `ServiceCallSignature.Parameters(contract,
selector)`: the declared `Reads` minus the selector, stack and frame
registers (`Ss`, `Sp`, `Cs`, `EspHigh`), in this order: segment registers
(`ushort ds`, `es`, `fs`, `gs`), the general registers (`ushort ax`/`bx`/`cx`/`dx`
when both halves are read, `byte al`/`ah`/... when only one half is), `ushort
si`/`di`/`bp`, the upper words (`ushort eaxHigh`, ...), then one `bool` per
`FlagsRead` flag (`carryFlag`, `parityFlag`, `auxiliaryFlag`, `zeroFlag`,
`signFlag`, `overflowFlag`, `directionFlag`, `interruptFlag`).

`ServiceHelperEquivalenceTest` proves each helper equals a real `int`: for
every contract test case, machine A executes `int N` written at a scratch
address in the interpreter, and machine B registers at the vector's target an
override that runs the real stub bytes in the interpreter (set CS:IP to the
stub, `ExecuteNext` until the expected return address) and then calls the
helper by reflection. Every register, the FLAGS register and memory
0-0x100000 must be identical afterwards. `ServiceHelperSignatureTest` checks
by reflection that every precise contract has exactly one helper method with
the signature `ServiceCallSignature` computes, and
`ServiceHelperVectorCheckTest` checks the vector check throws without
changing state.

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
the selector register plus SS, SP and every status flag (the frame push and
the `iret` cover them); the selector
register is must-written (the helper stores the constant itself, so a
program write to it before the call is dead); the contract writes and flag
writes are may-written; tokens are
`Memory` always plus `Io` when the service does I/O. The call delivers no
hardware interrupt: the `int` frame clears IF and no precise service sets
`State.InterruptFlag` (the contract harness asserts IF stays clear through every
precise contract), so nothing can be delivered inside it; delivery after
the call is the separate `CheckExternalEvents` of the next instruction. Because the call's writes are exactly the contract's, values a
caller expects preserved stay live across it and the writes the service
makes (the selector store, dead flags, dead register stores before the call)
are removed by the ordinary dead-write passes.

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

1. Register the action with `AddAction(index, action, new ServiceContract(...))`.
2. Include in `Reads` every register the service or the pre-dispatch code
   reads, the selector register included.
3. Include in `Writes` the union of registers written on any path.
4. Put a flag in `FlagsWritten` only if the service can change its value on
   the interrupt frame (via `SetCarryFlag` and friends); a flag the service
   merely reads goes in `FlagsRead`.
5. If no single call shape makes the declared outputs deterministic, use
   `ServiceContract.Unknown` with a comment saying why, instead of declaring a
   contract that only holds on some paths.
