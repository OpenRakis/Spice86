# Maupiti Batch Launch Investigation (2026-05-02)

## 1. Hard Facts

- Fact: Canonical startup path is batch-driven (`MAUPITI.BAT`) with two commands: `maupiti1` then `maup %1`.
- Code citation: `C:\jeux\maupiti\MAUPITI.BAT`.
- Symbol: n/a (runtime input file).
- Status: code-proven/runtime-proven.

- Fact: Spice86 batch engine starts from generated `AUTOEXEC.BAT`, calls `C:\MAUPITI.BAT`, launches `C:\maupiti1.EXE`, then resumes batch and launches `C:\maup.EXE`.
- Code citation: `logs/log-20260502.txt` around lines 2023-2051 and 2226-2234.
- Symbol: `DosBatchExecutionEngine.TryStart`, `DosBatchExecutionEngine.TryContinue`, `DosProcessManager.LoadBatchLaunchRequest`.
- Status: code-proven/runtime-proven.

- Fact: TSR path is reached and `INT 21h AH=31h` executes from `109E:0012` in live listing.
- Code citation: `runs/maupiti-preflight/D0288.../spice86dumpListing.asm` line 31.
- Symbol: `DosInt21Handler.TerminateAndStayResident`.
- Status: code-proven/runtime-proven.

- Fact: Runtime enters sustained interrupt loop with repeated `INT09` processing scan code `0x00` and `INT 15h AH=4Fh` intercept, while execution repeatedly returns to `19C9:0321`.
- Code citation: `logs/log-20260502.txt` lines 9184-9186 and repeated blocks 9193+ (`INT09 entry ST=0x1C`, `scan=0x00`, `INT 15h AH=4Fh`); `runs/.../spice86dumpListing.asm` lines 858 and 1031.
- Symbol: `BiosKeyboardInt9Handler.Run`, `SystemBiosInt15Handler.KeyboardIntercept`.
- Status: code-proven/runtime-proven.

- Fact: During the livelock sequence, keyboard controller status remains `0x1C` before and after reads; bit0 (output buffer full) is clear, indicating no new keyboard output-buffer byte is pending while `0x00` is still consumed.
- Code citation: `logs/log-20260502.txt` lines 9184-9185, 9193-9194, 9202-9203 (pattern repeated through the loop).
- Symbol: `Intel8042Controller.ReadByte`, `BiosKeyboardInt9Handler.Run`.
- Status: code-proven/runtime-proven.

- Fact: Final CPU snapshot at stop-after-cycles is `CS:IP=F000:0011`, confirming hot loop at BIOS keyboard interrupt handler entry.
- Code citation: `runs/.../spice86dumpCpuRegisters.json`.
- Symbol: `State.IP`, `State.CS`.
- Status: code-proven/runtime-proven.

- Fact: Existing root PSP fix is present: `COMMAND.COM PSP.CurrentSize` set to `CommandComSegment + PSP paragraphs` (small realistic value) to prevent oversized DX on TSR calls.
- Code citation: `src/Spice86.Core/Emulator/OperatingSystem/DosProcessManager.cs` (CreateRootCommandComPsp).
- Symbol: `DosProcessManager.CreateRootCommandComPsp`.
- Status: code-proven.

- Fact: Dosbox-staging IRQ1 callback scaffold is structurally aligned with Spice86 INT09 path: disable keyboard (`0xAD`), read `0x60`, re-enable (`0xAE`), invoke INT 15h AH=4Fh, then EOI.
- Code citation: `C:\Users\noalm\source\repos\dosbox-staging\src\cpu\callback.cpp` (`CB_IRQ1`) and `src/Spice86.Core/Emulator/InterruptHandlers/Input/Keyboard/BiosKeyboardInt9Handler.cs`.
- Symbol: `callback_setup_extra`, `BiosKeyboardInt9Handler.Run`.
- Status: code-proven.

- Fact: Dosbox-staging INT 15h AH=4Fh default sets CF=1 and Spice86 does the same; carry-flag semantics are not the current divergence.
- Code citation: `C:\Users\noalm\source\repos\dosbox-staging\src\ints\bios.cpp` (`case 0x4f`) and `src/Spice86.Core/Emulator/InterruptHandlers/Bios/SystemBiosInt15Handler.cs` (`KeyboardIntercept`).
- Symbol: `INT15_Handler`, `SystemBiosInt15Handler.KeyboardIntercept`.
- Status: code-proven.

