# Task 06 — Graph artifact format

Part of the shared format library.

## Source

- [project-file-format.md](../requirement/project-file-format.md): §Graph
  artifact, §Importing a producer's blocks
- [spice86-import.md](../requirement/spice86-import.md): NFR4

## Goal

Define the colocated bulk artifact (JSON) that holds the observed CFG:
nodes, edges, block membership, and the learned poison set. The project
file references it through the `graph` field.

## Deliverables

- JSON reader/writer for the artifact, evolving the existing `CfgReload`
  artifact shape. The new *write* shape may change freely, but the reader
  keeps accepting the current legacy shape for one transition: existing
  recorded projects (the Dune graph) must migrate losslessly through one
  load-then-dump round trip. The legacy reader can be removed once
  existing projects are migrated.
- Addresses are name-keyed on write; numeric addresses are accepted on
  read (NFR4).
- `bindingKey` field matching a `bindings` entry of the project file. A
  mismatching key is rejected; an absent key loads (legacy dumps).
- Path resolution: a relative `graph` path resolves against the project
  file's own directory, never the current directory; absolute paths are
  taken as given.
- Producer-block support: blocks may carry instruction bytes so a block in
  a `dynamic` space can be decoded portably; blocks in `entryDelta` spaces
  decode from memory (relocated). Stated instruction lengths are kept so
  the loader can cross-check.

## Acceptance

- Round trip of an artifact with nodes, edges, and poison set.
- A numeric-keyed legacy artifact loads; the rewritten artifact is
  name-keyed.
- Golden test on a legacy fixture (a captured `CfgReload` dump, or a slice
  of the Dune one): load legacy, write new, reload, and assert the graph
  is equal — same nodes, edges, block membership, and poison set. The
  legacy files are never rewritten in place.
- Mismatching `bindingKey` rejects with a warning; absent key loads with
  an information line.

## Dependencies

- Task 01 (address model), task 04 (address scalar parsing).
