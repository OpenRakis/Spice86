# Task 25 — Converter report and end-to-end validation

chani converter. Final converter task.

## Source

- [chani-converter.md](../requirement/chani-converter.md): all
  warn-and-drop and counted-drop rules
- [spice86-import.md](../requirement/spice86-import.md): NFR2 acceptance

## Goal

Every drop is visible. The converter run ends with a report, and its
output round trips through Spice86 cleanly.

## Deliverables

- Report with a count per drop category:
  - Warned drops: tuple types, ambiguous offset pins, attr-level `assume`
    differing from the space default, branch target keys not at an
    instruction start, structs with a non-trailing variable-length field,
    stack-slot bindings.
  - Silent-but-counted drops: segment-level `notes`/`format`/
    `start`/`end`/`type` tags, attr-level `assume` equal to the default,
    code markers, unnamed-`fn` `out` bindings.
- A run without binaries states which duties were skipped (offset pins,
  hashes, variable-length extents) instead of appearing to succeed.
- End-to-end test: convert the Dune databases (dncdprg and drivers), load
  the output in Spice86, run, and dump. The load produces no warnings
  (NFR2), and `load(write(load(f))) == load(f)` holds.

## Acceptance

- Converting a database with one of each droppable construct yields a
  report line per category with the right count.
- The end-to-end round trip passes.

## Dependencies

- Tasks 21-24; tasks 08-09 on the Spice86 side for the round-trip test.
