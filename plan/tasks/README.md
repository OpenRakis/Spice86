# Tasks — Spice86 project file

Implementation tasks derived from the [requirement documents](../requirement/README.md).
Each file is one PR-sized task with sources, deliverables, acceptance
criteria, and dependencies.

## Areas

| # | task | area |
|---|---|---|
| 01 | [Project file model classes](01-format-model.md) | shared format library |
| 02 | [Type grammar parser and writer](02-type-grammar.md) | shared format library |
| 03 | [Location parser](03-location-parser.md) | shared format library |
| 04 | [YAML serializer and deserializer](04-yaml-serializer.md) | shared format library |
| 05 | [Validator and diagnostics](05-validator.md) | shared format library |
| 06 | [Graph artifact format](06-graph-artifact.md) | shared format library |
| 07 | [Address resolution, minting, space-resolution query](07-address-resolution.md) | Spice86 — R1 |
| 08 | [Loader and launch settings](08-loader-and-launch.md) | Spice86 — R1 |
| 09 | [Writer and round trip](09-writer-roundtrip.md) | Spice86 — R1 |
| 10 | [Data extents feed the poison set](10-data-extents-poison.md) | Spice86 — R1 |
| 11 | [Branch target seeding](11-branch-target-seeding.md) | Spice86 — R1 |
| 12 | [Import a producer's blocks](12-block-import.md) | Spice86 — R1 — postponed |
| 13 | [Rebinding and stale-binding reporting](13-rebinding.md) | Spice86 — R1 |
| 14 | [Fact usage report (NFR5)](14-usage-report.md) | Spice86 — R1 |
| 15 | [Function naming and label promotion](15-function-naming.md) | Spice86 — R2 |
| 16 | [Struct accessor generation](16-struct-accessors.md) | Spice86 — R2 |
| 17 | [Typed globals accessors](17-globals-accessors.md) | Spice86 — R2 |
| 18 | [Comments, signatures, symbol comments](18-comments-and-symbol-resolution.md) | Spice86 — R2 |
| 19 | [Block-local backward type scan](19-backward-type-scan.md) | Spice86 — R3 |
| 20 | [Typed-access comments in generated code](20-typed-lowering.md) | Spice86 — R3 |
| 21 | [Converter scaffolding and database merging](21-converter-scaffolding.md) | chani converter |
| 22 | [Annotation mapping (Tables A, B, C)](22-converter-annotation-mapping.md) | chani converter |
| 23 | [Type mapping and struct layout](23-converter-type-mapping.md) | chani converter |
| 24 | [Binary-dependent duties](24-converter-binary-duties.md) | chani converter |
| 25 | [Converter report, end-to-end validation](25-converter-report.md) | chani converter |
| 26 | [SQ1: observed segment values](26-sq1-observed-segments.md) | side quest — postponed |
| 27 | [SQ2: persist runtime samples](27-sq2-runtime-samples.md) | side quest — postponed |
| 28 | [SQ3: interrupt dictionaries](28-sq3-interrupt-dictionaries.md) | side quest — postponed |

## Ordering

R1 is a prerequisite for R2 and R3 (README §Purpose). The format library
(01-06) comes first because both Spice86 and the converter consume it.
The converter track (21-25) can run in parallel with the Spice86 import
track (07-20) once the library exists; only task 25's end-to-end test
needs tasks 08-09.

```
01 ── 02 ── 04 ── 05 ── 06
       │     03─┘
       │
       ├─ Spice86 track:   07 ── 08 ── 09 ── 13
       │                    │      ├── 10, 11, 12, 14
       │                    │      └── 15 ── 18
       │                    │           16 ── 17
       │                    └── 19 (needs 03, 14)
       │                         20 (needs 07, 16, 17, 19)
       │
       └─ converter track: 21 ── 22 ── 23 ── 24 ── 25 (needs 08, 09)
```

Parallelism inside the Spice86 track: after task 08, tasks 09-14 are
mostly independent of each other. R2 (15-18) needs 08 and 14; task 18
also needs 15. R3 (19-20) comes last.

Side quests 26-28 are **postponed**: kept as tasks, but not to be started.
The main tracks are large, so what these tasks require may change before
they are reached. Re-evaluate them once R3 is done. Note for that review:
SQ1 (26) fills the "observed" rung of the space-resolution order, so it is
the first candidate to revive if R2/R3 need it.

## Conventions

- `§Section name` references point into the requirement documents; the
  [README table](../requirement/README.md#where-each--reference-points)
  says which document holds each section.
- Every code task follows the repository rules in `AGENTS.md`: TDD, full
  test suite green before submit, ASM-based tests preferred, and every
  `MachineTest` scenario mirrored in `GeneratedCodeMachineTest`.