- Fact: Dosbox-staging and Spice86 both return the previous 0x60 data byte when no new keyboard data is pending.
- Code citation: `C:\Users\noalm\source\repos\dosbox-staging\src\hardware\input\intel8042.cpp` (`read_data_port`) and `src/Spice86.Core/Emulator/Devices/Input/Keyboard/Intel8042Controller.cs` (`ReadByte`).
- Symbol: `read_data_port`, `Intel8042Controller.ReadByte`.
- Status: code-proven.

- Fact: Dosbox-staging IRQ1 processing includes an early layout/filter stage (`DOS_LayoutKey`) before switch-based scan handling; Spice86 has no equivalent pre-filter in INT09 processing.
- Code citation: `C:\Users\noalm\source\repos\dosbox-staging\src\ints\bios_keyboard.cpp` (`IRQ1_Handler`) and `src/Spice86.Core/Emulator/InterruptHandlers/Input/Keyboard/BiosKeyboardInt9Handler.cs`.
- Symbol: `IRQ1_Handler`, `UpdateKeyboardFlagsByInterpretingScanCode`.
- Status: code-proven.

## 2. Failure Surfaces

- Surface name: Post-TSR keyboard IRQ storm blocks full game handoff.
- Invariant: After TSR and next EXEC, keyboard IRQ should process real scan transitions, not unbounded `0x00` stream.
- Probable break mechanism: no-new-data reads from port `0x60` repeatedly return stale `0x00` while INT09 still performs full scan processing, creating perpetual IRQ1 servicing.
- Evidence sources: `BiosKeyboardInt9Handler.Run` logs + runtime tail traces.

- Surface name: Batch-to-child context transition after TSR return.
- Invariant: `TryContinue` should resume batch and launch child once, then hand control to program mainline.
- Probable break mechanism: child enters interrupt-driven livelock before useful progression, making batch appear complete but gameplay not reached.
- Evidence sources: batch logs (`TryContinue`, `LAUNCH external program`) + CPU snapshot.

- Surface name: DOS interrupt interplay around input path.
- Invariant: INT 09 + INT 15/4F should preserve normal keyboard flow without repeated null scans.
- Probable break mechanism: null-scan admission path (no-data read + no pre-filter) allows repeated `0x00` into BIOS processing loop.
- Evidence sources: `SystemBiosInt15Handler.KeyboardIntercept` and `BiosKeyboardInt9Handler.Run`.

- Surface name: Conventional memory/XMS/EMS indirect pressure not yet excluded.
- Invariant: next EXEC runtime should not degrade into unrelated IRQ loop due memory manager side-effects.
- Probable break mechanism: unknown indirect state coupling still possible but currently unproven.
- Evidence sources: no fresh runtime snapshots for XMS/EMS in this run.

## 3. Interrupt and Memory-Model Contracts

### DOS Interrupt Contract Table

- Interrupt: INT 21h AH=31h (TSR).
- Entry conditions: AL return code, DX paragraphs to keep.
- Expected side effects: resize/keep block, track resident block, terminate with TSR semantics.
- Expected return semantics: parent context restored, batch may continue.
- Code citation: `src/Spice86.Core/Emulator/InterruptHandlers/Dos/DosInt21Handler.cs` (`TerminateAndStayResident`).

- Interrupt: INT 21h AH=4Bh (EXEC).
- Entry conditions: program path + parameter block.
- Expected side effects: allocate/load child process, set parent linkage.
- Expected return semantics: success/failure encoded, caller context updated.
- Code citation: `src/Spice86.Core/Emulator/InterruptHandlers/Dos/DosInt21Handler.cs` and `DosProcessManager.LoadOrLoadAndExecute`.

- Interrupt: INT 09h (keyboard hardware IRQ handler path).
- Entry conditions: keyboard controller data available.
- Expected side effects: read scan, optional INT 15h intercept, update flags, EOI.
- Expected return semantics: finite per-event service, no livelock.
- Code citation: `src/Spice86.Core/Emulator/InterruptHandlers/Input/Keyboard/BiosKeyboardInt9Handler.cs`.

- Interrupt: INT 09h parity anchor in dosbox-staging.
- Entry conditions: callback scaffold reads 0x60 then delegates scan handling callback.
- Expected side effects: callback-level scan filtering/translation and BDA keyboard-state updates.
- Expected return semantics: finite per-event service with PIC EOI.
- Code citation: `C:\Users\noalm\source\repos\dosbox-staging\src\cpu\callback.cpp` and `C:\Users\noalm\source\repos\dosbox-staging\src\ints\bios_keyboard.cpp`.

