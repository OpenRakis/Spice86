# Task 17 — Typed globals accessors

Spice86 side, R2.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §R2

## Goal

Each typed `globals` entry becomes a named accessor in the space it
belongs to, in a generated `GlobalsOn<Space>` class that takes its
segment value at construction (base = segment << 4). The caller supplies
the segment — typically captured from a segment register at function
entry — because DS is not constant in hand-written assembly: the class is
neither hardwired to the space's bound segment value nor tied to a live
register the way `MemoryBasedDataStructureWith<Seg>BaseAddress` is. The
register-bound variants stay as they are for hand-written use.

## Deliverables

- One generated `GlobalsOn<Space>` class per space holding globals,
  constructed from a segment value, with an accessor per entry at the
  right offset and width.
- A global with no `name` gets an identifier derived from its address
  (`data_<OFFSET>`, or `data_<space>_<OFFSET>` when disambiguation is
  needed). The next dump still carries no `name`; the identifier is
  derived, not a stored fact (never synthesize a `name`).
- Globals in an unbound space are skipped and counted, not errors.
- Struct-typed globals use the accessor classes from task 16.

## Acceptance

- Generated classes compile; a written value reads back through each
  accessor.
- An unnamed global generates an address-derived accessor that reads and
  writes at the right offset, and the next dump still has no `name` on it.

## Dependencies

- Tasks 08, 14, 16.
