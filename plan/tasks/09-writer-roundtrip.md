# Task 09 — Write the project back on dump

Spice86 side, R1.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §R1 (write-back
  requirements), NFR3

## Goal

One writer, one explicit output path, and a semantic round trip. The
writer never changes a fact on its own; only a run does.

## Deliverables

- Dump path writes the completed project file to an explicit output path;
  output is never assumed to equal input.
- Write-back rules:
  - Names are minted for address spaces discovered during the run.
  - A rebind appends a `bindings` entry (task 13).
  - Newly observed `files` are appended.
  - A promoted label moves from `labels` to `functions` (task 15 records
    the promotion; the writer only writes it).
  - Unresolved facts (space with no binding) are written back unchanged,
    with the unresolved space name intact, in their own section.
  - Inconsistent entries are preserved verbatim; reserved keys (`kind`,
    `role`, `source`) are dropped.
- Graph artifact written next to the project file, name-keyed, with the
  current `bindingKey`.

## Acceptance

- A project written by Spice86 and loaded again yields an equal model.
- A loaded converter output, written and loaded again, yields an equal
  model (`load(write(load(f))) == load(f)`).
- A run that cannot bind a space dumps that space's facts unchanged.
- A legacy dump (current artifact shape, as recorded for the Dune
  project) round trips through load, write, load with an equal graph:
  same nodes, edges, block membership, and poison set. The legacy input
  files are left untouched.

## Dependencies

- Tasks 07, 08.
