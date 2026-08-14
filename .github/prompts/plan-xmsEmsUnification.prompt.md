# Plan: Unified XMS/EMS Memory Backing Store + VCPI + LIM EMS 4.0 Parity

## TL;DR
XMS (`ExtendedMemoryManager.XmsRam`) and EMS (`EmmPage`'s private `Ram` per logical page) each
currently own a private, isolated `Ram` array, disconnected from the main `Memory`'s flat
`_memoryDevices[]` address space. In real mode this is invisible (both are only ever reached via
their INT 2Fh/INT 67h APIs), but it breaks protected mode: a flat/big descriptor reading linear
address X must see the SAME bytes XMS/EMS manipulate, and VCPI's entire purpose is telling a
protected-mode program the REAL physical address of an EMS page so it can build page tables - which
requires EMS pages to actually live at stable, real addresses in the same memory space paging
operates on. Plan: (1) replace both private arrays with a single, size-configurable "unified
extended memory" region registered into the main `Memory` via the existing `RegisterMapping`
mechanism, so real-mode INT 2Fh/67h access and protected-mode flat access are provably the same
bytes; (2) once addresses are stable, implement VCPI (matching dosbox-staging's covered
subfunctions) and close remaining LIM EMS 4.0 gaps against dosbox-staging's function coverage
(GEMMIS/INT 67h AH=0x5D confirmed NOT implemented by dosbox-staging either - dropped, no reference
exists). Every phase is TDD: the failing ASM fixture(s) come first, then the implementation.

## Steps (5 phases, strictly sequential - each depends on the prior)

### Phase 1 - Unified extended-memory backing store (foundation) - **STATUS: DONE**
1. Design and document the concrete linear-address memory map: conventional (0x00000-0xFFFFF,
   unchanged) + HMA (0x100000-0x10FFEF, unchanged, already the first slice of what
   `ExtendedMemoryManager.XmsMemorySize` computes) + the remainder of one single, unified `Ram`
   array spanning the ENTIRE address space from physical 0. Corrected from the original draft
   below: there is no separate "pool starting at 0x110000" with its own size - the new
   `Configuration.RamSizeKb`/`RamSizeDefaultKb`(16*1024)/`RamSizeMaxKb`(64*1024) options (renamed
   from the originally-planned `ExtendedMemorySizeKb`) size the TOTAL RAM from address 0 (conventional
   + HMA + unified extended pool together), matching real hardware's flat physical address space and
   the user's explicit "entire RAM from 0 by default should be 16 MB" direction. XMS EMBs and EMS
   logical pages both still need to draw their storage from sub-ranges of this ONE array instead of
   two separate private arrays - exact sub-range split (e.g. EMS gets a reserved slice sized to its
   max configured pages, XMS gets the rest) is still open, to be finalized in Phases 2-3.
2. DONE: `Spice86DependencyInjection.cs` now constructs `new Ram((uint)configuration.RamSizeKb * 1024)`
   (previously `new(A20Gate.EndOfHighMemoryArea)`) - the whole flat array exists up front via the
   constructor, no separate `RegisterMapping` call was needed since `Memory`'s default mapping already
   covers the full `Ram` array by construction.
3. DONE: TDD fixture `tests/Spice86.Tests/Resources/cpuTests/protectedmode_unified_pool.asm`/`.bin` +
   `MachineTest.TestUnifiedExtendedMemoryPoolIsAddressable` - builds a flat 32-bit protected-mode GDT,
   writes a marker byte to physical 0x500000 (5MB) via `mov byte [dword 0x500000], 0x42`, returns to
   real mode, asserts the byte stuck. This exposed a genuine, previously-undiscovered `A20Gate` bug
   (see Decisions) that had to be fixed before this fixture could pass - `A20Gate` was masking a whole
   address RANGE (~1-2MB ceiling) instead of gating only bit 20 as real hardware does, which blocked
   ANY flat address beyond ~2MB regardless of A20 state. Fixture confirmed PASSING.
4. DONE: full regression suite green throughout (2405/2405 after the master rebase - see below),
   including all pre-existing `XmsUnitTests`/`Xms32BitUnitTests`/`EmsUnitTests`/`EmsTests`/`XmsTests`
   fixtures (still using the OLD private-array behavior at this point - Phase 1 only grew the shared
   backing array and fixed A20Gate, it doesn't touch XMS/EMS internals yet - that's Phases 2-3).
5. DONE (out-of-band): the whole `feature/protected_mode` branch (10+ commits, later squashed to one)
   was rebased onto latest `master` (which had independently migrated logging from Serilog's
   `ILoggerService` to `Microsoft.Extensions.Logging.ILogger` - see repo memory for conflict
   resolution details) and pushed. All further phases build on top of that rebased state.

### Phase 2 - XMS onto the unified store (depends on Phase 1)
1. TDD first: new ASM fixture, real mode, `enableSpeculativeCfgExploration: false`-style protected
   mode test mirroring `TestProtectedModeEntry`'s pattern: from real mode, call INT 2Fh AH=43h/09h to
   allocate an XMS block (or use the HMA claim, function 01h), note the handle/offset, enter
   protected mode via a flat descriptor covering the pool, write a marker at the block's real linear
   address, return to real mode, and verify the SAME marker is visible through XMS's own Move
   Extended Memory Block function (0Bh) reading that handle back into conventional memory. Must FAIL
   before this phase's fix (today's `XmsRam` is a disconnected array).
