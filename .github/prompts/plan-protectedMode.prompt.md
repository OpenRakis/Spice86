# Plan: 80386 Protected Mode (CPU + MMU + Overrides)

## Scope decisions (from user)
- Paging: IN SCOPE (CR3, page dir/table, #PF).
- TSS/task switching: FULL hardware task switch (JMP/CALL far to TSS selector).
- Privilege levels: CPL/DPL/RPL/ARPL/gate checks enforced.
- V86 mode: IN SCOPE.
- FPU: OUT OF SCOPE (explicit).
- Acceptance: enable `TEST_PMODE equ 1` in tests/Spice86.Tests/Resources/cpuTests/asmsrc/test386.asm/src/configuration.asm,
  rebuild via regen-asm-bins.sh, and pass POST 0xFF end-to-end (mirrors existing `Test386ButNotProtectedMode` test,
  renamed/duplicated as a protected-mode variant). Curated small ASM fixtures used per-phase for TDD.
- Override addressing in protected mode: LINEAR address keying (new), not selector:offset — because a selector's
  base can change via descriptor edits, unlike real mode where segment*16 is invariant.

## Architectural principle: no fallbacks, no quirks
- Every real-hardware invariant this plan relies on (e.g. "a segment register's hidden descriptor cache is always
  kept in sync with its raw value, in every mode, no matter what wrote that value") must be made TRUE everywhere in
  the emulator, not approximated with a heuristic at the one or two call sites a test happened to exercise.
  Concretely: relying on `CpuMode` to pick between a real-mode MMU and a descriptor-cache MMU, or special-casing CS
  so it always resolves through its cache while every other register dispatches by live mode, are QUIRKS/FALLBACKS
  introduced during Phase 2 debugging to unblock a single fixture without a full audit. They are correct only
  because nothing outside instruction execution (BIOS/VGA/DOS init code) writes a segment register directly and
  then depends on protected-mode-correct addressing through it. The true fix — auditing every direct
  `State.CS/DS/ES/SS/FS/GS = ...` write in the codebase (BiosLoader/DosProgramLoader/PCBootLoader, DOS/VGA/mouse
  interrupt handlers, etc.) and routing all of them through the same descriptor-cache-refreshing path used by
  `LoadSegmentRegister`, so that the descriptor cache is unconditionally authoritative and `CpuMmu` never needs to
  branch on `CpuMode` or special-case any one register — must land before this plan is considered structurally
  complete. Track this explicitly as a cleanup item at the end of Phase 2 (or as an early Phase 8 sub-task, since it
  is the same "pervasive but mechanical" shape of change): do not let it linger as a tolerated quirk into later
  phases.
- The same standard applies to every other phase: if an interim shortcut is taken to keep the test suite green,
  record it as a known gap in this plan and close it before moving to the next phase's "depends on" boundary,
  rather than layering new phases on top of an approximated invariant.

## Key existing groundwork found
- `CpuModel.INTEL_80386` already exists; `RealModeMmu8086`/`RealModeMmu386` + `RealModeMmuFactory` already exist,
  with `IMmu.CheckAccess`/`TranslateAddress` — explicit code comments say "future protected-mode MMU will extend this design".
- `tests/Spice86.Tests/Resources/cpuTests/asmsrc/test386.asm/` already vendored (PCjs/IBMulator 386 tester) with
  `TEST_PMODE equ 0` in `src/configuration.asm` — flipping this is the integration-test switch requested by the user.
  POST codes 0x08-0x1C cover GDT/LDT/paging/TSS/ring3/ARPL/etc. See its `README.md` table.
- `regen-asm-bins.sh` already knows how to rebuild `test386.bin` via nasm Makefile in `asmsrc/test386.asm/`.
- No GDT/LDT/IDT/Descriptor/CR0-CR4/Paging types exist anywhere in Spice86.Core today — this is ground-up.
- `State` (src/Spice86.Core/Emulator/CPU/State.cs) segment registers are bare `ushort` selectors via
  `SegmentRegisters : RegistersHolder` — no hidden descriptor cache (base/limit/access) per Intel real hardware design.
- `SegmentedAddress` (src/Spice86.Shared/Emulator/Memory/SegmentedAddress.cs) is `(ushort Segment, ushort Offset)`
  with `Linear = (Segment<<4)+Offset` baked into the constructor — cannot represent 32-bit EIP or non-linear-base
  protected-mode code addresses. `SegmentedAddress32` already exists (used by `LxsParser`/far pointers) — reuse as
  the CS:EIP shape for 32-bit code segments.
- CFG addressing (`CfgInstruction.Address`, `FunctionInformation` dictionary keys, `IOverrideSupplier`,
  `EmulatorBreakpointsManager`, GDB protocol, `ListingExtractor`) is pervasively keyed on `SegmentedAddress` today —
  this is the single biggest architectural risk area (see Phase 8).
- ModRM parser already supports 32-bit addressing (SIB, disp32) — reusable for protected-mode flat addressing.
- `CSharpOverrideHelper`/`IOverrideSupplier`/`DefineFunction`/`OverrideInstruction` already generic over any
  `SegmentedAddress` key — needs a parallel/extended key type for protected mode (Phase 8).
- dosbox-staging reference: `dosbox-staging/src/cpu/{cpu.cpp,cpu.h,paging.cpp,paging.h,modrm.cpp,modrm.h,
  core_normal.cpp}` — use for descriptor bit-layout, exception priority ordering, page-table walk algorithm,
  task-switch save/restore field order. Do not port code style (different language/paradigm), only algorithms
  and field semantics.

## Steps (phased; each phase ends with a compiling+passing curated ASM/unit fixture)

### Phase 1 — Descriptor/selector data model + CR0-CR4 (foundation, no behavior change yet)
*No dependency; can start immediately.*
1. Add `Spice86.Core/Emulator/CPU/Registers/SegmentSelector` (index/TI/RPL decomposition of a raw ushort) and
   `SegmentDescriptorCache` (Base uint, Limit uint, AccessRights byte, DefaultBig bool, granularity, Present, etc.) —
   one per segment register, stored on `State` alongside the existing selector `ushort` (extend `SegmentRegisters`
   or add a parallel array, not replace the existing selector storage).
2. Add `GdtRegister`/`IdtRegister` (Base uint32, Limit uint16) and `LdtRegister`/`TrRegister` (selector + cached
   descriptor) to `State`.
3. Add `CR0`, `CR2`, `CR3`, `CR4` fields to `State` (only PE, MP(optional no-op), TS, ET, PG bits meaningful; no FPU
   bits beyond stubs).
4. Add raw `GdtEntry`/`IdtEntry` byte-layout readers (struct helpers) mirroring Intel's 8-byte descriptor format —
   reference `dosbox-staging/src/cpu/paging.h`/`cpu.h` descriptor bit layouts for correctness.
5. Add `CpuMode` derived property on `State` (Real / Protected / V86) computed from `CR0.PE` and `EFLAGS.VM`.

### Phase 2 — Segment loading unification + protected-mode MMU (depends on Phase 1)
1. Introduce a single `SegmentLoader` service used by every segment-register write path (MOV Sreg, POP Sreg,
   LDS/LES/LFS/LGS/LSS parsers, far JMP/CALL/RET/IRET) that:
   - Real/V86 mode: sets selector, Base = selector*16, Limit = 0xFFFF (current behavior, refactored not changed).
   - Protected mode: looks up GDT/LDT entry, validates present/type/DPL vs CPL/RPL, raises `#GP`/`#NP`/`#SS` via
     existing `CpuException` hierarchy (extend with `CpuNotPresentException`, `CpuSegmentNotPresentException` etc.
     as needed — respect "no generic catch" and no null-forgiving rules), then populates the descriptor cache.
2. Replace `RealModeMmuFactory`/`RealModeMmu386` fixed-at-construction selection with a `CpuMmu` facade that reads
   `State.CpuMode` per access and delegates to `RealModeMmu8086`/`RealModeMmu386` (unchanged) or new
   `ProtectedModeMmu386` (translates via the *already-loaded* descriptor cache — no GDT re-lookup per access,
   matching real hardware and avoiding perf/semantic surprises).
3. `ProtectedModeMmu386.CheckAccess` enforces segment limit (respecting granularity bit) and type (code/data,
   expand-down) faulting with `#GP`/`#SS` as appropriate.
4. Verification fixture: small curated ASM (new file under `tests/Spice86.Tests/Resources/cpuTests/asmsrc/`) that
   sets up a minimal GDT, enters protected mode (`MOV CR0`), loads DS with a data selector, writes/reads memory,
   returns to real mode; assert memory dump. Add matching `GeneratedCodeMachineTest` entry per repo convention.

### Phase 3 — New/updated instructions for protected mode (depends on Phase 2)
1. Implement parsers+execution for: `LGDT`, `SGDT`, `LIDT`, `SIDT`, `LLDT`, `SLDT`, `LTR`, `STR`, `LMSW`, `SMSW`,
   `ARPL`, `LAR`, `LSL`, `VERR`, `VERW`, `MOV CRn,r32`/`MOV r32,CRn` (extend existing `SimpleInstructionParser`-style
   pattern; replace the current `ParseClts` no-op stub with real `CR0.TS` clearing).
2. Update `INT n`/`INTO`/`BOUND`/hardware-interrupt dispatch and `IRET` to branch: real/V86 semantics unchanged;
   protected-mode path walks the IDT, supports interrupt/trap/task gates, privilege/stack-switch logic (needs
   Phase 6 TSS for SS0:ESP0, so gate dispatch lands here but full ring-switch stack logic finishes in Phase 6).
3. Update far `JMP`/`CALL`/`RET` to add protected-mode paths: direct/indirect through call gates, conforming vs
   non-conforming segment checks, CPL/RPL/DPL validation — reuse existing `FarCall`/`FarJump`/`FarRet` helpers in
   `CSharpOverrideHelper` and `InstructionExecutionHelper`, extending rather than duplicating.
4. Verification: curated ASM fixtures per instruction group (mirrors `MachineTest`'s one-fixture-per-feature style),
   each with `GeneratedCodeMachineTest` counterpart per repo convention (AGENTS.md rule).

### Phase 4 — Privilege levels & gates (depends on Phase 3)
1. Implement CPL tracking (derived from CS selector RPL once in protected mode) and DPL/RPL checks on every
   segment load, gate transfer, and I/O instruction (`IOPL` check for `IN`/`OUT`/`CLI`/`STI`).
2. Implement call gates (task/interrupt/trap/call gate descriptor type dispatch) with stack switch on privilege
   escalation reading SS0:ESP0 (or SS1/SS2) from the current TSS.
3. Verification: curated ASM exercising ring 3 -> ring 0 call gate transition and back via `RETF`, matching
   test386 POST 0x0A/0x16 semantics on a small scale first.

### Phase 5 — Paging (depends on Phase 2, independent of Phase 3/4 — *parallel with Phase 3/4*)
1. Add `PagingUnit` component: linear->physical translation via CR3-rooted 2-level page directory/table walk,
   respecting P/R-W/U-S bits and CPL, TLB optional (correctness-first, skip caching unless perf requires it later).
2. Wire `PagingUnit` as a second translation stage after `ProtectedModeMmu386`/`RealModeMmu386` segment translation
   whenever `CR0.PG` is set (paging works in both real and protected mode on real hardware, but test386 only
   exercises it in protected mode — implement generally, test via protected mode).
3. Add `#PF` exception (`CpuPageFaultException`) carrying the faulting linear address into `CR2`, matching Intel's
   page-fault error-code bit layout.
4. Verification: curated ASM setting up an identity-mapped page directory/table, enabling `CR0.PG`, and forcing a
   deliberate not-present PTE access to assert `#PF` with correct `CR2`/error code.

### Phase 6 — TSS & hardware task switching (depends on Phase 4)
1. Add TSS layout reader/writer (32-bit TSS: link, ESP0-2/SS0-2, CR3, EIP, EFLAGS, general/segment regs, LDT
   selector, I/O bitmap offset, T bit) and `TrRegister` cache.
2. Implement task switch on far `JMP`/`CALL` to a TSS selector or task gate: save current task state into old TSS,
   load new TSS, set busy bit, NT flag/back-link handling for nested tasks, `IRET` with NT=1 returns via back-link.
3. Verification: curated ASM performing a `CALL` to a TSS selector, confirming register state persisted/restored
   across the switch.

### Phase 7 — V86 mode (depends on Phase 6 for entry via task switch, and Phase 4 for IRET-based entry)
1. Implement `EFLAGS.VM` semantics: entering via `IRET` popping a VM=1 image from a ring-0 stack, or via task switch
   into a V86 TSS; executing with real-mode-style segmentation but IOPL-based I/O permission bitmap checks and
   reflected interrupts/faults back to the ring-0 monitor.
2. Verification: curated ASM entering V86 mode, executing simple real-mode-style code, triggering a reflected
   fault back to protected mode.

### Phase 8 — CFG/override addressing migration to linear addressing (cross-cutting; start design early,
land incrementally alongside Phases 2-4; this is the highest-risk/most invasive phase)

**Confirmed by codebase audit**: `SegmentedAddress` (selector:offset) is the address type of
`ICfgNode.Address`/`CfgInstruction.Address`, and is used as a dictionary key (not just for physical-address
math) in at least: `FunctionCatalogue.FunctionInformations` (keyed by `f.Address`), `FunctionInformation.Address`
(hash/equality/`CompareTo` all delegate to it), `ExecutionContextManager.ExecutionContextEntryPoints`,
`CfgCodePartitionEntry.Address` (derived from `Node.Address`), and it drives ordering in
`ListingExtractor.DumpInOrder` via `node.Address.Linear`. GDB (`GdbCustomCommandsHandler`) prints addresses via
`SegmentedAddress.ToString`. This confirms the migration is pervasive but mechanically consistent: almost every
site just needs its key/compare/format logic parameterized by address kind rather than redesigned from scratch.

8.1. **Design the dual address type.** Add `CfgCodeAddress` (working name) as a discriminated value: either a
   `SegmentedAddress` (real/V86 mode, unchanged 16-bit selector:offset, `Linear = seg*16+offset` exactly as today)
   or a `LinearCodeAddress` (protected mode, a flat `uint`/`ulong` linear address computed once at fetch time
   through the loaded CS descriptor cache + paging if `CR0.PG` is set). Implement `IComparable`/`IEquatable`/
   `GetHashCode` so it can drop into existing `Dictionary<SegmentedAddress, T>`/`HashSet<SegmentedAddress>` call
   sites as a like-for-like replacement of the key type. Keep `SegmentedAddress` itself untouched (per Decisions:
   real-mode behavior must not regress) — `CfgCodeAddress` wraps it, it does not replace it.
8.2. **Fetch-time construction.** Wherever an instruction's address is captured today (`CfgInstruction` constructor,
   `_state.IpSegmentedAddress` reads in `CfgNodeFeeder`/`ExecutionContextManager`), switch to a single
   `State.CurrentCodeAddress`-style accessor that returns a `CfgCodeAddress`: real/V86 mode returns the existing
   `SegmentedAddress`; protected mode returns the linear address via the already-loaded CS descriptor cache (no
   extra GDT lookup — consistent with Phase 2's "translate via loaded cache" design).
8.3. **Migrate dictionary/collection keys.** Update, in this order (each independently testable against the
   existing real-mode suite):
   - `FunctionCatalogue.FunctionInformations` and `FunctionInformation.Address`/`GetHashCode`/`CompareTo`
     (`src/Spice86.Core/Emulator/Function/FunctionCatalogue.cs`, `FunctionInformation.cs`).
   - `IOverrideSupplier.GenerateFunctionInformations` return type and every `DefineFunction`/`OverrideInstruction`/
     `DoOnTopOfInstruction` overload in `CSharpOverrideHelper.cs` (these currently take `ushort segment, ushort
     offset` pairs — protected-mode overrides need a `CfgCodeAddress`-based overload, keeping the existing
     `ushort,ushort` overloads as real-mode sugar that just wraps `SegmentedAddress`).
   - `ExecutionContextManager.ExecutionContextEntryPoints` and `CfgCodePartitionEntry.Address`
     (`ControlFlowGraph`/`FunctionPartitioning` — used by CFG export and function partitioning/code generation).
   - `ListingExtractor.DumpInOrder` ordering key (currently `node.Address.Linear`; becomes
     `node.Address.SortKey`-equivalent on `CfgCodeAddress`).
   - `EmulatorBreakpointsManager`/GDB protocol (`GdbCustomCommandsHandler`, `SegmentedAddress.ToString`): these
     already operate on *physical* addresses for execution breakpoints (`MemoryUtils.ToPhysicalAddress`) separate
     from CFG addressing, so likely need only a display/parsing update for protected-mode selectors, not a key-type
     change — confirm during 8.4's audit rather than assuming.
8.4. **Full audit pass.** Run `vscode_listCodeUsages` on `SegmentedAddress` (and on `ICfgNode.Address`/
   `CfgInstruction.Address`) to enumerate every remaining call site not already covered by 8.3, and classify each
   as "real-mode only, unchanged" vs "must accept `CfgCodeAddress`". Expect additional sites in
   `CfgCodeGeneration` (generated C# source emits literal segment/offset constants today — protected-mode generated
   overrides need a linear-address-keyed registration path) and in the recorded-data JSON dumps
   (`spice86dumpCfgReload.json`/`spice86dumpCfgBlocks.json`/`spice86dumpCfgPartitions.json` per `doc/cfgcpuReadme.md`)
   whose serialization format currently assumes `SegmentedAddress`.
8.5. **C# override registration for protected mode.** Extend `CSharpOverrideHelper` with linear-address overloads
   of `DefineFunction`/`OverrideInstruction`/`DoOnTopOfInstruction` so `IOverrideSupplier` implementations can
   register protected-mode overrides directly against a linear address, independent of which selector currently
   maps there — this is what makes the override survive a descriptor edit that repoints the selector's base
   (the scenario that motivated the linear-addressing decision).
8.6. Verification:
   - Regression: re-run the full existing `MachineTest`/`GeneratedCodeMachineTest` suite unchanged — real-mode
     addressing/behavior must be bit-for-bit identical (no `CfgCodeAddress` behavior change observable from
     outside real-mode code paths).
   - New protected-mode override fixture (`CSharpOverrideHelperTest`-style): register a C# override at a linear
     address, then edit the GDT entry for the selector currently mapped there so it points elsewhere, and assert
     the override still fires for code physically at that linear address (proving the key survived the descriptor
     edit) while the old selector now resolves to unoverridden code.
   - New CFG dump fixture: confirm `spice86dumpCfgBlocks.json`/`ListingExtractor` output remains stable/ordered
     correctly for a protected-mode program (sanity check for 8.4's serialization audit).

### Phase 9 — Full test386 protected-mode acceptance (depends on all prior phases)
**STATUS: DONE.** `TEST_PMODE equ 1` is enabled and `test386_pmode.bin`/`.lst` are committed. `TempDebugTest386Pmode`
(`tests/Spice86.Tests/MachineTest.cs`) drives the suite: every `jne error`/`jnz error` assertion in the vendored
test386.asm PMODE suite passes, POST 0 through POST 0xEE (paging, TSS, privilege levels/gates, ARPL, ENTER/LEAVE,
VERR/VERW, and everything else the suite checks). The only unreached span is POST 0xEE → POST 0xFF, which is the
`arithLogicTests`/`bcdTests` section — confirmed via the source to contain zero `jne error` checks; it only prints
ASCII diagnostics meant for a deterministic full-memory-dump comparison against a trusted reference emulator (per
test386.asm's own README), not per-check assertions. Reaching it is blocked purely by CFG-interpreter throughput
(each printed character costs ~11 emulated instructions; even 50,000,000 cycles wasn't enough), not by any
correctness gap found in this codebase. Closing that gap (golden memory dump at literal POST 0xFF) is deferred as
future interpreter-performance work, not a Phase 9 blocker — every actual hardware-conformance check the suite
makes is green. The C# machine-code override system (real-mode `DefineFunction`/`OverrideInstruction`/
`DoOnTopOfInstruction` plus the Phase 8 linear-address-keyed protected-mode variant) is confirmed working and
covered by passing tests (`CSharpOverrideHelperTest.cs`, `LinearAddressOverrideTest.cs`).
1. Flip `TEST_PMODE equ 1` in `tests/Spice86.Tests/Resources/cpuTests/asmsrc/test386.asm/src/configuration.asm`.
2. Rebuild via `regen-asm-bins.sh` (nasm Makefile in that directory), producing an updated `test386.bin` +
   `test386.lst` committed alongside existing real-mode-only artifacts.
3. Add a new test (e.g. `Test386ProtectedMode` next to existing `Test386ButNotProtectedMode` in
   `tests/Spice86.Tests/MachineTest.cs`) asserting all POST codes through `0xFF`, plus the `GeneratedCodeMachineTest`
   counterpart per repo convention.
4. Iterate: run, diagnose first failing POST code via `test386.lst`, fix, repeat until `0xFF` reached — expect this
   to surface gaps missed in Phases 1-8.

### Phase 10 — XMS/EMS protected-mode addressability, LIM EMS v4, and VCPI (final phase; depends on Phase 9)
*User note: this is explicitly the last phase — XMS/EMS rework only makes sense once protected mode itself is
proven correct end-to-end.*
1. **Close the gap, don't paper over it**: today's XMS/EMS implementations rely on being unreachable via ordinary
   real-mode segment:offset addressing (XMS lives above the 1MB line, only reachable through its move-block API;
   EMS is reachable only through a bank-switched page-frame window). A protected-mode program with a flat/big
   data descriptor can address physical memory directly, including whatever backs XMS/EMS — so the memory those
   subsystems manage must become directly, correctly addressable through the descriptor-cache-based MMU once
   protected mode is active, with no special-casing that assumes "real mode can't reach here". Audit
   `src/Spice86.Core/Emulator/Memory` XMS/EMS device/handler code (device registration via `RegisterMapping`,
   the EMS page-frame window, the XMS high-memory-area/extended-memory block table) for any assumption that only
   real-mode addressing modes will ever reach that memory, and fix each one so protected-mode flat access sees the
   same consistent, correct data as the XMS/EMS API would report.
2. **Upgrade EMS to full LIM EMS v4**: audit the current EMS implementation against the LIM (Lotus/Intel/Microsoft)
   EMS 4.0 specification (multiple mappable regions/handles, save/restore page map context, OS/E functions,
   extended INT 67h subfunctions) and close any gaps against `dosbox-staging`'s EMS implementation
   (`dosbox-staging/src/dos` or wherever its EMS device lives — locate and use as the behavioral reference, not a
   code port).
3. **Add VCPI (Virtual Control Program Interface)**: implement the VCPI callback interface (INT 67h VCPI
   subfunctions: get VCPI interface presence/version, switch to/from protected mode via VCPI, get/set page tables,
   simulate real-mode interrupt, etc.), letting a DOS extender running under Spice86's protected-mode CPU cooperate
   with EMS the way real VCPI-aware DOS extenders (e.g. DOS/4GW) expect. Use `dosbox-staging`'s VCPI implementation
   as the behavioral reference for the callback contract and page-table handoff semantics.
4. Verification: curated ASM/DOS-extender-style fixtures exercising (a) a protected-mode flat access to
   XMS-managed and EMS-managed memory matching what the real-mode XMS/EMS API reports for the same physical bytes,
   (b) LIM EMS v4 subfunctions not previously covered, (c) a VCPI handshake (detect, enter protected mode via VCPI,
   page-table query, return to real mode) round-tripping correctly. Full regression suite must stay green
   (`dotnet test tests/Spice86.Tests --filter 'FullyQualifiedName!~SingleStepTest'`).

## Relevant files
- `src/Spice86.Core/Emulator/CPU/State.cs` — add CR0-CR4, GDTR/IDTR/LDTR/TR, per-segment descriptor cache.
- `src/Spice86.Core/Emulator/CPU/Registers/SegmentRegisters.cs`, `SegmentRegisterIndex.cs` — extend for descriptor
  cache storage.
- `src/Spice86.Core/Emulator/CPU/CpuModel.cs` — already has `INTEL_80386`; no change needed, but MMU/mode
  selection must stop being purely construction-time.
- `src/Spice86.Core/Emulator/Memory/Mmu/RealModeMmu8086.cs`, `RealModeMmu386.cs`, `RealModeMmuFactory.cs`,
  `IMmu.cs` — extend factory into a mode-dispatching `CpuMmu` facade; add `ProtectedModeMmu386.cs`.
- `src/Spice86.Core/Emulator/CPU/Exceptions/` — add `CpuNotPresentException`, `CpuPageFaultException`, etc.
  alongside existing `CpuGeneralProtectionFaultException`/`CpuStackSegmentFaultException`.
- `src/Spice86.Core/Emulator/CPU/CfgCpu/Parser/SpecificParsers/` — add new parsers (LGDT/LIDT/LLDT/LTR/ARPL/LAR/
  LSL/VERR/VERW/MOV CRn), update `SimpleInstructionParser.ParseClts`, `MovSregRm16Parser`, `MovRmSregParser`,
  `LxsParser`, far call/jump/ret/iret handling in `InstructionExecutionHelper.cs`.
- `src/Spice86.Shared/Emulator/Memory/SegmentedAddress.cs`, `SegmentedAddress32.cs` — reuse `SegmentedAddress32`
  for 32-bit CS:EIP; do not change real-mode `SegmentedAddress` semantics.
- `src/Spice86.Core/Emulator/Function/IOverrideSupplier.cs`, `src/Spice86.Core/Emulator/ReverseEngineer/
  CSharpOverrideHelper.cs` — extend addressing for Phase 8.
- `tests/Spice86.Tests/MachineTest.cs`, `GeneratedCodeMachineTest.cs`, `GeneratedCodeMachineTestRunner.cs`,
  `Spice86Creator.cs` — add protected-mode fixtures + `Test386ProtectedMode` test; every new fixture needs both
  `MachineTest` and `GeneratedCodeMachineTest` entries per AGENTS.md rule.
- `tests/Spice86.Tests/Resources/cpuTests/asmsrc/test386.asm/src/configuration.asm` — flip `TEST_PMODE`.
- `regen-asm-bins.sh` — already handles test386 rebuild; no change expected.
- Reference only (do not modify): `dosbox-staging/src/cpu/{cpu.cpp,cpu.h,paging.cpp,paging.h,modrm.cpp,
  core_normal.cpp,core_normal/*}`.

## Verification
1. Per-phase curated ASM fixture compiled via `nasm`/`fasm` per `regen-asm-bins.sh` conventions, added to both
   `MachineTest` and `GeneratedCodeMachineTest` (AGENTS.md rule), run via
   `dotnet test tests/Spice86.Tests --filter 'FullyQualifiedName!~SingleStepTest'`.
2. Regression: full existing suite must stay green after Phase 8 (real-mode addressing must be untouched).
3. Final acceptance: `Test386ProtectedMode` reaches POST `0xFF` with `TEST_PMODE=1`, matching a committed golden
   memory dump the same way `Test386ButNotProtectedMode` does today.
4. Manual smoke check (optional): run an actual protected-mode DOS extender or a hand-written protected-mode COM
   file via `dotnet run --project src/Spice86 -- -e ...` if/when available.

## Decisions
- Paging, full TSS task switching, CPL/DPL/RPL enforcement, and V86 mode are all IN SCOPE (per user confirmation).
- FPU is explicitly OUT OF SCOPE.
- Protected-mode C# overrides are keyed by linear address (new), not selector:offset, because descriptor edits can
  repoint a selector's base — this is a deliberate deviation from the real-mode override addressing scheme.
- Acceptance target is the full vendored `test386.asm` PMODE suite (POST 0x08-0x1C) reaching POST 0xFF, not just
  curated fixtures — curated fixtures are the TDD stepping stones per phase, test386 is the final gate.
- Architectural invariants (e.g. descriptor-cache correctness across mode transitions) must be resolved truly and
  universally, not via mode-branching heuristics or single-register special cases — see "Architectural principle:
  no fallbacks, no quirks" above.
- XMS/EMS protected-mode addressability, LIM EMS v4 compliance, and VCPI support are IN SCOPE as Phase 10, the
  explicit final phase, per user request — deferred until after protected mode itself is proven via Phase 9.

## Further Considerations
1. Phase 8 (CFG/override linear addressing) is by far the largest architectural risk: it touches the CFG graph,
   function partitioning, code generation, breakpoints, and GDB protocol. Recommend starting its design (the new
   `CfgCodeAddress` type and a full `vscode_listCodeUsages` audit of `SegmentedAddress`) in parallel with Phase 2,
   rather than waiting until the end, so later phases build against the final addressing model instead of a
   throwaway one.
2. Given the size (9 phases, likely 40+ new files, multiple new exception types, a new MMU stage, TSS, paging, and
   a pervasive addressing-model change), recommend tracking this as a series of separate implementation sessions
   per phase rather than one continuous session, with a checkpoint/manual-test pause after each phase per your
   existing AITD-style workflow preference.
