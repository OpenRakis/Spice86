# Task 10 — Data extents feed the poison set

Spice86 side, R1.

## Source

- [project-file-format.md](../requirement/project-file-format.md): §Data
  extents, and why they are worth importing
- [README.md](../requirement/README.md): §Shared context (narrow/widen
  rule)

## Goal

Derive a byte extent from every typed `globals` entry and feed it to the
speculative explorer as pre-poison, so data is not decoded as code. A data
range narrows speculation, which is the fail-safe direction, so it is
imported freely.

## Deliverables

- Extent calculation: address plus `sizeof(type)` from the type grammar's
  size computation (task 02), floored at one byte. There is no separate
  `ranges` section; the extent is arithmetic on a fact the file already
  carries.
- Imported extents union with the learned poison set in the explorer.
- The imported poison is not written into the graph artifact's learned
  poison; it is re-derived from `globals` on each load.
- Start with `HashSet<SegmentedAddress>`; note the upgrade path to an
  interval set.

## Acceptance

- A global of type `array<IntroPart, 48>` at `seg000:0337` pre-poisons
  exactly `0x337..0x577` (NFR4 acceptance).
- The explorer refuses to decode inside an imported extent.
- Extents in an unbound `dynamic` space are skipped and reported at
  information level.

## Dependencies

- Tasks 02, 07, 08.
