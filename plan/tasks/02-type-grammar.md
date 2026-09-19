# Task 02 — Type grammar parser and writer

Part of the shared format library.

## Source

- [project-file-format.md](../requirement/project-file-format.md): §Types,
  §Grammar, §Repetition
- [design-decisions.md](../requirement/design-decisions.md): reserved
  spellings (`enum`, `union`, `stream`, typedef and bitfield forms), no
  expression as an `array` count

## Goal

Parse and emit the type string vocabulary: integers (`uint8`..`int32`),
strings (`string(N)`, `cstring`), arrays (`array<T, N>`), pointers
(`nearptr16/32`, `farptr16/32`, optional `<pointee>`, near-only `@space`
pin), `code` as a pointee, and struct names.

## Deliverables

- Tokenizer and parser following the BNF in §Grammar, with unrestricted
  nesting.
- AST node per construct.
- Structural checks surfaced to the validator (task 05):
  - `array<T, N>` element must have a fixed size; `N` is a literal.
  - `@space` is near-pointer only; `@` on a far pointer does not parse.
  - `code` is valid only in pointee position.
  - Struct and space names must be defined (resolution hook, checked by the
    validator against the model).
  - Reserved spellings do not parse: `enum`, `union`, `stream`, typedef
    forms, bitfield forms.
- Writer emitting the canonical form (one space after the `array` comma).
- Size computation for sized types (used by data extents, struct layout
  checks, and R2).

## Acceptance

- Round trip: parse then write yields the canonical spelling for every
  grammar production.
- Every reserved spelling is rejected with a distinct diagnostic.
- Size computation matches the layout rules for each sized type.

## Dependencies

- Task 01 (struct and space name resolution hook).