2. Remove `ExtendedMemoryManager.XmsRam`; replace every `XmsRam.Read/Write`-style access with reads/
   writes through the shared `IMemory` at the block's real linear address (`XmsBaseAddress + offset`,
   already the correct formula - only the backing storage changes, not the addressing math).
3. Update `XmsBlock`/allocation bookkeeping only if needed to reflect the new fixed base (should be
   unaffected - `XmsBlock.Offset` is already relative to `XmsBaseAddress`).
4. Regression: all existing XMS unit tests + the new ASM fixture green; full suite green.

### Phase 3 - EMS onto the unified store with stable physical addresses (depends on Phase 2)
1. TDD first: new ASM fixture mirroring Phase 2's pattern: allocate+map an EMS logical page via INT
   67h AH=43h/44h from real mode, write a marker through the page-frame window (segment 0xE000),
   enter protected mode via a flat descriptor, read the SAME physical address the page's fixed
   linear address resolves to, and verify it matches - proving the page-frame window is an ALIAS
   into the shared pool, not a copy. Must FAIL before this phase (today's `EmmPage` is a private
   `Ram`, unmapped pages have no address at all, and `EmmRegister`'s page-frame slot is a distinct
   copy, not an alias).
2. Give every EMS logical page (allocated across ALL handles, not just the 4 mapped into the page
   frame at any moment) a fixed linear address within the pool's EMS sub-range as soon as it's
   allocated (INT 67h AH=43h), independent of whether it's currently mapped into the page frame.
3. Rework the page-frame-window mapping operation (AH=44h and friends) from "copy into a private
   `EmmRegister`" to "point the page-frame window's `_memoryDevices[]` slots at the SAME backing
   bytes as the logical page's fixed address" (i.e. `RegisterMapping` swap/alias on map/unmap,
   or an indirection device that forwards to the current mapping) - eliminates the separate
   `EmmPage`/`EmmRegister` private-`Ram` design entirely.
4. Regression: all existing EMS unit tests/ASM fixtures + the new one green; full suite green.

### Phase 4 - VCPI (depends on Phase 3)
1. TDD first: new ASM fixture - detect VCPI (INT 67h AH=DEh AL=00h), get the protected-mode interface
   entry point (AL=01h), allocate a physical page via VCPI (AL=04h), switch to protected mode via
   VCPI (AL=0Ch, building a minimal caller-owned GDT/IDT/page-directory per the interface contract),
   verify a flat read of the VCPI-allocated page's reported physical address matches what was written
   through it before the switch, then call back into the protected-mode interface to switch back to
   V86/real mode (function 0Ch's protected-mode-side counterpart) and halt cleanly. Must FAIL (no
   VCPI support exists at all today) before this phase.