- Interrupt: INT 15h AH=4Fh (keyboard intercept).
- Entry conditions: AL = scancode.
- Expected side effects: optional translate/filter, set CF for process/ignore decision.
- Expected return semantics: CF controls whether INT 09 keeps processing.
- Code citation: `src/Spice86.Core/Emulator/InterruptHandlers/Bios/SystemBiosInt15Handler.cs` (`KeyboardIntercept`).

### XMS and EMS Contract Table

- XMS contract:
  - Allocation and lock state should not affect IRQ dispatch semantics directly.
  - Risk: indirect memory pressure side effects if process lifecycle mutates low-memory assumptions.
  - Code citation: `src/Spice86.Core/Emulator/Mcp/EmulatorMcpTools.cs` (`read_xms_state`).

- EMS contract:
  - Page-frame mappings should be orthogonal to keyboard IRQ path.
  - Risk: indirect interaction if pointers or structures are misinterpreted after process transitions.
  - Code citation: `src/Spice86.Core/Emulator/Mcp/EmulatorMcpTools.cs` (`read_ems_state`).

### Batch Context Contract Table

- Entry: startup generated via `ConfigureHostStartupProgram` then `TryStart` calling `AUTOEXEC.BAT`.
- Nested call behavior: `CALL` pushes context, `TryContinue` resumes with child return code.
- Exit propagation: `TryPump` pops contexts until exhausted.
- Code citation: `src/Spice86.Core/Emulator/OperatingSystem/DosBatchExecutionEngine.cs`.

## 4. Observability Plan

- Checkpoint A (pre-TSR keep):
  - Capture `read_cpu_state`, `read_dos_state`, `read_dos_program_state`, key vectors (`22h`, `23h`, `24h`, keyboard path vectors), and `read_cfg_cpu_graph` (bounded).

- Checkpoint B (post-TSR, pre-maup launch):
  - Repeat A and diff: PSP parent linkage, current PSP, vectors, context depth, function hot spots.

- Divergence checkpoint (post-maup launch):
  - Trigger when first repeated `INT09 scan=0x00` sequence appears.
  - Capture `read_cpu_state`, `read_stack`, `read_cfg_cpu_graph`, `read_interrupt_vector`, plus MCP XMS/EMS snapshots.
  - Additionally capture 8042 status bit0 (output buffer full) immediately before and after 0x60 reads to prove whether zeros are synthetic or queued.
  - In this run, existing logs already satisfy this capture: status is repeatedly `0x1C` (bit0 clear) alongside `scan=0x00`.

- Required MCP captures:
  - `pause_emulator`, `read_cpu_state`, `read_dos_state`, `read_dos_program_state`, `read_interrupt_vector`, `read_cfg_cpu_graph`, `read_ems_state`, `read_xms_state`, `step_over` as needed.

- Fallback artifacts:
  - `spice86dumpCpuRegisters.json`, `spice86dumpExecutionFlow.json`, `spice86dumpListing.asm`, `spice86dumpMemoryDump.bin`, verbose logs.

## 5. Ranked Hypotheses

1. Claim: Null-scan admission path (no pending data + no pre-filter) causes synthetic `0x00` churn, trapping runtime in INT09/INT15 loop after game launch.

- Confidence: high.
- Evidence already present: repeated `INT09 read scan=0x00`, repeated `INT 15h AH=4Fh`, final `CS:IP=F000:0011`, repeated returns to `19C9:0321`.
- Missing evidence needed: short single-step sequence showing exactly where `scan=0x00` transitions from controller read to BIOS-keyboard-buffer admission (or equivalent processing branch).
- Falsification path: show status bit0 set with genuine queued frames at divergence, or prove zero scans are filtered before key-path side effects.

1. Claim: Batch and TSR lifecycle is now good enough to launch both `maupiti1` and `maup`, but runtime stalls before full game loop due input interrupt path, not memory exhaustion.

- Confidence: medium-high.
- Evidence already present: batch logs show successful launch chain to `maup.EXE`; no immediate EXEC allocation failure reported.
- Missing evidence needed: explicit proof that main game code reached expected first UI/game-state marker before loop.
- Falsification path: set execution breakpoint markers in game init routines and compare reached/not reached.

