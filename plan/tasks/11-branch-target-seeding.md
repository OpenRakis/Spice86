# Task 11 — Branch target seeding

Spice86 side, R1.

## Source

- [spice86-import.md](../requirement/spice86-import.md): §Branch target
  seeding (and its subsections: seeded when the branch is first decoded,
  destinations resolve at seed time, riders)
- [project-file-format.md](../requirement/project-file-format.md): §Branch
  targets

## Goal

Seed imported indirect-branch destinations into the speculative explorer.
A target widens speculation, so it is fenced: untrusted, seeded lazily,
and never poisoning.

## Deliverables

- `targets` destinations stay `(space, offset)` in the model; they resolve
  when the seed fires, not at load time.
- Seeding happens when the node at the source address is first created
  (`InstructionsFeeder` handing the instruction to
  `SpeculativeExplorer.ExploreFrom`), never at startup. Startup seeding
  would decode whatever occupies the region and permanently poison the
  addresses it exists to reach.
- An unresolvable destination (unbound space, bytes that do not decode) is
  skipped silently at seed time, reported at information level, and
  retried the next time the branch is decoded.
- Riders:
  - Seeded nodes are speculative; `MethodEmitter.VerifySpeculativeEntryOrFail`
    covers a wrong entry.
  - A seed wires no continuation; it carries predecessor and edge type
    only.
  - An imported seed never adds to the poison set.
  - The key is not qualified by instruction bytes; the entry applies to
    every byte variant at that address.

## Acceptance

- A target into a space loaded later (for example a driver space bound
  mid-run) seeds once the source branch is decoded after the binding
  exists.
- A dead `targets` entry (source never decoded) produces no warning and is
  written back unchanged.
- No seeded destination ever appears in the poison set.

## Dependencies

- Tasks 07, 08.
