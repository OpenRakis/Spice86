# Plan: TSR-First Full Live-CFG Understanding (Primary Pivot)

## Core Objective

Primary goal is now to understand `maupiti1.exe` TSR line-by-line from live CFG evidence before pursuing any broader fix.
Canonical runtime path is the real game startup through batch execution, not isolated executable launch.

Everything else is secondary and depends on this:

1. Map every executed TSR instruction to intent.
2. Prove TSR side effects (vectors, memory, handles, DOS calls).
3. Only then analyze why batch startup fails before full game launch.

## Investigation Rules

1. Trust only live CFG-backed evidence.
2. No raw-memory code assumptions (`0xFF` pages, data interpreted as code).
3. Ignore stack-edit warnings as a root-cause signal unless CFG-linked evidence says otherwise.
4. Do not implement compatibility changes without a proven TSR-to-exit causal chain.

## Current TSR Understanding Snapshot

Already established from live CFG listing:

1. TSR installer main routine is at `126E:0000`.
2. It saves selected interrupt vectors (INT 21h AH=35h loop), then installs handlers (AH=25h for 00h/23h/24h/3Fh).
3. It builds internal context blocks via helper at `126E:0329`.
4. It runs file/device setup callback path through `126E:03A6`, `126E:042D`, `126E:043E`.
5. It terminates resident via `109E:0012 mov AH,0x31 / int 0x21`.

Detailed instruction-by-instruction notes are tracked in:

- `.github/prompts/maupiti-tsr-live-cfg-walkthrough.md`

## Phase Plan

### Phase 1: Complete TSR Line Coverage

Goal: verify each TSR line as executed, classified, and understood.

1. Enumerate TSR routines and exact executed addresses from CFG artifacts.
2. Confirm every line in these routines has semantic annotation:
   - setup/context
   - vector backup/install
   - parameter block build
   - file/device callback dispatch
   - TSR terminate-and-stay-resident
3. Mark unresolved lines (if any) with explicit unknowns and hypotheses.

Exit criteria:

- zero unclassified executed TSR instructions.

### Phase 2: TSR Side-Effect Ledger (Before/After)

Goal: convert TSR understanding into concrete machine-state deltas.

Capture and compare, pre/post TSR:

1. Interrupt vectors touched by TSR (old/new values).
2. Resident memory allocation/resize outcomes.
3. DOS handles/device classification outcomes in callback path.
4. PSP/process state relevant to child executable startup.

Exit criteria:

- one side-effect table with concrete values, not inferred behavior.

### Phase 3: Link TSR Side Effects To Main EXE Exit

Goal: test whether exit is a direct consequence of TSR state.

Approach:

1. Trap termination functions (`AH in {00,31,4C}` and INT 20h path).
2. Build comparative run matrix:
   - full BAT path (primary truth path)
   - TSR + EXE sequence
   - EXE without TSR (if executable path allows)
3. Identify first CFG divergence after TSR completion.

Mandatory constraint:

- Root-cause conclusions must remain valid on full batch startup flow.

Exit criteria:

- one proven causal chain from specific TSR side effect to exit path, or explicit proof TSR is not causal.

### Phase 4: Compatibility Survey (Secondary, Triggered Only If Needed)

Goal: only if TSR is exonerated or points to identity checks, inspect BIOS/IBM compatibility probes.

1. Track profile-sensitive startup queries (INT 11h/12h/15h, selected INT 10h/21h checks, BDA reads).
2. Compare query/response tuples against DOSBox default IBM PC AT behavior.
3. Tie mismatch to a concrete CFG branch leading toward termination.

Exit criteria:

- ranked mismatch list with one CFG-anchored likely trigger.

### Phase 5: TDD + Fix

Goal: implement the smallest fix only after proven causality.

1. Write minimal failing ASM/integration test for proven contract.
2. Implement fix in the exact responsible subsystem.
3. Re-run full Maupiti batch startup flow and full test suite.

Exit criteria:

- red test turns green, full game launches from batch path, and no regressions.

## Required Artifacts Per Session

1. Updated TSR line walkthrough file.
2. TSR side-effect ledger table.
3. Batch execution trace summary (batch context transitions, command progression, handoff into game executable).
4. Termination capture table (function, caller, registers).
5. Divergence matrix (BAT/TSR/EXE variants).

## Immediate Next Steps

1. Continue live CFG acquisition for any TSR lines not yet annotated.
2. Build pre/post TSR side-effect ledger from MCP snapshots.
3. Execute termination traps right after TSR completion and map first post-TSR divergence on full batch path.
4. Confirm whether batch engine context/flow diverges before full game handoff.

## Success Definition

Done means all are true:

1. Every executed TSR line is understood and documented.
2. TSR side effects are measured, not guessed.
3. A proven causal path to batch startup failure is identified (or TSR is disproven as cause).
4. Full game launch through batch path is achieved.
5. Fix and tests are based on that proof.
