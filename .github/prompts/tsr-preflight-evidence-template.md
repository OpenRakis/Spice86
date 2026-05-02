# TSR Preflight Evidence Template

## Hard Facts

- Fact:
- Code citation:
- Symbol:
- Status: code-proven

## Failure Surface

- Name:
- Invariant:
- Probable break mechanism:
- Primary evidence source:
- Secondary evidence source:

## DOS Interrupt Contract Table

- Interrupt:
- Entry conditions (registers and state):
- Expected side effects:
- Expected return semantics:
- Code citation:

## Batch Engine Contract Table

- Batch operation/context transition:
- Expected stack/context behavior:
- Expected ERRORLEVEL and control-flow behavior:
- Code citation:

## XMS Contract Table

- XMS operation:
- Expected allocation/move/map behavior:
- Conventional-memory interaction risk:
- Code citation:

## EMS Contract Table

- EMS operation:
- Expected page-frame/mapping behavior:
- Conventional-memory interaction risk:
- Code citation:

## Checkpoint A (Pre-TSR)

- CPU state captured:
- DOS state captured:
- DOS program state captured:
- Interrupt vectors captured:
- Memory windows captured:
- CFG snapshot captured:

## Checkpoint B (Post-TSR)

- CPU state captured:
- DOS state captured:
- DOS program state captured:
- Interrupt vectors captured:
- Memory windows captured:
- CFG snapshot captured:

## Divergence Capture

- Trigger condition:
- First divergent address:
- Context depth:
- Call stack summary:
- New path summary:
- Interrupt context transitions:
- Batch context transitions:

## Ranked Hypotheses

1.

- Claim:
- Confidence:
- Supporting evidence:
- Missing evidence:
- Falsification step:

1.

- Claim:
- Confidence:
- Supporting evidence:
- Missing evidence:
- Falsification step:

## Handoff Contract

- Ready for execution-phase work:
- Must not assume:
- Blocking unknowns:
