# Plan: maup.EXE Post-Prompt Allocation Failure (Primary Pivot)

## Core Objective

`maup.EXE` reaches VGA init and PIT/PIC reprogramming after the TSR handoff, but then crashes inside a
two-call `AH=48h` memory allocation sequence at `19C9:03DB`. The emulator log confirms:

```
DOS operation from AllocateMemoryBlock failed with an error
State ... EAX=0x4863 EBX=0xFFFF
```

Disassembly of the failure site:

```asm
19C9:03DB  B448        mov AH,0x48
19C9:03DD  BBFFFF      mov BX,0xFFFF
19C9:03E0  CD21        int 0x21          ; probe: BX=0xFFFF -> should error, CF=1, BX=largest free
19C9:03E2  891E7F01    mov word ptr DS:[0x017F],BX
19C9:03E6  53          push BX
19C9:03E7  B448        mov AH,0x48
19C9:03E9  CD21        int 0x21          ; allocate BX paragraphs -> this fails
```

The pattern is a textbook DOS `AH=48h` max-probe: request `0xFFFF` paragraphs, expect failure with
`CF=1` and `BX` updated to the largest available block, then allocate exactly that. Any emulator error
in the first call's CF/BX return corrupts the second call.

Everything else (TSR, batch, VGA, PIT/PIC) is already proven working. The investigation is now
narrowed to the DOS memory allocation subsystem and the MCB arena state at the point `19C9:03E2` runs.

## Investigation Rules

1. Trust only live, MCP-captured snapshots. No assumptions about DOS memory layout.
2. All claims about `AH=48h` behavior must be tied to the Spice86 handler code path, not to the DOS spec alone.
3. Do not implement any fix until the causal chain is proven from live BX values before and after both calls.
4. Do not skip the MCB arena audit — it is the prerequisite for any allocation analysis.
5. The active PR (`fix: correct COMMAND.COM PSP[0x02] to prevent memory exhaustion from incorrect TerminateAndStayResident call`) may already be relevant. Read its diff before assuming we need a new fix.

## Cloud Continuation Bootstrap (Mandatory)

The cloud agent must not restart from the old root cause. This investigation continues from the
current branch state where TSR/PSP baseline fixes and MCP memory diagnostics are already in place.

### Branch Pinning (Do First)

Cloud often defaults to `master`; that is wrong for this investigation.

1. Ensure repository is on branch `copilot/fix-dx-calculation-issue`.
2. Pull latest remote for this branch before any run or analysis.
3. Confirm commits include:
    - `fix: correct COMMAND.COM PSP[0x02] to prevent memory exhaustion from incorrect TerminateAndStayResident call`
    - `feat(mcp): add tools to read DOS MCB chain, PSP chain, and memory map for diagnostics`

### Game Artifact Source (Cloud-Available)

Preferred source in repo: `tests/testdata/maupiti.zip`.

Issue reference (historical, possibly outdated metadata but valid attachment source):
`https://github.com/OpenRakis/Spice86/issues/2137`
attachment: `https://github.com/user-attachments/files/27274701/maupiti.zip`

Extraction target in cloud runner:

1. Extract zip into a working folder, for example `<workspace>/tmp/maupiti`.
2. Use executable `MAUPITI.BAT` from extracted files.
3. Keep extracted hash directory `D0288AAE38E0F90C92A32A50B1233CD3A457B1268A87B6E01B95C7B2452BF01B/` intact for breakpoint file placement.

### Required Launch Mode In Cloud

Always launch headless with dummy audio and MCP enabled:

```powershell
dotnet run --project src/Spice86 -- --Debug \
   -c "<extracted_game_parent_dir>" \
   -e "<extracted_game_dir>\\MAUPITI.BAT" \
   -r "runs\\maupiti-alloc-investigation" \
   --HeadlessMode Minimal \
   --AudioEngine Dummy \
   --McpHttpPort 8083 \
   --VerboseLogs
```

### Breakpoints.json Requirement

Before launch, write breakpoints to:

`runs/maupiti-alloc-investigation/D0288AAE38E0F90C92A32A50B1233CD3A457B1268A87B6E01B95C7B2452BF01B/Breakpoints.json`

Minimum required breakpoints:

