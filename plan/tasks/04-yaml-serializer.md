# Task 04 — YAML serializer and deserializer

Part of the shared format library.

## Source

- [project-file-format.md](../requirement/project-file-format.md):
  §Top-level shape, §Launch and reproducibility, §Comments
- [spice86-import.md](../requirement/spice86-import.md): NFR3 (one writer,
  semantic round trip), NFR4 (numeric input)

## Goal

Read a `.spice86.yaml` document into the model (task 01) and write the model
back out. This is syntax only; semantic validation is task 05 and address
resolution is task 07.

## Deliverables

- Deserializer covering every section; every section is optional and absence
  is never a diagnostic (NFR2).
- Scalar address parsing: `<segment>:<offset>` in one YAML scalar, both
  parts hex, `0x` prefix optional, `:` delimiter, no whitespace.
  `0x170:0x83` and `170:83` are the same address. Accepted anywhere a
  `{ space, offset }` mapping is expected.
- Hash format handling: only `sha256:<64 lowercase hex>`; anything else is
  an inconsistency for the validator, not a second supported form.
- Case-insensitive path comparison with separator normalization (`\` and
  `/` equal); no filesystem lookup, ASCII case folding only.
- Serializer writing the model back. Key order, quoting, indentation, and
  number base are the emitter's choice. YAML `#` comments are not
  preserved; `comment` fields and the `comments` section are data and do
  round trip.
- Unknown and reserved keys (`kind`, `role`, `source`, ...) surface to the
  validator instead of failing the parse.

## Acceptance

- Semantic round trip: `load(write(m)) == m` and
  `load(write(load(f))) == load(f)` for representative files.
- A file containing only structs, functions, globals, and comments parses
  with no diagnostic.
- Both address spellings parse to the same model value.

## Dependencies

- Task 01 (model), task 02 (type strings), task 03 (locations).