1. Claim: Remaining memory/XMS/EMS issue still contributes indirectly to input loop.

- Confidence: low.
- Evidence already present: none direct in this run.
- Missing evidence needed: MCP `read_ems_state`/`read_xms_state` and low-memory consistency snapshots at A/B/divergence.
- Falsification path: show stable XMS/EMS and conventional memory invariants while loop persists.

## 6. Handoff Contract

- What execution-phase agent can do next:
  1. Add targeted diagnostics in keyboard interrupt path and controller reads to distinguish real vs synthetic scans.
  2. If evidence confirms synthetic-zero admission, apply a minimal INT09 parity fix that blocks no-data null-scan processing.
  3. Add integration test scenario covering batch startup to full launch transition with keyboard IRQ sanity assertions.
  4. Validate with MCP checkpoint protocol and ensure full game launch criterion, not only TSR/EXEC success.

- What it must not assume:
  1. Do not assume memory lifecycle is solved just because root PSP fix exists.
  2. Do not assume isolated EXE success implies batch-path success.
  3. Do not assume raw disassembly beats live CFG evidence for causality.

- Blocking unknowns:
  1. Exact first divergence from normal game path to INT09 livelock.
  2. Whether keyboard intercept behavior should filter scan `0x00` in this context.
  3. Whether any hidden DOS interrupt contract mismatch precedes the IRQ storm.

---

## 7. Phase 2 Hard Facts: TSR Side-Effect Ledger (Code-Proven)

### INT 21h AH=31h Handler Contract (Code Path)

- Fact: `DosInt21Handler.TerminateAndStayResident` calls `TryModifyBlock(currentPspSegment, DX_paragraphs)` then `TrackResidentBlock` (pushes MCB segment onto `_pendingResidentBlocks` stack).
- Code citation: [DosInt21Handler.cs](src/Spice86.Core/Emulator/InterruptHandlers/Dos/DosInt21Handler.cs) line 1223.
- Status: code-proven.

- Fact: `DosProcessManager.TerminateProcess(returnCode, TSR)` does NOT close file handles and does NOT free program memory; it DOES free the environment block. It pops `_pendingResidentBlocks`, writes `residentBlock.PspSegment = currentPspSegment`, and sets `currentPsp.CurrentSize = residentNextSegment`.
- Code citation: [DosProcessManager.cs](src/Spice86.Core/Emulator/OperatingSystem/DosProcessManager.cs) lines 594-670.
- Status: code-proven.

- Fact: `TerminateProcess` restores INT 22h/23h/24h vectors from current PSP fields (not from IVT directly). These were saved when the TSR process was launched via `SaveInterruptVectors`. This means the TSR-installed INT 00h/23h/24h/3Fh vectors from `126E:0053-0071` are NOT restored here (they were installed in IVT, not in PSP fields).
- Code citation: [DosProcessManager.cs](src/Spice86.Core/Emulator/OperatingSystem/DosProcessManager.cs) lines 636-638 and `RestoreInterruptVector` at line 796.
- Status: code-proven.

- Fact: `TerminateProcess` for TSR with `parentIsRootCommandCom == true` restores SS:SP from `parentPsp.StackPointer`, patches the iregs frame (stack[0]=INT22 offset, stack[2]=INT22 segment), sets DS=ES=parentPspSegment, then calls `RestoreStandardHandlesAfterLaunch` and `TryResumeBatchExecutionFromRoot`.
- Code citation: [DosProcessManager.cs](src/Spice86.Core/Emulator/OperatingSystem/DosProcessManager.cs) lines 645-668.
- Status: code-proven.

- Fact: `TryResumeBatchExecutionFromRoot` calls `_batchExecutionEngine.TryContinue` in a loop; each iteration calls `LoadBatchLaunchRequest` → `LoadInitialProgram` → `LoadOrLoadAndExecuteInternal`. On success, `LoadInitialProgram` returns `DosExecResult` with `InitCS:InitIP:InitSS:InitSP` from the EXE header, and the caller sets the CPU registers. This is the point at which `maup.exe` gets its entry point.
- Code citation: [DosProcessManager.cs](src/Spice86.Core/Emulator/OperatingSystem/DosProcessManager.cs) lines 676-714.
- Status: code-proven.

### INT09 Handler State Machine Post-Fix (Code Path)