- `CPU_INTERRUPT 0x21` with condition `ah == 0x48`
- `CPU_INTERRUPT 0x21` with condition `ah == 0x31`

If the runtime does not auto-load all triggers from file, re-add missing ones (such as
`MACHINE_STOP`) through MCP immediately after startup.

## Known Evidence (Do Not Rediscover)

- TSR entry `126E:0000` runs correctly; `INT 21h/AH=31h` keep call is real with `DX=0x17C7`.
- Batch resumes via internal path (`EXEC` flow, not guest `AH=4Bh`); `maup.EXE` runs with PSP at `0x1935`.
- `INT 16h` reads for `V` and `C` both succeed (scan codes `0x2F/0xAF` and `0x2E/0xAE`).
- VGA `320x200`, PIT 0 `~100 Hz`, PIC IMR `FE` all happen after `C`. Proven up to `2035:04D7`.
- The machine-stop breakpoint fires at `2035:0525` immediately after the allocation error.

## Phase Plan

### Phase 1: Trap Both AH=48h Calls Live

Goal: capture exact BX and CF returned by the first probe, and BX/AX/CF at the second allocation.

Steps:

1. Start a fresh paused run:
   ```
   dotnet run --project src/Spice86 -- --Debug -c "C:\jeux" -e "C:\jeux\maupiti\maupiti.bat"
       -r "runs\maupiti-alloc-investigation" --McpHttpPort 8083 --VerboseLogs
   ```
2. Install exactly these breakpoints via MCP:
   - `CPU_INTERRUPT 0x21` condition `ah == 0x48` (catches both calls)
   - `CPU_INTERRUPT 0x21` condition `ah == 0x31` (TSR keep sentinel to know when to start watching)
   - `MACHINE_STOP`
3. Resume until `AH=31h` fires; capture PSP and MCB chain via `read_dos_state`.
4. Resume until first `AH=48h` fires; capture full `read_cpu_state`.
5. Continue single-step or resume until second `AH=48h` fires; capture full `read_cpu_state`.
6. Note: the handler returns by modifying registers in-place before IRET; reading state *after* the
   break (if on entry) or using a `CPU_EXECUTION_ADDRESS` at `19C9:03E2` lets you see the returned BX.

Exit criteria:

- Exact BX value returned by the first `AH=48h` probe is known and recorded.
- Whether that BX is sane given the MCB arena is confirmed.

### Phase 2: MCB Arena Full Audit

Goal: understand the exact memory map at the moment of the allocation failure.

Steps:

1. Use `read_dos_state` (includes MCB listing) captured after `AH=31h` and again after first `AH=48h`.
2. Walk the MCB chain:
   - Identify every allocated and free block.
   - Identify the largest contiguous free region in paragraphs.
   - Compare that to the BX Spice86 returned from the first `AH=48h` probe.
3. Check whether the TSR resident block (`DX=0x17C7` keep request) was correctly sized by DOSInt21h
   and whether the MCB immediately above it is a real free block or is corrupted.
4. Relevant Spice86 sources:
   - `src/Spice86.Core/Emulator/OperatingSystem/Dos.cs` or its successor
   - `src/Spice86.Core/Emulator/InterruptHandlers/Dos/DosInt21Handler.cs`
   - `src/Spice86.Core/Emulator/OperatingSystem/Memory/` — MCB management

Exit criteria:

- Largest free block as computed by MCB walk equals or does not equal BX returned by probe.
- Discrepancy (if any) is tied to a specific MCB boundary or size field.

### Phase 3: Inspect the AH=48h Handler in Spice86

Goal: verify Spice86's `AllocateMemoryBlock` matches DOS behavior for the max-probe pattern.

DOS spec for `INT 21h/AH=48h`:
- Input: BX = paragraphs to allocate.
- If enough memory: CF=0, AX = segment of new block.
- If not enough: CF=1, AX=8 (insufficient memory), **BX = largest available block in paragraphs**.

Steps:

1. Locate the `AH=48h` handler in `DosInt21Handler.cs` (search for `AllocateMemoryBlock` or `0x48` in
   the interrupt handler dispatch).
