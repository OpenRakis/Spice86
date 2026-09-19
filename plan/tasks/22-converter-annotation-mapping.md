# Task 22 — Annotation mapping (Tables A, B, C)

chani converter.

## Source

- [chani-converter.md](../requirement/chani-converter.md): §Where a
  producer's annotations land (§Effective type, §Table A, §Table B,
  §Table C), §Functions, and the code marker that is not one, §Comment
  placement

## Goal

Map every chani annotation to its destination entity, completely and
mechanically. Each table is complete over its domain, so completeness is
checkable.

## Deliverables

- Effective type computation: `code` when `type = code` or the attr has
  `fn`; otherwise the stated type, or absent.
- Table A: the entity an attr becomes, from `name` presence and effective
  type (functions, globals, labels, or nothing).
- Table B: the other attr keys (`fn`, `ofs_seg`, `comment`, `targets`,
  `let`, ...) each mapped to its destination. `fn` is the only Table B
  key that affects the entity decision.
- Table C: annotations above attr level (project, file, segment, struct),
  including the silent drops: segment-level `arch = 8086`, `notes`,
  `format`, `start`/`end`, `type = code|data` tags. Dropped items are
  counted in the report.
- Code markers: an attr with `type = code` and no `name` is dropped as a
  marker (never synthesize a name); every other fact at that address
  (ofs_seg, comment, targets, let) is kept. The dropped-marker count is
  reported.
- An unnamed attr with `fn` becomes assertions with `when: pre` for
  in/inout bindings; `out` bindings are dropped and counted. A named attr
  with `fn` and no `type` becomes a `functions` entry with the signature.
- Comment placement: when the attr produces an entity, the comment goes on
  that entity's `comment` field; otherwise it becomes a `comments` entry.

## Acceptance

- Table-driven tests: one case per Table A row and per Table B/C key.
- Converting the Dune databases keeps every named fact and reports every
  drop with a count.

## Dependencies

- Task 21.
