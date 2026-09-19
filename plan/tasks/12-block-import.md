# Task 12 — Import a producer's blocks

**Status: postponed.** Current imports come from the chani converter, which
carries no blocks; this task only matters for producers with CFG data (IDA,
Ghidra). Re-evaluate when such a producer is in scope. Notes for then: the
graph artifact (task 06) must store each instruction's stated length for the
corruption cross-check below, and `CfgNodeReconstructor` currently always
decodes from stored bytes, never from memory — decode source selection is
new work.

Spice86 side, R1. Only needed for producers with CFG data (IDA, Ghidra);
a chani conversion carries no blocks.

## Source

- [project-file-format.md](../requirement/project-file-format.md):
  §Importing a producer's blocks, §Graph artifact

## Goal

Load producer-emitted blocks from the graph artifact at startup as
speculative nodes, verified at run time.

## Deliverables

- Blocks load at startup, not at dump time, as speculative nodes.
- Decode source selection:
  - `entryDelta` spaces decode from memory (the image is relocated there).
  - `dynamic` spaces decode from producer-emitted bytes carried in the
    artifact (portable across bindings).
- A block in an unbound `dynamic` space is skipped with an information
  line and imported on a later run once a binding exists.
- A block whose decoded instruction length disagrees with the stated one
  is skipped and reported as an inconsistency.

## Acceptance

- Blocks in an image space import on the first run; blocks in a `dynamic`
  space import from the second run (after a binding is recorded).
- A corrupted block (length mismatch) is skipped with a warning and does
  not poison anything.

## Dependencies

- Tasks 06, 07, 08.
