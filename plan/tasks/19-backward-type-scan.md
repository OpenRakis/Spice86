# Task 19 — Block-local backward type scan

Spice86 side, R3.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §R3, §What the
  coming SSA work must preserve
- [project-file-format.md](../requirement/project-file-format.md): §When
  an assertion holds, §An 8-bit half of a 16-bit binding

## Goal

Bring a type fact from a function-entry `in` param or an `assertions`
entry to the instruction that uses the value. Pre-SSA, this is a backward
scan inside one basic block, not dataflow.

## Deliverables

- From a use site, walk back to the nearest assertion for that location,
  stopping at any write to it.
- A write to any overlapping location stops the scan too (`mov dl, 0`
  stops a scan for `dx`).
- An assertion on a merely overlapping location does not answer the scan
  (a `dx` assertion never types `dl`).
- `when` decides only whether an assertion on the scan's own starting
  instruction counts: `pre` yes, `post` no.
- Assertions on value-only locations (8-bit halves, flags, segment
  registers, `ax`/`cx`/`dx`/`sp`) load and round trip but are reported as
  `location-not-an-address-base`.
- Scope: use sites outside the asserting basic block are out (R3
  non-goal). The address-keyed shape `(address, pre)` = live-in,
  `(address, post)` = definitions must stay translatable to future SSA.

## Acceptance

- `pre` and `post` assertions at the same address for the same location
  both load, and each reaches only its own side's use sites.
- An assertion on `dx` does not type a later use of `dl`; `mov dl, 0`
  between a `dx` assertion and a `[bx]` access through DX stops the scan.
- An assertion on an 8-bit half round trips and appears in the NFR5
  report as `location-not-an-address-base`.

## Dependencies

- Tasks 03, 08, 14.
