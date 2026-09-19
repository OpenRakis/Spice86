# Task 21 — Converter scaffolding and database merging

chani converter (own repository, consuming the shared format library from
the Spice86 repository).

## Source

- [chani-converter.md](../requirement/chani-converter.md): intro, §Merging
  several producer databases (§The name prefix, §A driver space with no
  number)
- [README.md](../requirement/README.md): §Purpose

## Goal

A standalone tool that reads one or more chani databases and emits one
Spice86 project file, using the shared model, serializer, validator, and
type grammar. It never invents format vocabulary; the loader treats an
unknown spelling as an inconsistency.

## Deliverables

- Repository and CLI skeleton: one primary module plus any number of
  secondary modules as input, one project file as output.
- Reading a chani database as stored data (reuse chani's parser where
  possible; no rebuilding of its static analysis).
- Merge rules:
  - `launch` comes from the primary module alone; secondaries contribute
    no launch inputs.
  - Every module's file becomes a plain `files` entry.
  - `project:` comes from the primary's project name.
- Name prefix: every space of a secondary module is renamed
  `<module>_<space>`, where `<module>` is the secondary's project name
  lowercased with characters outside `[a-z0-9_]` replaced by `_`. The
  prefix is unconditional, applies to struct names too, and reaches every
  place a space name appears: YAML keys, `space`/`resolves` in
  `segmentDefaults`, and `@<space>` pins inside type strings. Function,
  global, and label names need no prefix (their key is the address).
- A driver space with no segment number stays `dynamic` with no binding.

## Acceptance

- Merging three databases each declaring `seg001` yields a file the loader
  accepts with no duplicate-name inconsistency.
- The emitted file validates cleanly with the shared validator.

## Dependencies

- Tasks 01-05 (shared library published/consumable).
