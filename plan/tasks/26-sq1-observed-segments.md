# Task 26 — SQ1: record observed segment values per memory access

**Status: postponed.** Do not start this task. Re-evaluate it once the main
tracks are done; what is required may have changed by then.

**Hot-path warning.** Every variant of this task hooks the compiled-expression
execution path (`AstExpressionBuilder`, `node.CompiledExecution`) — the same
hot path whose cost deferred task 20's rewrite. Even the cheapest version adds
a per-access check inside compiled code. Any re-evaluation must start from
that constraint: capture belongs in a slow instrumented mode or at
node-creation / first-execution time, never in steady-state execution.

Spice86 side, side quest. Does not block R1.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §SQ1

## Goal

Capture, per memory-accessing CFG node, the effective segment value
observed for the access — ideally also which segment register supplied
it. This is the "observed" rung of the space-resolution order (task 07).

## Deliverables

- Cheapest version first: one segment value recorded per node on first
  observation.
- Upgrade to a set only when the case appears (self-modifying code, code
  reused with a different DS).
- Feed the observations into the space-resolution query, enabling:
  - Attributing an absolute access no `segmentDefaults` entry covers
    (R2/R3).
  - Grouping globals into `GlobalsOn<Space>` by observed space.
  - Resolving concrete targets of dynamic near code pointers (vtables,
    jump tables).
  - The disagreement check that reports a wrong or over-extended segment
    default (`observed-space-disagrees`).

## Acceptance

- A run records the observed segment per accessing node and the
  space-resolution query uses it when no pin or assertion applies.
- A segment default contradicted by an observed value produces an
  information line.

## Dependencies

- Task 07 (query), task 14 (report).
