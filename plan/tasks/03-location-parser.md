# Task 03 — Location parser

Part of the shared format library.

## Source

- [project-file-format.md](../requirement/project-file-format.md):
  §Locations, §An 8-bit half of a 16-bit binding
- [design-decisions.md](../requirement/design-decisions.md): stack-slot
  locations stay out

## Goal

Parse the closed set of storage locations used by assertions and signature
params.

## Deliverables

- Parser for the closed set: gp32 (`eax`..`edi`), gp16 (`ax`..`di`), gp8
  (`al`..`bh`), segment registers (`es`..`gs`), flags (`cf`, `zf`, `sf`,
  `of`, `pf`, `af`). Lowercase only.
- Rejection of the bp-relative stack-slot spelling (`@ +6`, `@ -0x10`); the
  spelling is reserved and does not parse in this pass.
- Overlap model: which locations overlap which (half vs parent register),
  needed by R3's backward scan and by NFR5's
  `location-not-an-address-base` classification. A binding on a half binds
  only that half; a binding on the parent does not type the halves.
- Classification of locations that can serve as an address base (usable by
  R3) vs value-only locations (8-bit halves, flags, segment registers,
  `ax`/`cx`/`dx`/`sp`).

## Acceptance

- Every accepted spelling parses; every reserved or unknown spelling is
  rejected with a diagnostic.
- Overlap queries return correct results for half/parent pairs.

## Dependencies

None (standalone; consumed by tasks 01, 05, 19).