2. Verify that on failure it:
   a. Sets CF=1 and AX=8.
   b. Updates BX to the largest free block, not leaves BX as the caller's input or sets BX=0xFFFF.
3. Verify that when the caller sends BX=0xFFFF and there is no 64K+ contiguous block, the returned
   BX is the actual largest block, not `0xFFFF` unmodified.
4. Compare the handler to DOSBox source (`shell/program.cpp` and `dos/dos_memory.cpp`, function
   `DOS_AllocateMemory`) for any behavioral gap.

Exit criteria:

- Handler is correct OR a specific defect in BX-on-failure return is identified and linked to the crash.

### Phase 4: Read Active PR Diff and Assess Overlap

Goal: determine whether PR #2138 (`fix: correct COMMAND.COM PSP[0x02]`) already addresses the MCB
arena corruption or is independent.

Steps:

1. Diff `src/Spice86.Core/Emulator/InterruptHandlers/Dos/` between `master` and
   `copilot/fix-dx-calculation-issue`.
2. Read any MCB-related changes, especially around `TerminateAndStayResident`, PSP `[0x02]` field,
   and memory block sizing.
3. Determine if applying the PR branch fixes the BX corruption path.
4. If the PR is related but incomplete, note what additional change is needed.

Exit criteria:

- Clear statement: PR #2138 fixes the issue OR does not touch the relevant code path.

### Phase 5: TDD Fix

Goal: implement the smallest fix proven by the above chain.

Steps:

1. Write a failing ASM integration test that:
   - Allocates some memory, probes max with `AH=48h/BX=0xFFFF`, checks CF=1 and BX < 0xFFFF.
   - Then allocates exactly that BX; checks CF=0 and AX is a valid segment.
2. Run the test and confirm it fails in the current code.
3. Implement the narrowest possible fix in the identified handler or MCB code.
4. Re-run the test and confirm it passes.
5. Re-run the full `maupiti.bat` startup (MCP-controlled, without `--Debug` after the initial probe,
   or let it run headless) and confirm the allocation failure log line is gone.
6. Run the full test suite (`dotnet test tests/Spice86.Tests`) and confirm no regressions.

Exit criteria:

- Red test turns green.
- `maup.EXE` advances past `19C9:03E9` and game renders the main screen.
- `dotnet test` is fully green.

## Required MCP Session Sequence

Each session that touches this investigation must capture and record:

1. `read_dos_state` after `AH=31h` (TSR keep sentinel): full MCB list.
2. `read_cpu_state` at first `AH=48h` entry: AX, BX, DS, CS, IP.
3. `read_cpu_state` at `19C9:03E2` (after first `AH=48h` returns): BX changed or not.
4. `read_cpu_state` at second `AH=48h` entry: BX value fed to second call.
5. `read_cpu_state` or log capture at `19C9:03E9` return: CF, AX, BX.
6. `read_disassembly` from `19C9:03DB` for at least 20 instructions to cover the full allocation block.

Do not claim a session was informative unless all six data points are documented.

## Breakpoints.json Shortcut

Write the following directly into
`D0288AAE38E0F90C92A32A50B1233CD3A457B1268A87B6E01B95C7B2452BF01B/Breakpoints.json`
before launching so the breakpoints survive restarts without MCP re-installation:

```json
{
  "Breakpoints": [
    { "Trigger": 33, "Type": "CPU_INTERRUPT", "IsEnabled": true, "ConditionExpression": "ah == 0x48" },
    { "Trigger": 33, "Type": "CPU_INTERRUPT", "IsEnabled": true, "ConditionExpression": "ah == 0x31" },
    { "Trigger": 0,  "Type": "MACHINE_STOP",  "IsEnabled": true }
  ]
}
```

Note: `MACHINE_STOP` is not restored from file by the current serializer, but including it in the
file is harmless; add it via MCP after launch when needed.

## Success Definition

Done means all are true:

1. Exact BX value from first `AH=48h` probe is captured live and compared to actual MCB largest free.
2. The Spice86 `AH=48h` handler code is audited and its failure-path BX return is verified or corrected.
3. A failing test for the two-call max-probe pattern exists and turns green.
4. `maup.EXE` runs past `19C9:03E9` and the game reaches its main screen on the full batch path.
5. Full test suite is green with no regressions.
