# Spice86 project file — design

This folder holds the design of the Spice86 project file. The text was split out
of `RE_IMPORT_REQUIREMENTS.md`, which held all of it in one file.

The split moved text and changed nothing else, with three exceptions. Every
original heading is still here and no paragraph was dropped.

1. Every section that became the top of a new document moved up one heading
   level, and its subsections moved with it.
2. The split added 17 headings. Those headings name text that had no heading of
   its own before, so a `§` reference can now point at it. The converter duties
   are the bulk of them, because each one used to sit inside the format section
   it serves.
3. Four sentences used the word "above" to point at text that is now in another
   document. Each one now names the document or the section instead. The four are
   in §Rejected alternatives, §Deferred, §Where a producer's annotations land, and
   §Branch target keys.

## The four documents

| document | question it answers |
|---|---|
| [project-file-format.md](project-file-format.md) | What does the project file hold, and what does the colocated graph artifact hold? |
| [chani-converter.md](chani-converter.md) | How does a converter turn chani databases into one project file? |
| [spice86-import.md](spice86-import.md) | What does Spice86 do with the file it loads, and what does it write back? |
| [design-decisions.md](design-decisions.md) | Which alternatives were rejected, and which work is deferred? |

The three sections below are shared by all four documents.

## Purpose

Give Spice86 a **project file** it owns: a single place that records how to launch
a program, how its address space is laid out, and what is known about its code
and data. Spice86 **reads** it at startup, **complements** it while running, and
**writes** it back on dump. The round trip is Spice86 → Spice86.

External reverse-engineering tools are one-way producers upstream of it:

- A **standalone converter** (one per tool: chani, IDA, Ghidra) reads **one or
  more** databases of that tool and emits a single Spice86 project file. One
  program is often split across several databases, so merging them is the
  converter's job (§Merging several producer databases). Converters live outside
  the core app, each in its own repository. The format model, serializer,
  validator and type grammar live in the Spice86 repository as a shared library
  that both Spice86 and every converter consume.
- **Spice86 reads and writes only its own format.** It never parses `.chani`, an
  IDA `.idb`, or a Ghidra project.

The format is Spice86's own vocabulary because Spice86 stores more than any
single tool can express: the numeric runtime segment binding, an observed
control-flow graph, generation state. A converter output is a static snapshot;
the project file is a living store.

This document states **what** we want for three staged requirements and defines
the format they share. **R1 is a prerequisite for R2 and R3.**

1. **R1 — Load** the project file into an internal Spice86 model, and write it
   back.
2. **R2 — Consume** the model to name generated functions and generate
   memory-based data structures.
3. **R3 — Typed access**: use type facts to emit typed field/pointer access
   where it can be proven, falling back to raw access otherwise.

## Shared context

- **Spice86 is the owner of the format, not a guest in it.** Heavy static
  analysis (disassembly, pointer and type inference) stays in the producers.
  Spice86 loads facts attached to addresses, adds what running the program
  teaches it, and writes the whole thing back.

- **Addresses are `(address space name, offset)`.** Numeric segment values are
  written in exactly two places — address-space definitions and bindings
  (§Address spaces) — and nowhere else in anything Spice86 *writes*, including the
  bulk graph artifact. This is what keeps a project valid across a configuration
  change: the numbers are recomputed or re-bound, the facts are untouched.
  On *read*, a numeric address is accepted anywhere a `{ space, offset }` mapping
  is expected, spelled as the scalar `"0x170:0x83"`. It carries information — the
  producer had no minted names — and is not an error. The loader reverse-maps it
  through `addressSpaces` and `bindings` and mints a name when no space covers it
  (§Minting and rebinding), so the fact is name-keyed by the time it is written
  back. One carve-out: an artifact written for an external consumer (the Ghidra
  symbol file) keeps the addressing that consumer requires (NFR4).

