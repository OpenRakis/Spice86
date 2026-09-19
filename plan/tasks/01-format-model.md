# Task 01 — Project file model classes

Part of the shared format library that lives in the Spice86 repository and is
consumed by both Spice86 and every converter.

## Source

- [project-file-format.md](../requirement/project-file-format.md): §Top-level
  shape, §Address spaces, §Launch and reproducibility, §When an assertion
  holds, §Offset pins, §Segment defaults, §Branch targets, §Comments,
  §Required fields per section
- [README.md](../requirement/README.md): §Shared context (address
  representation, partial-by-design)

## Goal

Define the in-memory model of the project file: plain classes with no parsing
logic. The model is Spice86-owned and format-neutral (NFR1): no producer
vocabulary appears in it.

## Deliverables

- Root model with all top-level sections, every section optional.
- `Launch` (exe, exeArgs, cDrive, programEntryPointSegment,
  providedAsmHandlersSegment, initializeDos).
- `FileEntry` (path, hash). No `role` field; the spelling is reserved.
- `AddressSpace` with exactly one of `entryDelta` (paragraphs, signed,
  anchored at the load image base), `absolute`, or `dynamic`.
- `Binding` keyed by launch configuration (exe hash,
  programEntryPointSegment, initializeDos, exeArgs) mapping space name to
  segment number.
- `StructDefinition` (name, optional size, comment, fields) and
  `StructField` (name, type, offset, optional comment, optional `repeat`
  with `count` field reference and `unit: bytes|elements`).
- `FunctionEntry` (address, required `name`, optional comment, optional
  `signature` with convention, params, returns; each param has name, type,
  location, `dir: in|out|inout`).
- `Assertion` keyed by `(address, location, when)`, `when` defaulting to
  `pre`.
- `OffsetPin` (address of the constant's own bytes, pointer type).
- `SegmentDefault` (space, reg, resolves, optional start/end).
- `BranchTarget` (source address, flat `to` destination list; nothing else).
- `Global` (address, required `type`, optional name), `Label` (address,
  required `name`, no type), `Comment` (address, text; no `kind`).
- Address reference type holding `(space name, offset)`; a raw numeric form
  must be representable until resolution (task 07).

## Acceptance

- Model compiles and is consumable without referencing YAML or any producer
  format.
- Unit tests construct each section and read it back through the model.

## Dependencies

None. First task of the format library.