- Fact: With the output-buffer guard, `BiosKeyboardInt9Handler.Run` now reads `StatusRegister` BEFORE reading port 0x60. If `StatusBits.OutputBufferFull` (bit0) is clear, it re-enables keyboard port, sends PIC EOI, and returns immediately without touching port 0x60 or BIOS keyboard buffer.
- Code citation: [BiosKeyboardInt9Handler.cs](src/Spice86.Core/Emulator/InterruptHandlers/Input/Keyboard/BiosKeyboardInt9Handler.cs) lines 78-91.
- Status: code-proven.

- Fact: When real scan code is pending (bit0 set), INT09 reads port 0x60, calls INT 15h AH=4Fh (AL=scancode), and if CF=1 (process), routes through `UpdateKeyboardFlagsByInterpretingScanCode`. For scan code 0x2F ('V') with no modifiers, `KeyboardMap.GetKeyCodesFor(0x2F).Normal = 0x2F76` is enqueued: BIOS code scan=0x2F char='v'(0x76). For 0x2E ('C'), `Normal = 0x2E63`: scan=0x2E char='c'(0x63).
- Code citation: [BiosKeyboardInt9Handler.cs](src/Spice86.Core/Emulator/InterruptHandlers/Input/Keyboard/BiosKeyboardInt9Handler.cs) lines 220-222 (KeyboardMap entry [46]=C, [47]=V) and `normal_key` path lines 505-555.
- Status: code-proven.

- Fact: For a release scan code (bit7 set = `scanCode & 0x80 != 0`), the `normal_key` path goes to `irq1_end` without enqueuing anything. So only press events (bit7 clear) produce BIOS buffer entries for letter keys. The MCP injection must send `isPressed=true` AND then `isPressed=false` pairs for the full BIOS key sequence to be correct.
- Code citation: [BiosKeyboardInt9Handler.cs](src/Spice86.Core/Emulator/InterruptHandlers/Input/Keyboard/BiosKeyboardInt9Handler.cs) lines 509-511 (`(scanCode & KeyReleaseMask) != 0 → goto irq1_end`).
- Status: code-proven.

### MCP Keyboard Path Equivalence (Code Path)

- Fact: Both the old MCP path (`hub.PostToEmulatorThread(() => EnqueueKeyEvent(parsedKey, isPressed))`) and the new MCP path (`hub.PostKeyboardEvent(new KeyboardEventArgs(PhysicalKey.V, true))` → `PS2Keyboard.OnKeyEvent` → `EnqueueKeyEvent(V, true)`) call the same `PS2Keyboard.EnqueueKeyEvent` with the same `PcKeyboardKey` argument. The functional result (scan codes enqueued, IRQ generation) is identical. The new path improves code structure but does not change observable keyboard behavior.
- Code citation: [PS2Keyboard.cs](src/Spice86.Core/Emulator/Devices/Input/Keyboard/PS2Keyboard.cs) lines 483-486 (`OnKeyEvent`) and 529-553 (`EnqueueKeyEvent`).
- Status: code-proven.

## 8. Phase 3 Hard Facts: First Divergence Analysis

### Keyboard Buffer Admission Path (Scan → BIOS Buffer)

- Fact: `PS2Keyboard.EnqueueKeyEvent` → `EnqueueScanCodeFrame` → if `_controller.IsReadyForKbdFrame`, calls `_controller.AddKeyboardFrame(bytes)` which stores bytes in the 8042 output register and raises IRQ1. This means the scan code bytes for V (0x2F) or C (0x2E) arrive in the output buffer, set `StatusBits.OutputBufferFull`, and the INT09 output-buffer guard will correctly let them through.
- Code citation: [PS2Keyboard.cs](src/Spice86.Core/Emulator/Devices/Input/Keyboard/PS2Keyboard.cs) lines 537-553 and [Intel8042Controller.cs].
- Status: code-proven (call chain).

### Remaining Unknown: Game's INT 16h Poll Point

- Unknown: Whether the game `maup.exe` reaches its INT 16h keyboard poll (or equivalent BIOS buffer read) after the IRQ livelock is eliminated by the output-buffer guard.
- Significance: Even with the guard fix, if the game is caught in its own busy-loop (e.g., waiting for a vsync or input state that it never transitions out of), the BIOS buffer will never be consumed.
- Evidence needed: Verbose log run with the guard fix, scanning for `INT 16h` calls from within the game's segment range or first game-segment code after `maup.exe` entry point.
- Priority: HIGH - this is the single remaining blocking unknown before claiming the fix is sufficient.