2. Implement INT 67h AH=0xDE subfunctions matching dosbox-staging's coverage (confirmed via
   `dosbox-staging/src/ints/ems.cpp` lines ~941-1174, used as the concrete behavioral reference, not
   copied): 0x00 install check, 0x01 get protected-mode interface (install a callback + set up a
   private GDT/IDT/LDT/TSS area backed by EMS memory, mirroring `SetupVCPI()`), 0x02 max physical
   address, 0x03 get free page count, 0x04 allocate one page, 0x05 free page, 0x06 get physical
   address of a page currently mapped in the first MB, 0x0A/0x0B get/set PIC vector remapping,
   0x0C switch V86->protected mode (load caller's GDT/IDT/LDT/TSS/CR3, then far-jump).
3. Implement the protected-mode-side callback handler (dosbox's `VCPI_PM_Handler` equivalent) for
   the subset callable while already in protected mode - primarily 0x0C's counterpart (switch back
   to V86) and 0x03/0x04/0x05 (free-page-count/alloc/free) reachable from protected mode.
4. Verify our existing V86-mode support (already implemented per the protected-mode plan's Phase 6/7
   hardware-task-switch-based V86 entry) is reachable via VCPI's DIRECT (non-task-switch) transition
   path too - VCPI's switch is a bare LGDT/LIDT/LLDT/LTR/MOV CR3,CR0/JMP FAR sequence, not a task
   switch; confirm no code assumes V86 entry only ever happens via a task gate.
5. Regression: full suite green including the new VCPI ASM fixture.

### Phase 5 - LIM EMS 4.0 completeness audit vs dosbox-staging (depends on Phase 4; GEMMIS dropped)
1. Audit `ExpandedMemoryManager.FillDispatchTable()`'s registered subfunctions against
   dosbox-staging's full list (enumerated this session from `ems.cpp`'s `case` labels): confirmed
   already covered - 0x40-0x4E core, 0x50/0x51/0x53/0x58/0x59 (partial, per this class's own XML
   doc). Confirmed GAPS to close: 0x4E's full save/restore/get-array-size combo (if not already
   complete), 0x4F (Save/Restore PARTIAL Page Map - distinct from the full-map 0x47/0x48), 0x52
   (Set/Get Handle Attributes - volatile/non-volatile), 0x54 (Handle Functions), 0x57 (Memory Region
   Move/Exchange), 0x5A (Allocate Standard/Raw Pages). Re-verify this exact list against dosbox
   source line-by-line at implementation time - the enumeration above is a session-time list, not a
   final spec.
2. **GEMMIS (INT 67h AH=0x5D) is explicitly OUT OF SCOPE**: confirmed via direct grep of
   `dosbox-staging/src/ints/ems.cpp` that dosbox-staging itself has NO 0x5D handler at all - there is
   no reference implementation anywhere available, and per user direction the target bar is "full
   parity with dosbox-staging's EMS support," which does not include GEMMIS.
3. TDD first, per gap: one small ASM `.com` fixture per missing subfunction (mirroring the existing
   `tests/Spice86.Tests/Resources/EmsTests/*.asm` one-behavior-per-file convention), written to fail
   first, then implement the subfunction.
4. Regression: full suite green.

## Relevant files
- `src/Spice86.Core/Emulator/InterruptHandlers/Dos/Xms/ExtendedMemoryManager.cs` - remove `XmsRam`,
  read/write through shared `IMemory` instead (Phase 2).
- `src/Spice86.Core/Emulator/InterruptHandlers/Dos/Ems/ExpandedMemoryManager.cs`,
  `EmmPage.cs`, `EmmHandle.cs` - remove private per-page `Ram`, fixed linear addressing, page-frame
  aliasing instead of copying (Phase 3), then LIM 4.0 gap closures (Phase 5).
- `src/Spice86.Core/Emulator/Memory/Memory.cs` (`RegisterMapping`, `_memoryDevices[]`),
  `src/Spice86.Core/Emulator/Memory/Ram.cs`, `A20Gate.cs` - backing-store sizing (Phase 1, DONE);
  `A20Gate.cs` also got a real bug fix (gates only bit 20 now, not a whole address range - see
  Decisions) discovered by Phase 1's own TDD fixture.
- `src/Spice86/Spice86DependencyInjection.cs` - constructs `new Ram((uint)configuration.RamSizeKb *
  1024)` (Phase 1, DONE); `src/Spice86.Core/CLI/Configuration.cs` has the actual
  `RamSizeKb`/`RamSizeDefaultKb`(16*1024)/`RamSizeMaxKb`(64*1024) options (renamed from the
  originally-planned `ExtendedMemorySizeKb` - represents TOTAL ram from address 0, not an add-on
  pool). `tests/Spice86.Tests/Spice86Creator.cs`/`McpIntegrationContext.cs` both got a passthrough
  `ramSizeKb` test-only constructor parameter for fixtures needing a specific total size.
- New file(s) for VCPI (Phase 4), e.g. under `src/Spice86.Core/Emulator/InterruptHandlers/Dos/Ems/`
  or a new `Vcpi` subfolder - dispatch table addition to `ExpandedMemoryManager` for AH=0xDE, plus
  whatever new GDT/IDT/TSS-construction helper is needed (reuse existing `DescriptorTableReader`/
  `SegmentAndControlRegisterOperations`/`ProtectedModeCallGateDispatcher` machinery from Phase 9
  rather than reimplementing descriptor construction).
- `tests/Spice86.Tests/Resources/XmsTests/*.asm`, `tests/Spice86.Tests/Resources/EmsTests/*.asm` -
  existing one-behavior-per-file real-mode convention to extend for every new subfunction (Phase 5)
  and as the template for the new protected-mode fixtures (Phases 2-4).
