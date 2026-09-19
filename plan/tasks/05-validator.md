# Task 05 — Validator and diagnostics

Part of the shared format library.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §R1 / Validation
  and reporting
- [project-file-format.md](../requirement/project-file-format.md):
  §Required fields per section
- [design-decisions.md](../requirement/design-decisions.md): §Rejected
  alternatives (reserved spellings)

## Goal

Report inconsistency as a warning; never report absence. An inconsistent
entry is not applied to any consumer, is preserved verbatim in the model,
and is written back unchanged on the next dump. Inconsistency never blocks
a load and never deletes a fact.

## Deliverables

Warning-level checks (inconsistency):

- Address whose `space` is not defined.
- Address space with none of `entryDelta`/`absolute`/`dynamic`, or more
  than one; `entryDelta` not a valid signed number.
- Two `addressSpaces` entries with the same name.
- A space using a reserved device name (`ivt`, `biosdata`, `vga`, `cga`,
  `vgarom`, `bios`) at a different number, or a minted spelling its own
  number contradicts.
- Overlapping struct fields; a field extending past a stated `size`.
- Type string that does not parse (task 02 rules), or naming an undefined
  struct or address space.
- `array<T, N>` whose element has no fixed size.
- `repeat` whose `count` does not name a field of the same struct, names
  one at a higher offset, or names one whose type is not an unsigned
  integer; `repeat` on an element type with no fixed size.
- `launch.exe` matching no `files` entry; `hash` not spelled
  `sha256:<64 lowercase hex>`.
- Graph artifact whose `bindingKey` does not match the current run.
- `targets` entry naming an undefined space, or with an empty `to` list.
- Offset pin whose stated pointer width disagrees with the decoded field
  length (check runs at snapshot/generation time, when the graph exists).
- `segmentDefaults` entry with undefined `space`/`resolves`, `reg` equal to
  `cs` or not a segment register, `end` not above `start`, or overlapping
  another entry with the same `space` and `reg`.
- Assertion with `when` neither `pre` nor `post`; `when` on an offset pin;
  two assertions for the same `(address, location, when)` with different
  types; `location` naming no accepted register or flag (including the
  stack-slot spelling).
- Reserved spelling used as a key: `kind` on a comment, `role` on a file
  entry, `source` on a fact. Warned about and dropped from write-back.
- `functions` entry with no `name`; `globals` entry with no `type`.
- Producer block whose decoded instruction length disagrees with the
  stated one.

Information-level (never warnings): missing graph artifact, missing
`launch`, missing `bindingKey`, absent sections, `dynamic` space with no
binding, numeric address on read, and every NFR5 declined-fact line.

## Acceptance

- One test per check above: the bad input produces the warning, the fact
  survives to write-back, and the load succeeds.
- A minimal valid file and an empty file both load with zero warnings.

## Dependencies

- Tasks 01-04.
