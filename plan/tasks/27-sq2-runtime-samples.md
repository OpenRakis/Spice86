# Task 27 — SQ2: persist observed runtime samples

**Status: postponed.** Do not start this task. Re-evaluate it once the main
tracks are done; what is required may have changed by then.

Spice86 side, side quest. Does not block R1. Investigation first.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §SQ2

## Goal

Decide what bulk per-instruction register and memory captures are worth
writing back into the project artifacts, and at what granularity, given
the file-size cost.

## Deliverables

- A short written proposal (added to the design documents, not code
  comments): candidate capture points, granularity options, estimated
  artifact sizes on the Dune workload, and a recommendation.
- If the recommendation is to proceed: a follow-up task with the agreed
  scope.

## Acceptance

- The proposal is reviewed and a go/no-go decision is recorded in
  design-decisions.md.

## Dependencies

- Tasks 06, 09 (artifact and writer exist to size against).