- `tests/Spice86.Tests/MachineTest.cs` - protected-mode fixture pattern to mirror
  (`Spice86Creator(cpuModel: CpuModel.INTEL_80386, enableSpeculativeCfgExploration: false,
  maxCycles: 1000)`, direct `machine.Memory.ReadRam(...)` assertions) for the new XMS/EMS/VCPI
  protected-mode ASM fixtures.
- `tests/Spice86.Tests/Dos/Xms/XmsUnitTests.cs`, `Xms32BitUnitTests.cs`,
  `tests/Spice86.Tests/Dos/Ems/EmsUnitTests.cs` - existing unit-test baselines that must stay green
  throughout.
- Reference only (do not modify): `dosbox-staging/src/ints/ems.cpp` (VCPI at lines ~941-1174,
  `SetupVCPI()` ~1318-1380; full subfunction list enumerated via its `case 0x..` labels), used as
  the completeness/behavioral bar for both LIM EMS 4.0 and VCPI.

## Verification
1. Every phase: new ASM fixture(s) written FIRST and confirmed failing, then made to pass (strict
   TDD, per explicit user direction) - run via
   `dotnet test tests/Spice86.Tests --filter 'FullyQualifiedName!~SingleStepTest'`.
2. Regression: full suite must stay green after every phase, including every pre-existing
   XMS/EMS unit test and `.asm`/`.com` fixture - none of them should need behavior changes, only
   (potentially) their internal setup if backing-store plumbing changed under them.
3. Final acceptance: a protected-mode DOS-extender-style fixture exercising the full chain in one
   run - detect VCPI, allocate EMS pages via VCPI, switch to protected mode, read/write through flat
   descriptors, switch back to V86/real mode, confirm data survived the round trip.

## Decisions
- Backing store: single unified `Ram` array covering the ENTIRE address space from physical 0
  (conventional + HMA + extended), default 16MB TOTAL, extensible up to 64MB via
  `Configuration.RamSizeKb` (mirrors dosbox-staging's own configurable memory size) - per user's
  explicit "entire RAM from 0 by default should be 16 MB" direction. This corrects the original plan
  draft's wording of a separate "unified extended pool starting at 0x110000" with its own independent
  size - in the actual implementation there is only ONE array, sized in total, not an extra pool
  layered on top of a fixed conventional+HMA base.
- `A20Gate` real-hardware-accuracy bug fix (Phase 1 side effect, not originally planned but required
  for Phase 1's own TDD fixture to pass): it was masking a whole address RANGE
  (`DisabledAddressMask=0xFFFFF`/`EnabledAddressMask=0x1FFFFF`, imposing an artificial ~1-2MB ceiling)
  instead of gating only bit 20 (`0x100000`) as real 80286+ hardware does. Fixed to
  `DisabledAddressMask = ~0x100000u` / `EnabledAddressMask = 0xFFFFFFFF`.
- VCPI: target feature parity with dosbox-staging's covered subfunction set (`ems.cpp`'s 0xDE
  handler), not the full VCPI 1.0 spec beyond that - per user direction.
- GEMMIS: OUT OF SCOPE, dropped. Confirmed dosbox-staging itself has no INT 67h AH=0x5D handler, so
  there is no available reference implementation; the user's actual bar ("everything dosbox-staging
  supports") does not include it.
- Every phase is TDD, ASM-first: no implementation work starts before its corresponding failing ASM
  fixture(s) exist - explicit, non-negotiable per user direction for this plan.
- Phases are strictly sequential (1->2->3->4->5), each depending on the previous, per user
  confirmation.
- HMA is NOT a separate region to invent - it's already the first ~64KB slice of what
  `ExtendedMemoryManager.XmsMemorySize`/`XmsBaseAddress` represent; Phase 1 keeps this relationship,
  it just makes the WHOLE thing (HMA + XMS EMBs + EMS pages) one real, addressable region instead of
  a disconnected private array.

## Further Considerations
1. Exact EMS-vs-XMS sub-range split within the unified pool (Phase 1 step 1) is deliberately left
   open pending implementation-time investigation of current allocation-size assumptions in both
   managers (e.g. whether EMS's page count is hardcoded anywhere that assumes a specific base
   address) - flagged, not blocking, since it's a mechanical detail once the pool itself exists.
   Recommendation: fixed EMS sub-range sized to `EmmMaxHandles`-worth of max pages up front (simplest,
   avoids dynamic-growth complexity), XMS gets the remainder.
2. Phase 5's subfunction gap list was enumerated this session from a live grep of dosbox-staging's
   source `case` labels, not a line-by-line spec cross-check - re-verify at Phase 5's start rather
   than trusting this list blindly, since some functions listed as "gaps" might already be partially
   implemented under a different subfunction dispatch than expected (e.g. 0x53 vs 0x54 handle-name
   functions could overlap).
