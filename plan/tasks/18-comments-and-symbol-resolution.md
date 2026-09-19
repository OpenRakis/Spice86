# Task 18 — Comments, signatures, and symbol comments in generated code

Spice86 side, R2.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §R2
- [project-file-format.md](../requirement/project-file-format.md):
  §Comments, §Offset pins, §Segment defaults

## Goal

Surface loaded comments and static facts in the generated source, using
the space-resolution query from task 07 at emit sites.

## Deliverables

- Comment slots follow storage (no `kind` field):
  - `functions[].comment` at an address the partitioner rooted a method at
    becomes the method's doc comment; at any other address it becomes a
    comment line above the instruction's disassembly line.
  - Every other comment (entity `comment` fields, `comments` entries)
    becomes a comment line above the instruction's disassembly line.
- A loaded `signature` is emitted as a comment at the function's entry
  address, not as method metadata. `signature.convention` is not honored
  in generated code (R2 non-goal).
- Symbol comments at emit sites: R2 asks the space-resolution query for an
  instruction field's offset; when the offset resolves to a known
  function, global, or label in that space, R2 emits the symbol name as a
  comment line.

## Acceptance

- The doc-comment vs body-line split follows the partition result for the
  same comment text.
- An offset pin whose offset matches a named symbol in the pinned space
  produces a comment naming it.
- With a `ds` segment default over a code range, no pin, and no observed
  segment value, `mov ax, [0x25c]` names the global at `0x25c` in the
  resolved space.

## Dependencies

- Tasks 07, 08, 15.
