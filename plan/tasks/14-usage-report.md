# Task 14 — Fact usage report (NFR5)

Spice86 side, R1/R2 boundary. Implement the counting skeleton with R1 and
extend it as R2/R3 consumers land.

## Source

- [spice86-import.md](../requirement/spice86-import.md): NFR5

## Goal

An imported fact that is not used says so. Generation reports, per fact
kind: loaded, applied, and declined counts, with a reason per declined
fact. Declining is never an error and never blocks generation.

## Deliverables

- Report infrastructure: per-kind counters, per-fact declined reasons, and
  a quiet path when everything applied.
- The closed reason set: `shape-not-provable`, `type-unknown-here`,
  `register-written-in-body`, `non-static-field`, `off-field-offset`,
  `no-symbol-at-target`, `layout-not-expressible`,
  `target-space-not-bound`, `target-not-decodable`,
  `observed-space-disagrees`, `location-not-an-address-base`.
- Promotion count: labels promoted to functions (task 15).
- Unbound-space lines: the report names the unbound declared space, the
  minted space observed instead, and the waiting fact count.
- Unused facts stay in the model and appear in the next dump.

## Acceptance

- Importing a file whose facts the current consumers cannot use produces
  no warning and no silent loss: every unused fact appears in the report
  with a reason and is present in the next dump.
- A run where every fact applied prints a quiet report.

## Dependencies

- Tasks 07, 08 (extended by 15-20).
