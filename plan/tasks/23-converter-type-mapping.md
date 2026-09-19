# Task 23 — Type mapping and struct layout derivation

chani converter.

## Source

- [chani-converter.md](../requirement/chani-converter.md): §The type
  mapping, §Struct layout, §Location bindings, §Segment default scope

## Goal

Translate chani's type and location vocabulary into the format's type
grammar, and derive static struct layouts.

## Deliverables

- Type mapping table, including:
  - Display wrappers (`hex()`, `dec()`, `bin()`, `char()`) dropped
    silently; signedness maps as the type, not a display modifier.
  - `unknown` maps to `uint8` (a data extent, no layout claimed).
  - `ofs16(seg001)` maps to `nearptr16@seg001`.
  - Tuple types `(T, U, ...)`: warn, drop, and count (no way to say where
    each member lives). The empty tuple `()` becomes an absent `returns`.
- Struct layout derivation:
  - Field offsets are a packed cursor over the ordered field list; no
    alignment or padding is inserted.
  - `size` is the cursor over all fields, emitted only when every field
    has a fixed size. A trailing variable-length field: emit the fields,
    omit `size`.
  - A non-trailing variable-length field: warn and drop the struct.
  - Read every layout syntax chani accepts (both forms), reusing chani's
    parser so the layout computation is identical (flat form last-wins
    dedupe, sub-dict form no dedupe).
- Location bindings map one-to-one (accepted set minus 32-bit registers);
  stack-slot bindings are dropped with a count; `@bp` binds the BP
  register and is not a stack slot.
- Segment defaults: a space-level `assume` becomes a `segmentDefaults`
  entry covering the whole space. An attr-level `assume` equal to the
  space default is dropped silently; one that differs is warned and
  dropped (the range end cannot be computed without a decoder and CFG).

## Acceptance

- Every chani type in the Dune databases converts or is reported as a
  counted drop; no type string the shared grammar rejects is emitted.
- Derived struct layouts match chani's own layout computation on the same
  input.

## Dependencies

- Tasks 02, 21, 22.
