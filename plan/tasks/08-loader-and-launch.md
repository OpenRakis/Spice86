# Task 08 — Load the project into Spice86 and apply launch settings

Spice86 side, R1.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §R1 (Goal,
  Functional requirements), NFR1, NFR2
- [project-file-format.md](../requirement/project-file-format.md): §Launch
  and reproducibility

## Goal

Wire the format library into Spice86: load a project file at startup into
the internal model, attach every fact to a `SegmentedAddress`, and use
`launch` to configure the run.

## Deliverables

- Importer boundary: only the importer knows file syntax; the generator,
  `FunctionCatalogue`, R2, and R3 depend on the neutral model alone (NFR1).
- Command-line option naming the project file; loading it produces the
  model with every resolvable fact attached to a `SegmentedAddress`.
- `launch` applies when the corresponding command-line flag is absent; a
  supplied flag overrides the file value.
- Launch target resolution: `launch.exe` is looked up in `files` by path,
  case-insensitively with separators normalized. A file with no `launch`
  is valid.
- Graph artifact loading when `graph:` is present and the `bindingKey`
  matches or is absent.
- A fact whose space has no binding stays unresolved and intact in the
  model.
- Wiring in `Spice86DependencyInjection` in dependency order.

## Acceptance

- A converter output with only structs, functions, globals, and comments
  (no graph, no binding, no launch) loads, runs, and dumps with no
  warnings (NFR2).
- A converted COM project loads and runs with `launch.exe` naming a `.COM`
  path.
- A driver database conversion with one `files` entry and no `launch`
  loads with no warning.
- A project merged from three producer databases each declaring `seg001`
  (prefixed by the converter) loads with no duplicate-name inconsistency.

## Dependencies

- Tasks 01-07.
