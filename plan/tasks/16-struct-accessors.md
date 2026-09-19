# Task 16 — Struct accessor generation

Spice86 side, R2.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §R2
- [project-file-format.md](../requirement/project-file-format.md): §Types,
  §Repetition

## Goal

Each `structs` entry becomes a generated memory-based data-structure class
(the existing `MemoryBasedDataStructure` pattern over `IByteReaderWriter`)
with named getters and setters at the stated field offsets.

## Deliverables

- Code generation of one accessor class per struct: named getters/setters
  at correct offsets, types mapped to accessor widths.
- A `repeat` field generates two accessors: a count accessor (reading the
  referenced sibling field) and an indexed element accessor.
- A struct with no `size` whose last field is variable-length generates
  fixed fields at their stated offsets. The terminator-scanning accessor
  for the last field is deferred — no scanning infrastructure exists yet —
  and the field is reported (`terminator-scan-deferred`, NFR5). The struct
  is still generated: its fixed fields are what manual refactoring of the
  generated code works with until SSA.
- A struct with a variable-length field that is not last is declined and
  reported (`layout-not-expressible`), not emitted.
- A type the accessor model cannot express is skipped and reported, never
  guessed.
- Name sanitization and address collisions produce no duplicate or invalid
  C# identifiers.

## Acceptance

- Generated accessor classes compile; field offsets match the file layout;
  a written value reads back through the accessor.
- For a `repeat` field with count N: element N-1 reads back, element N is
  refused.
- The declined struct appears in the NFR5 report with its reason.
- A variable-length last field appears in the NFR5 report as
  `terminator-scan-deferred`; its struct's fixed fields still generate and
  compile.

## Dependencies

- Tasks 02, 08, 14.