- **The project file is partial by design.** Every section is optional and
  **absence is never a diagnostic**. A chani conversion carries structs,
  functions, labels, globals, comments, offset pins, segment defaults and branch
  targets, and nothing else — no blocks
  (§Importing a producer's blocks), no graph artifact, no binding — and that file
  must load and run without complaint.
  Only *inconsistency* is reported (§R1 validation).

- **Import facts that narrow speculation freely; facts that widen it
  reluctantly.** A data range narrows: the worst case is exploring less, which
  is fail-safe. An entry point widens: the worst case is decoding garbage, which
  is fail-open. This rule decides whether a candidate fact belongs in the format
  and how much it is trusted. It is why data extents are imported, why
  entry-point seeding is not (§Deferred), and why branch targets are imported but
  fenced (§Branch targets).

- **Function naming already has a single channel.** Generated method names
  derive from `FunctionCatalogue` (`FunctionInformation.Name`, keyed by
  `SegmentedAddress`) via `CfgPartitionNameProvider`. Loaded names reach that
  channel — the same door Ghidra-symbol names already use. Two sections feed it:
  `functions` always, and `labels` when the partitioner rooted a method at the
  label's address (§Labels). One channel, two sources, and the partition result
  decides.

- **The faithful generation guarantee holds.** The generator emits a faithful,
  register/memory-level translation of observed execution. Typed rewriting (R3)
  only replaces an access when it is provable; otherwise it emits the faithful
  raw access. It never produces behavior that diverges from observed execution.

- **No fact carries a provenance field.** What drives behavior is
  speculative-vs-observed, which already exists per node in the CFG and is what
  codegen guards on. The word `source` stays reserved
  (§Rejected alternatives).

## Shared non-goals (this pass)

- Exporting to a producer's format (chani, IDA, Ghidra, kaitai).
- Editing project facts via MCP.
- Rebuilding a producer's static analysis.
- Changing a producer to serve the conversion. A converter reads the producer's
  database as stored data, so a fact reaches the format only when that database
  already holds it.
- Keeping the current dump artifact shape on *write*. The new artifacts may
  change shape freely. The *read* side is not a non-goal: existing recorded
  projects (the Dune graph represents many hours of play) must migrate
  losslessly, so the loader keeps reading the current shape for one
  transition, and one load-then-dump round trip performs the migration.

---

## Where each `§` reference points

The documents keep the `§Section name` spelling for a cross-reference. This
table says which document holds each section.

| section | document |
|---|---|
| Purpose, Shared context, Shared non-goals | README.md (this file) |
| NFR1 … NFR5 | spice86-import.md |
| Format — the Spice86 project file | project-file-format.md |
| Top-level shape | project-file-format.md |
| Address spaces | project-file-format.md |
| Launch and reproducibility | project-file-format.md |
| Data extents, and why they are worth importing | project-file-format.md |
| Graph artifact | project-file-format.md |
| Importing a producer's blocks | project-file-format.md |
| Types, Grammar | project-file-format.md |
| Locations, An 8-bit half of a 16-bit binding | project-file-format.md |
| Repetition | project-file-format.md |
| When an assertion holds | project-file-format.md |
| Offset pins | project-file-format.md |
| Segment defaults | project-file-format.md |
| Branch targets | project-file-format.md |
| Comments | project-file-format.md |
| Required fields per section | project-file-format.md |
| Merging several producer databases | chani-converter.md |
| Where a producer's annotations land, Effective type, Table A, Table B, Table C | chani-converter.md |
| Functions, and the code marker that is not one | chani-converter.md |
| Converter duties | chani-converter.md |
| Deriving an address space delta | chani-converter.md |
| The launch target hash | chani-converter.md |
| Data extents and segment tags | chani-converter.md |
| The type mapping | chani-converter.md |
| Struct layout | chani-converter.md |
| Location bindings | chani-converter.md |
| Offset pin keys | chani-converter.md |
| Segment default scope | chani-converter.md |
| Branch target keys | chani-converter.md |
| Comment placement | chani-converter.md |
| Facts whose behaviour lives in Spice86 | spice86-import.md |
| Address space binding at run time | spice86-import.md |
| Minting and rebinding | spice86-import.md |
| Branch target seeding | spice86-import.md |
| Labels, and promotion to a function name | spice86-import.md |
| R1, R1 validation | spice86-import.md |
| R2 | spice86-import.md |
| R3, R3 non-goals | spice86-import.md |
| Side quests, SQ1, SQ2, SQ3 | spice86-import.md |
| Rejected alternatives | design-decisions.md |
| Deferred | design-decisions.md |

`§R1 validation` names the "Validation and reporting" heading inside R1.