### TSR-Installed Vector Persistence (Code-Proven Risk)

- Fact: TSR installs INT 00h, 23h, 24h, 3Fh via INT 21h AH=25h in its own code segment (126E:xxxx). `TerminateProcess` only restores INT 22h/23h/24h from PSP fields (which reflect the values at process launch time, BEFORE the TSR installed its handlers). The TSR's INT 23h and 24h handlers may therefore persist in the IVT even after the TSR terminates, pointing to the now-resident code at 126E:xxxx.
- Risk: If `maup.exe` or DOS triggers INT 23h (Ctrl-C/Break) or INT 24h (critical error), execution jumps to 126E's handler, not the COMMAND.COM/DOS handler. This is intentional for a TSR but must be verified against expected game behavior.
- Code citation: [DosProcessManager.cs](src/Spice86.Core/Emulator/OperatingSystem/DosProcessManager.cs) `RestoreInterruptVector` only called for vectors 22h/23h/24h from **PSP fields**, while TSR installed 23h/24h to **IVT** independently at [BiosKeyboardInt9HandlerIntegrationTests.cs walkthrough 126E:005B-0069].
- Status: code-proven risk (not confirmed as active failure).

## 9. Updated Handoff Contract (Post Phase 2/3)

- What execution-phase agent can do next:
  1. Run the game with the INT09 guard fix active (committed on current branch) and verbose logs.
  2. Scan logs for `INT 16h` calls from the game's segment range post-TSR to confirm keyboard buffer polling.
  3. Inject V then C via MCP using `send_keyboard_key` (both `isPressed=true` then `isPressed=false`) and observe whether the game transitions to calibration screen.
  4. If game still does not respond: capture `read_interrupt_vector` for INT 16h, 09h, 23h, 24h post-TSR via MCP to verify vector table state.
  5. If BIOS buffer is drained but game doesn't advance: check whether game uses INT 16h AH=00h (blocking) vs AH=01h (non-blocking poll).

- What it must not assume:
  1. Do not assume UI-path MCP fix changes functional keyboard behavior (both paths call the same PS2Keyboard.EnqueueKeyEvent).
  2. Do not assume the guard fix alone is sufficient without a fresh run confirming the game reaches INT 16h.
  3. Do not assume TSR-installed INT 23h/24h vectors are benign post-TSR (they persist in IVT).

- Blocking unknowns:
  1. Whether `maup.exe` reaches INT 16h keyboard poll after IRQ livelock is eliminated.
  2. Whether TSR-persistent INT 23h/24h handlers affect game execution paths.
  3. Whether MCP key injections occur at the right execution window relative to the game's input poll loop.

## 10. Execution Validation Result (2026-05-02)

- Runtime result: A fresh batch-path run with the INT09 output-buffer guard active, MCP enabled, and `V`/`C` press-release injection at the live INT 16 polling window succeeded; the game responded and the emulator then exited normally.
- Evidence source: live runtime validation during `logs/log-20260502_003.txt` run plus MCP interaction on port 8082.
- Status: runtime-proven.

- Fact: During the successful run, a paused MCP snapshot captured `CS:IP = F000:003C`, confirming the execution window was inside BIOS INT 16 while polling for keyboard input.
- Evidence source: `read_cpu_state` MCP response during the live run.
- Status: runtime-proven.

- Fact: The required MCP transport contract for HTTP calls is an `Accept` header that includes both `application/json` and `text/event-stream`; without it, Spice86 rejects requests with `Not Acceptable`.
- Evidence source: live MCP request/response behavior during this validation run.
- Status: runtime-proven.

- Conclusion: For the Maupiti batch startup scenario, the critical emulator-side fix is the INT09 output-buffer guard. The remaining MCP keyboard routing change is structural, and no additional emulator compatibility fix is required to make this startup path work.

- Resolved unknowns:
  1. `maup.exe` does reach a BIOS INT 16 keyboard polling window after the livelock fix.
  2. MCP keyboard injection works when sent as full press/release pairs at that live polling window.

- Remaining non-blocking observation:
  1. The verbose log still captures repeated INT09 entries with `ST=0x1C` around the INT 16 polling window, but those snapshots no longer imply a startup blocker for this scenario because the end-to-end batch-path interaction completed successfully.
