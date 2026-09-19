# Task 07 — Address resolution, minting, and the space-resolution query

Spice86 side, first R1 building block.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §Address space
  binding at run time, §Minting and rebinding, NFR4
- [project-file-format.md](../requirement/project-file-format.md):
  §Address spaces, §Segment defaults

## Goal

Turn every `(space, offset)` reference into a `SegmentedAddress`, accept
numeric addresses everywhere, and expose the one query consumers use to
resolve a data reference to a space.

## Deliverables

- Space number computation: `entryDelta` spaces from
  `programEntryPointSegment + delta` (delta in paragraphs, signed),
  `absolute` from the stated value, `dynamic` from the matching `bindings`
  entry. A space with no binding is never an error; its facts stay
  unresolved and intact.
- Reverse mapping: a numeric address read anywhere (project file, graph
  artifact, `CfgBlocks`, execution flow) maps back through `addressSpaces`
  and `bindings` to a name.
- Minting: an observed segment number no space covers gets a name. Dedupe
  by resolved number first. Exact match against the reserved device-base
  table (`ivt` 0x0000, `biosdata` 0x0040, `vga` 0xA000, `cga` 0xB800,
  `vgarom` 0xC000, `bios` 0xF000) mints `absolute`; everything else mints
  entry-relative as `seg_<DELTA>`. Identity is the exact segment number;
  a space is never folded into one it merely falls inside. Two runs of the
  same configuration mint the same names.
- Space-resolution query for a data reference, applied in fixed
  narrowest-wins order: offset pin, then assertion with `@space`, then
  observed segment value, then segment default, then nothing. The
  "observed" rung is supplied by task 26 (SQ1), which is postponed; until
  it lands the query treats that rung as empty.

## Acceptance

- Numeric input goes back out under the resolved or minted name.
- A well-formed numeric address that cannot be reverse-mapped is reported
  as information, mints a name, and is not lost.
- The query returns the right space for each rung of the resolution order,
  with a disagreement between a static fact and an observed value reported
  as `observed-space-disagrees`.

## Dependencies

- Tasks 01, 04, 05.
