# Task 15 — Function naming and label promotion

Spice86 side, R2.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §R2, §Labels, and
  promotion to a function name
- [README.md](../requirement/README.md): §Shared context (function naming
  has a single channel)

## Goal

Loaded names reach generated code through the existing channel:
`FunctionInformation.Name` in `FunctionCatalogue`, consumed by
`CfgPartitionNameProvider`. Two sections feed it: `functions` always, and
`labels` when the partitioner rooted a method at the label's address.

## Deliverables

- `functions` entries become `FunctionInformation` entries.
- Label promotion: a `labels` entry whose address the partitioner rooted a
  method at supplies the method's name. A `functions` entry at the same
  address wins.
- Promotion is recorded in the model during the run; the next dump writes
  the entry under `functions` with `name` and `comment` and drops it from
  `labels`. The promoted entry carries no signature.
- Promotion is idempotent and one-way: a second run reads the `functions`
  entry and promotes nothing; nothing ever demotes. A stale `functions`
  entry is harmless.
- A label the partitioner did not root a method at supplies no name, stays
  in `labels`, and is not reported as declined. A label in a data space is
  never a partition root, so it never promotes.
- A named producer attr carrying `fn` and no `type` becomes a `functions`
  entry with that signature, not a `labels` entry (converter rule; the
  loader honors it).

## Acceptance

- Generated code uses loaded function names where the address matches.
- A promoted label appears under `functions` in the next dump and is gone
  from `labels`; loading that dump and re-running yields an equal model
  and a promotion count of zero.
- A non-rooted label produces no warning, no declined line, and is still a
  `labels` entry in the next dump.

## Dependencies

- Tasks 08, 09, 14.
