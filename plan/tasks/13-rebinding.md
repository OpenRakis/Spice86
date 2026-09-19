# Task 13 — Rebinding and stale-binding reporting

Spice86 side, R1.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §Address space
  binding at run time, §Minting and rebinding

## Goal

Let the user merge a minted space onto a declared one, and surface
possibly stale bindings. Spice86 never silently replaces a stored
`bindings` number; a number reaches `bindings` only through a rebind.

## Deliverables

- `--BindSpace <declared>=<minted>` command-line option. The rebind is a
  merge:
  - The minted space's number moves to the declared space.
  - Every fact learned under the minted name is re-keyed.
  - The graph artifact is re-keyed.
  - The minted entry disappears.
  - The result is written back (task 9 appends the `bindings` entry).
- Report lines that drive the user to rebind:
  - An unbound declared space with waiting facts: names the space, the
    minted space observed instead, its number, and the waiting fact count.
  - A bound space that holds facts and saw no observed activity is
    reported as possibly stale, with advice to rebind.
  - A space bound under a different launch key is reported at information
    level.

## Acceptance

- After `--BindSpace segvga=seg_2DD0`, the dump holds the declared space
  with the number, all facts re-keyed, and no minted entry.
- Running the rebound project again reports nothing to rebind.
- A run with one of two alternative driver spaces bound dumps the other
  space's facts unchanged (NFR5 acceptance).

## Dependencies

- Tasks 07, 08, 09.
