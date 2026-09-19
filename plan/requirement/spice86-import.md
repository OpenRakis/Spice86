# Spice86 — loading the project file, using it, and writing it back

Part of the [Spice86 project file design](README.md). Read
[Purpose and Shared context](README.md) first, then
[project-file-format.md](project-file-format.md), which defines every field
named here.

This document holds the three staged requirements, the non-functional
requirements they answer to, and the behaviour Spice86 adds on top of a loaded
file.

## Non-functional requirements

### NFR1 — The internal model is Spice86-owned and format-neutral

- The internal model uses Spice86's own vocabulary (its `SegmentedAddress`, its
  own type/struct/signature types). No producer's surface concepts leak into it.
- Parsing lives behind an **importer boundary**. Only the importer knows the
  file syntax; the generator, `FunctionCatalogue`, R2 and R3 depend on the
  neutral model alone.
- Adding a producer means writing another converter, with **no change** to the
  model or its consumers.

Acceptance: a consumer of the model contains no reference to any producer's
vocabulary; a second producer reuses the format and the same consumers
unchanged.

### NFR2 — A partial project loads

- No section is required. A file with no section at all is valid.
- A missing graph artifact, a missing binding, a missing `launch`, a missing
  `segmentDefaults` section: all normal. Spice86 proceeds and learns what it can
  by running.
- Diagnostics distinguish **absence** (info at most) from **inconsistency**
  (warning). See §R1 validation.

Acceptance: a converter output containing only structs, functions, globals and
comments loads, runs, and dumps a completed project file with no warnings.

### NFR3 — One writer, one output path, and a semantic round trip

The writer takes an explicit output path and never assumes output == input.
Phase 1 has a single project file and rewrites it, but nothing in the writer
may depend on that.

**The round trip is semantic, never byte-level.** Key order, quoting, block
versus flow, indentation and number base are the emitter's choice, and nothing
pins them. What must hold is `load(write(m)) == m` for every model the loader can
produce, and `load(write(load(f))) == load(f)` for every file it accepts. So a
converter owes no canonical spelling, and two spellings of one value must parse
equal. Models compare by value, section order is not significant, and each
section's key is the one that section already states. YAML `#` comments are not
preserved. The `comment` fields on entities and the `comments` section are data,
so they do round trip.

**Both properties are about the writer, not about a run.** `write` never changes a
fact on its own. A run does, and that is the point of the file: Spice86 appends a
newly observed `files` entry, mints a name for a discovered space, upgrades numeric
keys to names (NFR4), and moves a promoted label into `functions` (§Labels). So a
dump after a run is expected to differ from the file that was loaded. What the
round trip forbids is the writer inventing or losing a fact between the model and
the bytes.

### NFR4 — Numeric input loads without a migration step

Dump artifacts already exist on users' disks, written with numeric
`segment:offset` addresses, and a converter may have nothing but numbers to give.
Nobody is asked to run a re-keying script, and no producer is asked to invent
names it does not have — naming is Spice86's job, done while loading.

- **Every read accepts a numeric address** anywhere a name is expected: the graph
  artifact's `addr` / `entryPoints` / `poisonedAddresses`, `CfgBlocks` `entry`,
  the Ghidra symbol file, execution flow, and the project-file fact sections
  (`functions`, `globals`, `assertions`, `offsetPins`, `labels`, `comments`,
  `targets`). The loader resolves it to a space by reverse-mapping
  through `addressSpaces` and `bindings`, minting a name when no space covers it
  (§Minting and rebinding). A numeric address says the producer had no names to
  give; Spice86 gives it one.
- **Writing is name-keyed for files Spice86 owns**: project files and its own
  machine artifacts (graph artifact, `CfgBlocks`, execution flow). Loading a
  legacy one and dumping again upgrades it in place, with no user action.
- **Artifacts written for an external tool keep the format that tool requires.**
  The Ghidra symbol file stays `name_SEG_OFF_LINEAR 0xLINEAR f|l`: Ghidra parses
  it, so its addressing is not ours to change. Same for any future export whose
  consumer is not Spice86. Name keying is an internal invariant, not an output
  format.
- **Numeric tolerance is read-only.** It never reaches an output Spice86 owns: a
  numeric input goes back out under the name that was resolved or minted for it,
  so numbers last one round trip at most and the configuration coupling the
  name keying removes is not reintroduced. When the minted name is not the one the
  user wants, a rebind merges it onto a declared space (§Minting and rebinding).
- Absence of a `bindingKey` in an artifact is tolerated (NFR2): a legacy dump has
  none. Only a *mismatching* key is rejected.

This tolerance has a known limit. Accepting an unkeyed numeric dump is only sound
while the run configuration is unchanged. With no `bindingKey` there is nothing to
check that against. The residual protection is the existing per-node memory match
and the speculative guard. Those two turn a stale address into a rejected node
instead of a wrong one. The upgrade path is the `bindingKey` itself: once
artifacts carry it, tolerance can be narrowed to keyed dumps only.

### NFR5 — An imported fact that is not used says so

A fact may be loaded, stored, round-tripped and still consumed by nothing: a
`post` assertion whose use sites are all in another block, an offset pin on a
field the program overwrites, a struct the accessor model cannot express. That is
**expected** — the file is the durable artifact and the consumers are staged, so
an unused fact is not a defect. A fact that is dropped with no report is.

- Generation reports, per fact kind, how many were **loaded**, **applied** and
  **declined**, with a reason per declined fact: shape-not-provable,
  type-unknown-here, register-written-in-body, non-static-field, off-field-offset,
  no-symbol-at-target, layout-not-expressible, target-space-not-bound,
  target-not-decodable, observed-space-disagrees,
  location-not-an-address-base.
- A run in which everything applied prints the counts and nothing else, so the
  report is quiet when there is nothing to say.
- Declining is never an error and never blocks generation.
- Generation also reports how many `labels` were **promoted** to `functions`
  (§Labels). A promotion is written back, so the count says what this run added to
  the file, and it falls to zero on the next run of the same configuration. A label
  that stayed a label is **not** a declined fact and needs no reason: most labels
  name data, and data never promotes.

**A fact whose address could not be resolved is preserved whole.** The
strongest case is a space with no binding: Cryo Dune has two sound drivers,
DNADL and DNSDB, and exactly one loads per run, so one of the two spaces has no
number and every fact inside it is unresolvable. Those two spaces come from two
separate producer databases, which a converter merged into this one project
(§Merging several producer databases). A writer that emitted only what
it resolved would delete the whole analysis of the driver that did not load, on
every run, silently. So an unresolved fact is written back in its own section,
with every field and its unresolved space name unchanged (NFR3), and a space with
no binding is never an error — at load or at generation.

**The report is what makes an unbound space actionable.** It names the declared
space that stayed unbound, the minted space observed in its place with its number,
and the number of facts waiting, so the user can rebind (§Minting and rebinding).
Without that, a first run looks like a failed import: no names, no comments,
nothing applied, and no reason given. A space bound under a *different* launch
configuration is reported at information level instead, with no advice to rebind.
That is the expected state for the driver that did not load
(§Merging several producer databases).

Acceptance: importing a file whose facts the current consumers cannot use
produces no warning and no silent loss — every unused fact appears in the report
with a reason, and is still present in the next dump. A run that binds only one
of two alternative driver spaces dumps the other one's facts unchanged.

This is the general form of a specific bug: a producer annotation can be the
second-most-common thing in its database and still vanish between import and
output with nobody able to tell. Counting is what makes that visible.

---

## Facts whose behaviour lives in Spice86

Three format sections state a field whose whole meaning is a Spice86 action. The
field itself is in [project-file-format.md](project-file-format.md); the action
is here.

### Address space binding at run time

**A stored number can go stale, and phase 1 cannot detect it.** Something outside
the key — a setup file choosing a differently-sized driver — shifts every space
allocated after it, so `segvga: 0x2F40` can become wrong under an unchanged key.
Spice86 cannot notice this on its own. Noticing would mean recognising that a
segment observed at `0x3100` *is* `segvga`, which is the identification problem
below; without it, the moved module looks like a new unnamed space and the stale
entry is never questioned. So:

- **Spice86 never silently replaces a binding.** A number reaches `bindings` only
  through a rebind, so the user is always the one who put it there. A space Spice86
  minted needs no binding at all: it is `absolute` or `entryDelta`, and both resolve
  from the launch configuration (§Minting and rebinding).
- **A stale binding is visible, not silent.** Its facts resolve to addresses where
  nothing matches, so the NFR5 report shows that space applying almost nothing,
  and any generated node there fails the speculative guard. One extra check makes
  it explicit and costs nothing: a bound space that holds facts and saw **no
  observed activity at all** is reported as possibly stale, with the advice to
  rebind. This has false positives — a driver whose code genuinely never ran in
  that session — so it is a report line, never an error.
- **Real detection needs the content anchor** (§Deferred). Comparing memory at the
  stored number against a per-space byte pattern answers both questions with one
  mechanism: a mismatch means the binding is stale, and scanning for a match
  rebinds it. Until then, staleness is repaired the same way it is created, by
  hand.

The `bindingKey` guard on the graph artifact is unaffected by any of this: it
protects nodes restored at startup, which only exist in `entryDelta` spaces, and
those depend on `programEntryPointSegment` alone.

#### Minting and rebinding

Spice86 must **mint a name** for any segment number it observes that no space
covers, so a discovered fact is portable. Without this, everything learned at
runtime writes back numerically and the whole scheme leaks on the first round
trip. Minting happens in two places — a space discovered by running, and a
numeric address read on input (NFR4) — and both take the same path.

**Dedupe by resolved number, before minting.** Every known space resolves to a
number: `entryDelta` plus launch, `absolute` as stated, `dynamic` through
`bindings`. An observed number equal to one of those **is** that space, so the
name is reused and nothing is minted. This is also what makes a rebind durable,
because the declared space then resolves to the number that used to mint.

**Identity is the exact segment number.** One space per distinct observed
segment, and a number that merely falls *inside* another space is its own space.
`0xA001` is not `vga` plus `0x10`. Folding would rebase every offset in the
space, so a fact would stop reading like the `[0]` the instruction actually used.
The cost is that physical aliasing becomes ordinary rather than an overlay edge
case: a `global` named at `vga:0x10` gets no name when code reaches it through
`0xA001:0x0`, and R2 and R3 silently miss it. Aliasing stays deferred, and there
is no space-level report for it. `addressSpaces` states no size, so two spaces
have no extent to compare, and a 64 KiB window would mark every adjacent
allocation as overlapping. §Deferred records the fact-level check that reports
the missed name instead.

**Well-known device bases have reserved names**, matched on the exact number and
nothing else: `ivt` (`0x0000`), `biosdata` (`0x0040`), `vga` (`0xA000`), `cga`
(`0xB800`), `vgarom` (`0xC000`), `bios` (`0xF000`). A project file declaring one
of these at a different number is an inconsistency, not a silent override. The EMS
page frame is not in the table because its base is configurable, so it is not a
fixed region.

**The kind comes from that table alone, and it is a guess.** An observed segment
equal to a reserved device base mints `absolute` at that number. Everything else
mints entry-relative, `entryDelta` = observed segment − `programEntryPointSegment`,
named `seg_<DELTA>` in uppercase hex with no `0x` and no padding. `0xA001` is not
a device base, so it mints `seg_9E91` with `entryDelta: 0x9E91` under a
`programEntryPointSegment` of `0x170`.

One run cannot do better than guess. `0xA001` observed once does not say whether
the code loaded a constant or computed `programEntryPointSegment + 0x9E91`; both
produce that number. So a segment inside a device region that is not its base —
plane access at `0xA800`, a normalized far pointer into the framebuffer — gets
entry-relative and moves when the load base moves, which is wrong.

**A wrong kind costs one hand edit.** The kind lives in `addressSpaces` and the
facts are name-keyed, so the repair is rewriting `entryDelta: 0x9E91` as
`absolute: 0xA001`: the name does not change, no fact is re-keyed, and the graph
artifact is untouched. That is the property the name keying exists for. Detection
needs nothing new either — a space resolved to a number where nothing matches is
exactly the possibly-stale report already specified above.

**A minted name is a persisted key**, in the project file and in the graph
artifact, so the same configuration must mint the same name on the next run.
Both spellings are stable by construction: a reserved name because the region is
fixed, and `seg_<DELTA>` because a delta stays correct across a change of load
base, and the raw number does not. Every spelling is self-describing, so a
declared space whose name matches one of them while its number says otherwise is
an inconsistency (§R1 validation).

The minted kind has a known limit. It comes from one observation, so a constant
segment and a computed one are indistinguishable in a single run. The
upgrade path is differential: two runs with different `--ProgramEntryPointSegment`
settle every minted kind at once, because an absolute space's observed number does
not move and an entry-relative one's does (§Deferred).

Minting alone is not enough to make a producer's `dynamic` space usable, and this
is the gap that decides whether a converted database works at all. A segment
number observed at runtime does not say **which** named space it is. So on a first
run Spice86 mints, say, `seg_2DD0` for what it observes, while the converter's `segvga`
stays unbound — and stays unbound on every later run too, because nothing ever
connects the two. Every fact in `segvga` is then dead weight. In the Dune
database that is 171 of 319 branch target edges.

The file link cannot settle it either. A converter knows the space came from
`DNVGA.BIN`, and Spice86 records DOS reads in `files`, but the format
deliberately stores no file-to-memory mapping (§Top-level shape) because a
compressed or overlay file does not land in memory linearly. Dune ships `.HSQ`
files, so the read destination is a decompression buffer, not the space base.

Phase 1 resolves it by **rebinding**, driven by the user:

- The report names the unbound declared space, the minted space observed instead,
  and its number, together with how many facts are waiting (NFR5).
- The user rebinds the minted space onto the declared one:
  `--BindSpace segvga=seg_2DD0`.
- Rebinding is a **merge**, not a text edit: the number moves to the declared
  space, every fact learned under the minted name is re-keyed, the graph artifact
  is re-keyed with it, and the minted entry disappears. The graph artifact is
  machine-written and voluminous, so this cannot be done by hand — a rebind that
  only edited the project file would leave the graph pointing at a name that no
  longer exists.
- DOS allocation is deterministic for a fixed launch configuration, so a rebind
  is done once per configuration and then lives in `bindings`.

Automatic binding by matching space content is the upgrade path, not phase 1
(§Deferred).

### Branch target seeding

#### Seeded when the branch is first decoded, not at startup

An imported target is **not** seeded at startup. It is seeded the first time a
node is created at the source address — where `InstructionsFeeder` hands the
freshly parsed instruction to `SpeculativeExplorer.ExploreFrom`.

Startup is not merely a worse choice, it does not work. In the Dune database 171
of the 319 edges point into `segvga`, which is `DNVGA.BIN` read through DOS
(`load = [0x100..]:dnvga` in chani). Those bytes are not in memory when the exe
is loaded. Decoding them at startup reads whatever occupies the region, and an
invalid decode adds the address to the poison set, which is monotonic and
persisted. A startup seed would permanently poison the very addresses it exists
to reach.

Seeding on first decode of the source has none of that. While the region is still
packed the branch is not reached, so nothing is seeded. Once the branch is
reached, the module it dispatches into is loaded — that is what the branch is
for.

#### Destinations resolve at seed time, not at load time

A destination in a `dynamic` space has no number until its binding is known, and
for `segvga` that happens partway through the first run. So `targets`
destinations are the one place where R1's "resolve every address at load" does
not apply: they stay `(space, offset)` in the model and resolve when the seed
fires. An unresolvable destination is skipped, reported at info level, and
retried the next time the branch is decoded.

The source address is resolved the other way round. The explorer holds a
`SegmentedAddress` and asks whether any targets are keyed there, which is the
reverse mapping NFR4 already requires for reading numeric dump addresses.

#### Riders

- **Seeded nodes are speculative**, so `MethodEmitter`'s
  per-instruction `VerifySpeculativeEntryOrFail` guard covers a wrong entry. A
  stale or wrong target costs dead generated code, never divergent behavior.
- **A seed wires no continuation.** It carries a predecessor and an edge type and
  nothing more. This is the untrusted-seed shape; `SeedKnownSafe` stays reserved
  for emulator-installed handlers.
- **An imported seed never poisons.** The explorer poisons an address whose
  decode is invalid. A target that lands on a region not yet unpacked must be
  skipped silently instead, so a later run can still reach it.
- **The key is not qualified by instruction bytes,** so the entry applies to every
  byte variant at that address. This is deliberate. A patched branch is exactly
  the case the annotation exists for, and the list is the union of destinations
  across the patched variants; a byte-qualified key would match none of them
  (§Deferred, per-fact byte anchor).

### Labels, and promotion to a function name

A `labels` entry is a name at an address with no type. **A label whose address the
partitioner roots a method at supplies that method's name.** So `labels` is not
only the fallback for a type that cannot be expressed. It is the section a producer
uses when it has a name and cannot say whether the address is code or data.

This exists because a producer often cannot say. chani's attr `type` is not a
classification of the byte: `type = code` is a **seed** for its recursive
disassembly, listed beside the exe entry point and every `targets` destination. A
byte becomes code when that walk decodes it, not when an attr says so. So a
function chani reaches on its own — through a call edge or a fall-through — carries
a name and no `type` at all. `draw_game_ui` at `dncdprg` `seg000:0086` is one:
`seg000:0083` falls into it and `seg000:3768` calls it, so nobody had to mark it.

316 names in the Dune databases are this shape, against 891 that carry
`type = code`. 56 of the 316 also carry a `fn`, so the effective-type rule routes
them to `functions` (§Functions). The remaining **260** reach `labels`, and
promotion is what turns them into method names.

**Promotion is what keeps those 260 names without a converter-side decode.** The
alternative was to rebuild chani's own derivation in the converter: decode from the
seeds, follow calls and fall-through, and ask whether the address came out as a
call target. That needs the original binaries and an x86 decoder to answer a
question Spice86 answers for certain later, from a real CFG rather than a static
guess. Reading `type` as a classification instead is not an option — it would send
`draw_game_ui` to `labels`, where no method name comes out.

Four properties make this cheap rather than a new mechanism:

- **The two sections differ only in naming.** A `functions` address "is explicitly
  not a boundary and seeds nothing" (§Functions), so nothing about speculation,
  poison or partitioning depends on which section a name sits in. Only
  `FunctionCatalogue` does.
- **R2 already asks this question.** `functions[].comment` becomes a doc comment,
  "falling back to a line at the entry address when the partitioner did not root a
  method there" (§Comments). Promotion reuses that same query for the name.
- **Data needs no separate filter.** A label in a data space is never a partition
  root, so it is never promoted. The code/data split comes from the CFG, and no
  byte is classified twice.
- **Being wrong costs a name.** A label promoted at an address that is both a named
  data location and a partition root yields a misleading method name and nothing
  else, which is the same cost a wrong assertion carries (§When an assertion
  holds).

**A promoted label is written back as a `functions` entry.** The promotion is a
fact Spice86 learned by partitioning, so it belongs in the file. §Purpose already
says Spice86 reads the file at startup, complements it while running, and writes it
back on dump, and this is exactly such a complement. Discarding it would make every
later run re-derive the same answer, and it would leave the file saying less than
the run knew. The entry moves whole: the `name` and any `comment` go to the
`functions` entry, and the `labels` entry is gone.

**Promotion happens during a run, and the writer only records it.** The writer
promotes nothing on its own, so NFR3 still holds: `load(write(m)) == m` for the
model as it stands, and a dump taken with no partition result promotes nothing.
This is the same shape as the other things a run adds — a newly observed `files`
entry, a minted space name, a numeric artifact upgraded to name keys (NFR4).

**Promotion is idempotent and one-way.** The second run reads a `functions` entry
and has nothing left to promote, so two runs of one configuration produce the same
file and the promotion count falls to zero. Nothing ever demotes a function back to
a label. That is deliberate: after the write-back a promoted entry is
indistinguishable from one a user declared or another producer stated, there is no
provenance field to tell them apart (§Shared context), and demotion would delete a
name the user may have put there by hand.

**A stale `functions` entry is harmless, which is what makes persisting it safe.**
A later partition may root no method at that address. The entry then states a name
candidate nobody uses, and a `functions` address "is explicitly not a boundary and
seeds nothing" (§Functions), so nothing about speculation, poison or partitioning
changes. NFR5 already reports a fact the consumers declined.

A promoted label carries **no signature**, because a `labels` entry has no
`signature` field and gains none. That costs nothing: an attr that states a
signature is routed to `functions` by the effective-type rule instead (§Functions).

---

## R1 — Load and write the project file

### Goal

A Spice86-owned, format-neutral in-memory model (in `Spice86.Core`) that later
stages query by address, populated from the project file and written back to it.

### Functional requirements

- Parse the project file. Every section is optional; absence is not a
  diagnostic.
- Resolve each `address` to a `SegmentedAddress` by looking its `space` up in
  `addressSpaces`, computing the number from `entryDelta` + launch,
  `absolute`, or a matching `bindings` entry. Accept a numeric address anywhere
  a `{ space, offset }` mapping is expected — in a project-file fact as well as
  in a dump artifact — and reverse-map it, minting a name when no space covers
  it (NFR4).
- Apply `launch` to configure the run when the corresponding command-line flag
  is absent; a supplied flag overrides.
- **Resolve the launch target by looking `launch.exe` up in `files` by path**,
  comparing case-insensitively with separators normalized, and read the exe hash
  for a `bindings` key from that entry. No `files` entry states a role, so this
  lookup is the only way the model knows which file is the program. A project
  with no `launch` has no launch target, which is normal (§Launch and
  reproducibility).
- Represent structs, function names and signatures, per-address `assertions`,
  offset pins, globals with their derived data extents, labels, comments,
  branch targets,
  launch settings, recorded `files`, address spaces and bindings in the model.
- Carry each entity's own `comment` on that entity, and `comments` entries as
  address-keyed text. Reject a `kind` on a comment (§Comments).
- Never synthesize a `name` for a fact that has none. A `globals` entry with no
  `name` stays nameless in the model and in the write-back; any identifier a
  consumer needs is derived at generation time and not stored.
- Key `assertions` by `(address, location, when)`, defaulting `when` to `pre`, so
  the two sides of one instruction are two distinct facts.
- Parse a `location` against the closed set in §Locations, and reject a
  bp-relative stack-slot spelling. Two locations that overlap physically are two
  distinct keys, so a `dx` fact and a `dl` fact never merge and never conflict.
- Index `offsetPins` by the physical address of the pinned constant, so a
  consumer holding a decoded instruction field can look one up directly.
- Represent `segmentDefaults` so a consumer holding an instruction address and an
  effective segment register gets the resolution space in one lookup, treating an
  entry with no `start`/`end` as covering the whole space.
- **Expose one space-resolution query** for a data reference, applying the fixed
  order pin → assertion → observed → segment default (§Segment defaults). R2 and
  R3 both consume this, so the order lives in the model and not in either
  consumer. Report a static fact that disagrees with an observed value
  (`observed-space-disagrees`); resolving to nothing is normal and leaves the
  access raw.
- **Derive each data fact's byte extent from its type** — `sizeof(type)` bytes
  from a `globals` address — and feed those extents into a second poison set that
  the explorer unions with learned poison, and never write that set to the
  `CfgReload` artifact. A type with no static size contributes no extent, which is
  normal and reported as nothing (§Data extents). Extents may overlap; the union
  is the answer.
- Keep `targets` destinations **unresolved** in the model, as `(space, offset)`,
  and resolve them when the seed fires rather than at load (§Branch targets). Look
  a source up by reverse-mapping an observed `SegmentedAddress` back to
  `(space, offset)` — the mapping NFR4 already needs for numeric dumps.
- Seed imported branch targets into the speculative explorer when a node is first
  created at the source address: untrusted items, no continuation wiring, and
  never adding to the poison set.
- Load the graph artifact when `graph:` is present and its `bindingKey` matches
  or is absent; import producer blocks per §Importing a producer's blocks. Resolve
  a relative `graph:` path against the project file's directory, not the current
  directory (§Graph artifact).
- Leave a fact whose `space` has no binding **unresolved and intact**, count it in
  the report, and write it back unchanged (NFR5). An unbound space is never an
  error. Report a space that no `bindings` entry mentions under any key with the
  advice to rebind; report one bound under a different launch key at information
  level, because that is the expected state for the driver that did not load
  (§Merging several producer databases).
- Never silently replace a stored `bindings` number: a number reaches `bindings`
  only through a rebind, since a minted space resolves from the launch
  configuration instead. Report a bound space that holds facts and saw no observed
  activity as possibly stale (§Address space binding at run time).
- **Rebind a minted space onto a declared one** on request
  (`--BindSpace <declared>=<minted>`): move the number, re-key every fact learned
  under the minted name, re-key the graph artifact with it, drop the minted space,
  and write back.
- **Mint a name for an observed segment no space covers**: dedupe by resolved
  number first, so an observed number equal to a known space reuses that name;
  otherwise take the kind from the reserved device-base table — an exact match
  mints `absolute`, everything else mints entry-relative as `seg_<DELTA>`
  (§Minting and rebinding). One space per distinct observed segment, never folded
  into a space it merely falls inside.
- **Write the project file back.** Mint names for discovered address spaces, append
  a `bindings`
  entry for a rebind, append newly observed `files`, move a promoted label from
  `labels` to `functions` (§Labels), and rewrite the project
  file to an explicitly supplied output path. Everything written is name-keyed,
  so a numeric artifact read in is upgraded by the next dump.

### Validation and reporting

Absence is never reported as a problem. An inconsistency is reported as a
**warning**, and its consequence is always the same: the entry is not applied
to any consumer, it is preserved verbatim in the model, and the next dump
writes it back unchanged — the same treatment a fact with an unbound space
gets. An inconsistency never blocks the load and never deletes a fact; the
warning tells the user what to repair, and repairing the file is theirs to do.
One carve-out: a **reserved spelling used as a key** (`role`, `kind`, `source`,
…) is warned about and dropped from the write-back, because the writer emits
only Spice86's own format and must never re-emit a spelling the format rejects.
The entry that carried the key keeps its accepted keys and is otherwise
preserved.

Report **inconsistency**:

- an address whose `space` is not defined
- an address space with none of `entryDelta` / `absolute` / `dynamic`, or more
  than one
- two `addressSpaces` entries with the same name. This check is what catches a
  merge of several producer databases that did not rename a colliding space
  (§Merging several producer databases).
- a space using a reserved device name (`vga`, `bios`, …) at a different number
  than that region's, or using a minted spelling (`seg_<DELTA>`) that its own
  number contradicts. Both spellings are self-describing, so both are checkable
  (§Minting and rebinding).
- an `entryDelta` that is not a valid signed number
- overlapping struct fields, or — when `size` is present — a field extending past
  it. An absent `size` is normal (§Types) and checks nothing here.
- a type string that does not parse (§Types grammar) — including `@` on a far
  pointer, `code` outside a pointee position, a non-literal `array` count, or a
  reserved spelling (`enum`, `union`, `stream`, …) — or one naming an undefined
  struct or address space
- an `array<T, N>` whose element has no fixed size: a struct with no `size`, or a
  bare `cstring`. That shape is a `stream` (§Deferred), and a converter owes
  warn-and-drop rather than emitting an array that cannot stride.
- a `repeat` whose `count` does not name a field of the same struct, names one at
  a higher offset, or names one whose type is not an unsigned integer. The count
  must be readable before the repetition it sizes.
- a `repeat` on an element type with no fixed size. `sizeof(T)` is what both the
  stride and the `bytes` unit are computed from, so without it there is nothing
  to repeat by — that shape is a `stream` (§Deferred).
- a `launch.exe` matching no `files` entry. Paths compare case-insensitively with
  separators normalized (§Launch and reproducibility). An absent `launch` is
  normal and checks nothing here
- a `hash` — in `files` or in a `bindings` key — not spelled
  `sha256:<64 lowercase hex>`. Another algorithm prefix is rejected rather than
  compared, because an unequal comparison would read as a changed file (§Launch
  and reproducibility). An absent `hash` is normal and checks nothing here.
- a graph artifact whose `bindingKey` does not match the current run
- a recorded run whose actual entry point or file hash disagrees with `launch` /
  `files`
- a numeric address that cannot be reverse-mapped because it does not spell a
  valid `segment:offset` scalar (§Address spaces). A well-formed numeric address
  is accepted anywhere, in fact sections and dumps alike, and is reported as
  information only (NFR4)
- a `targets` entry whose source or a destination names an undefined space, or
  whose `to` list is empty
- an offset pin whose stated pointer width disagrees with the decoded length of
  the field at that address
- a `segmentDefaults` entry whose `space` or `resolves` names an undefined space,
  whose `reg` is `cs` or is not a segment register, whose `end` is not
  above its `start`, or that overlaps another entry with the same `space` and
  `reg`. Overlap is an inconsistency rather than a last-wins rule: two defaults
  for one register at one address make every access there resolve two ways
  (§Segment defaults)
- an assertion whose `when` is neither `pre` nor `post`, or a `when` on an offset
  pin
- a `location` naming no accepted register or flag, including a bp-relative
  stack-slot spelling. That spelling is reserved and does not parse in this pass
  (§Locations). A location that is accepted but is no address base — a flag, an
  8-bit half, a segment register — checks nothing here: the loader carries it,
  and R3 declines it at generation time as `location-not-an-address-base` (NFR5)
- two assertions for the same `(address, location, when)` with different types.
  Two assertions on overlapping locations (`dx` and `dl`) are different keys and
  check nothing here (§Locations)
- a `kind` on a `comments` entry, a `role` on a `files` entry, or any other
  reserved spelling used as a key. A `role` is rejected rather than ignored,
  because it would restate the launch target that `launch.exe` already names
  (§Launch and reproducibility)
- a `functions` entry with no `name`. The name is the entry's reason to exist;
  the address alone is a partition-root hint that seeds nothing (§Functions, and
  the code marker that is not one)
- a `globals` entry with no `type`. The type is the entry's reason to exist; an
  absent `name` is normal and checks nothing (§Required fields per section)
- a producer block whose decoded instruction length disagrees with the stated
  one

Report as **information**, not warning: a missing graph artifact, a `dynamic`
space with no binding yet, a `dynamic` space bound under a different launch key
and therefore unbound here, blocks skipped for that reason, a missing `launch`,
a missing `bindingKey`, a numeric address on read — in a fact section or a dump
artifact — an absent section,
a branch target skipped because its space has no binding yet or because the bytes
there do not decode, a name minted for an observed segment, a segment default
whose `resolves` space has no binding yet, a segment default contradicted by an
observed segment value, and any fact the current consumers declined to use
(NFR5).

Checks that require a populated graph — an imported function address that is
not a block entry, or not in the graph at all, or an offset pin whose address is
not the start of a decoded instruction field — run at snapshot/generation time,
**not** at import. At import time on a fresh conversion nothing is in the graph
yet, so running them there would fire on every entry in the file.

### Acceptance criteria

- Given a project file, the loader produces the model with every fact attached
  to a `SegmentedAddress`.
- A converter output with no graph data, no binding and no launch loads, runs,
  and dumps a completed project file with no warnings.
- A project written by Spice86 and loaded again yields an equal model, and a
  loaded converter output written and loaded again does too (NFR3).
- A converted COM project loads and runs, with `launch.exe` naming a `.COM` path
  and its `files` entry stating a path and a hash only. A converted driver
  database, holding one `files` entry and no `launch`, loads with no warning, and
  `--Exe` on the command line supplies the launch target. A `files` entry carrying
  a `role` key is reported as an inconsistency.
- A run that cannot bind a space dumps that space's facts unchanged, and the
  report names the space, the minted space observed instead, and the waiting
  count.
- A project merged from three producer databases, each declaring a space named
  `seg001`, loads with no duplicate-name inconsistency. After the two alternative
  driver spaces are bound under their own launch keys, a run reports only the
  driver that did not load, at information level (§Merging several producer
  databases).
- Two runs of the same configuration mint the same name for the same discovered
  space. An observed segment equal to a declared space's resolved number reuses
  that name and mints nothing; an observed segment one paragraph away from it
  mints its own space, and neither space's offsets are rebased.
- After a rebind, the facts of the formerly unbound space resolve and apply, and
  the graph artifact still loads.
- A dump folder produced by the current numeric format loads with no user
  action and no warning, and the next dump writes Spice86-owned artifacts back
  name-keyed.
- The Ghidra symbol file is still parsed by Ghidra after the change.
- A global of type `array<IntroPart, 48>` at `seg000:0337` pre-poisons exactly
  `0x337..0x577`, and the byte at `0x577` is untouched. Lowering the count to `24`
  and running again leaves `0x457..0x577` unpoisoned, even though the earlier run
  poisoned it. A `cstring` global pre-poisons nothing and reports nothing. Two
  overlapping globals poison their union, with no diagnostic.
- Inconsistent entries are surfaced; absent ones are not.
- The parser and model have no dependency on the UI project.

### Scope notes

- In scope: launch settings, recorded `files`, address spaces and bindings,
  minting and rebinding a discovered space,
  structs, function names and signatures, per-address assertions, offset pins,
  segment defaults and the space-resolution query over them,
  globals and the data extents derived from their types, labels, entity and
  address-keyed comments, branch targets,
  graph artifact reference and producer block import, write-back.
- Out of scope: interrupt `.dict` files; persisting bulk per-instruction runtime
  samples; merging several producer databases, which is a converter duty
  (§Merging several producer databases). R1 only checks the result, through the
  duplicate-space-name rule.

---

## R2 — Name functions and generate memory-based data structures

### Goal

Use the R1 model so the generated override project (a) uses meaningful function
names, and (b) exposes named, typed accessors for structs and typed globals
instead of raw memory indexing.

### Functional requirements

- **Loaded functions supply the generated method names.** They become
  `FunctionInformation` entries in
  the `FunctionCatalogue`, so generated method names and dumped symbols use the
  loaded name instead of an `unknown_XXXX` placeholder.
- **A promoted label reaches the same channel.** A `labels` entry whose address the
  partitioner rooted a method at supplies that method's name (§Labels).
  A `functions` entry at the same address wins, so promotion only ever fills a name
  that would otherwise be `unknown_XXXX`. **The promotion is recorded in the
  model**, so the next dump writes that entry under `functions`, carrying its
  `name` and `comment`, and drops it from `labels`. A label the partitioner did not
  root a method at supplies no name, stays in `labels`, and is not reported as
  declined — that is the normal state for a data label. The promotion count goes in
  the report (NFR5).
- **Struct accessors come from the `structs` section.** Each struct becomes a
  generated memory-based
  data-structure class (the `MemoryBasedDataStructure` pattern) with named
  getters/setters at the correct field offsets. Holes are skipped. `size` bounds
  the class when present; when absent each field accessor bounds itself.
  Terminator scanning is deferred: no scanning infrastructure exists yet, so a
  `cstring` accessor is not generated in this pass — the field is skipped and
  reported (`terminator-scan-deferred`, NFR5). When scanning lands it runs
  under a fixed cap, so a string that is never terminated cannot walk the
  whole segment.
- **A repeated field generates two accessors.** A `repeat` field gets a count
  accessor and an indexed element accessor — `Slots_Count(memory, base)`,
  reading the `count` field and dividing by `sizeof(T)` when `unit` is `bytes`,
  and `Slots(memory, base, i)` at `base + offset + i * sizeof(T)`. The count is
  read from emulated memory, so it is **untrusted**: the indexed accessor
  range-checks `i` against the count it just read and refuses an index at or
  past it, so a garbage header cannot turn a typed read into a read past the
  repetition. There is no address-space extent to clamp against, and none is
  needed. An offset is 16 bits wide, `RealModeMmu8086` wraps inside the segment,
  and `A20Gate` masks the linear address, so the count alone is what the
  accessor has to check. A `bytes` length that is not a multiple of `sizeof(T)`
  truncates and is reported; it is not an error, because the program itself may
  be mid-write.
- **Each typed global becomes a named accessor** in the space it
  belongs to (the `GlobalsOn<Space>` pattern). The generated class takes its
  segment value at construction (base = segment << 4), supplied by the caller
  — typically captured from a segment register at function entry. DS is not
  constant in hand-written assembly, so the class is neither hardwired to the
  space's bound segment nor tied to a live register. A global with no `name` gets an
  identifier derived from its address — `data_<OFFSET>` in uppercase hex, or
  `data_<space>_<OFFSET>` when accessors from several spaces share a class — so it
  is stable across runs without being persisted. `unknown_XXXX` is not reused
  here: it is the function-name placeholder, and sharing the spelling would hide
  whether a name failed to load or never existed. An `array<uint8, N>` global
  asserts an extent and no layout (§Data extents), so it gets one indexed byte
  accessor whatever `N` is — never `N` members — and nothing else.
- **A comment has two possible slots**, chosen by where the comment is stored
  and not by a field on it (§Comments). `functions[].comment` becomes the
  method's doc comment; everything else — a `comments` entry, and an entity
  comment whose address the partitioner did not root a method at — becomes
  comment lines above the instruction's disassembly line, one line per line of
  text. A loaded `signature` is emitted as a comment at the function's entry
  address, not as method metadata, so it stays correct when the partitioner
  disagrees about boundaries. The comment prints each param's `location` as
  written, and R2 renames no register, so an 8-bit half binding needs no C#
  identifier (§Locations).
- **R2 resolves an offset at the emit site.** For a decoded instruction field it
  asks the R1 space-resolution query for that field — an offset pin at its physical
  address first, then, for a ModRM or absolute displacement, the segment default
  for the operand's **effective** segment register: the prefix when the
  instruction carries one, SS for a BP-based operand, DS otherwise. When the
  offset resolves to a known function, global or label in that space, R2 emits
  the symbol name as a comment line on the instruction. The emitted value stays
  the faithful constant — resolution says what a number means, it never changes
  the number.
  A default is what makes this fire on a first run: an immediate needs a pin,
  but a displacement is resolved by the enclosing default alone, with nothing
  observed and no per-instruction annotation (§Segment defaults).
- **A type the accessor model cannot express is skipped, not guessed.** A
  variable-length field with fields *after* it has offsets that are not static, so
  it is skipped with a clear message
  (`layout-not-expressible`, NFR5), never emitted at a wrong offset. A
  variable-length field that is **last** keeps the struct expressible: every
  other offset stays static, so the struct and its fixed fields are still
  generated — they are what manual refactoring of the generated code works
  with until SSA — and only the last field's scanning accessor is deferred
  (`terminator-scan-deferred`).

### Acceptance criteria

- The generated project uses loaded function names where an address matches.
- A `labels` entry at an address the partitioner rooted a method at names that
  method, and the next dump carries it under `functions` with its `name` and
  `comment`, and no longer under `labels`. Loading that dump and running the same
  configuration again promotes nothing and yields an equal model, so the reported
  promotion count is zero on the second run.
- A `labels` entry at an address the partitioner did not root a method at names
  nothing, produces no warning, is not counted as declined, and is still a `labels`
  entry in the next dump. A label in a data space never promotes.
- A promoted entry is never demoted: a later run whose partitioner roots no method
  at that address leaves the `functions` entry in place and reports it as a declined
  fact, not as an inconsistency.
- A named attr carrying a `fn` and no `type` becomes a `functions` entry with that
  signature, not a `labels` entry.
- Generated accessor classes compile; field offsets match the file layout; a
  written value reads back through the accessor.
- A struct with no `size` whose last field is variable-length generates its
  fixed fields at their stated offsets; the last field's terminator-scanning
  accessor is deferred and reported (`terminator-scan-deferred`). It is
  rejected as an `array` element. A struct with a variable-length field that
  is *not* last is declined and reported, not emitted.
- A `repeat` field generates both accessors: with a count of N written into the
  count field, element `N-1` reads back and element `N` is refused rather than
  reading past the repetition.
- Name sanitization and address collisions are handled without producing
  duplicate or invalid C# identifiers.
- A `functions[].comment` at an address the partitioner rooted a method at
  becomes that method's doc comment; the same comment at an address it did not
  becomes a comment line there instead. A `comments` entry, single- or
  multi-line, becomes comment lines at its address. The generated file compiles
  with a comment text containing quotes, a `*/`, or a newline.
- An offset pin whose offset matches a named symbol in the pinned space produces
  a comment naming it, and the generated constant is unchanged. A pin on a field
  whose `UseValue` is false produces nothing.
- With a `ds` segment default over the code range and no pin and no observed
  segment value, `mov ax, [0x25c]` names the global at `0x25c` in the resolved
  space, and the emitted access is byte-for-byte what it was without the default.
  A `[bp+4]` operand in the same range resolves through the `ss` default, not the
  `ds` one. Outside every default's range the access stays unnamed.
- A global with no `name` generates an accessor named from its address, reads and
  writes at the right offset, and the next dump still carries no `name` on it.

### Non-goals

- Substituting typed parameters into generated method bodies (R3).
- Honouring `signature.convention` in generated code. Phase 1 emits it as a
  comment only.

---

## R3 — Typed field access in generated code

### Goal

Where it can be proven, name the field a memory access touches — e.g.
`mov AX, DS:[SI+4]` with `SI : nearptr16<Troop>` gets a comment naming
`Troop.Count` above the faithful raw `UInt16[ds, si + 4]`. Rewriting the
access itself to a typed accessor call is deferred until SSA lands: pre-SSA,
R3 proves the fact and surfaces it as a comment, and the emitted statement is
always the faithful raw access. Where nothing can be proven, no comment is
emitted.

### Functional requirements

- **A type fact reaches its use points.** From a function-entry `in` param or an
  `assertions` entry, R3 reaches the instruction that uses the value. Pre-SSA
  this is a **backward scan inside one basic block**, not a dataflow: from the
  use site, walk back to the nearest assertion for that location, stopping at
  any write to it. A write to any **overlapping** location stops the scan too,
  so `mov dl, 0` stops a scan for `dx`. An assertion on a merely overlapping
  location does not answer the scan, so a `dx` assertion never types `dl`
  (§Locations). `when` decides only whether an assertion on the scan's own
  starting instruction counts (`pre` yes, `post` no). No fixpoint, no join,
  nothing that crosses a block boundary.
- **A memory operand gets a typed-access comment only when every condition
  below holds**, and it stays uncommented otherwise:
  - the operand's offset expression is exactly one register plus at most one
    constant term — two runtime registers (`[bx+si+4]`) means the displacement is
    not a compile-time constant, so it cannot be proven;
  - a type for that register is known at this use site by the scan above;
  - the instruction's own execution AST does not assign that register anywhere.
    This is what makes `mov si, ds:[si]`, `xchg`, and the string operations
    decline rather than guess: their bodies are several statements and the
    register's value changes between them;
  - the displacement lands exactly on a field boundary of the known layout, at a
    matching width.
- **An operand with no register term (`[0x25c]`) is commented on its own.** It needs its
  space resolved through the R1 query (§Segment defaults), and the offset must land
  exactly on a typed global, or on a field boundary of a layout stated
  there, at a matching width. The offset is a compile-time constant already, so
  the only missing piece is the space — which a segment default supplies on a
  first run and SQ1's observed value supplies for what ran. This case needs no
  backward scan and no block: a default is range-scoped, so it costs one lookup
  at the operand.
- **The rewrite itself waits for SSA.** Pre-SSA there is no value identity
  across statements, so a rewritten access would either allocate a wrapper
  object per access in the emulator's hot path (`new Troop(memory, addr).Count`
  costs a heap object every execution) or call static plumbing
  (`Troop.Count(memory, baseAddress)`) that reads worse than the raw access it
  replaces and is a dead end for later refactoring. Neither output is worth
  keeping, so this pass rewrites nothing. When SSA lands, lowering emits R2's
  instance accessor classes (`troop.Count`) with wrapper construction hoisted
  to where the base value is defined — the shape a human or an AI continuing
  the rewrite would produce — which is both readable and allocation-free per
  access. R2's accessor classes stay as they are for hand-written use.
- **Comment emission is always on and non-destructive.** It is the default and
  cannot be disabled. The emitted statement is always the existing faithful raw
  access, so the faithful guarantee holds everywhere.

### Acceptance criteria

- Given a typed pointer binding, a constant-displacement access that lands on a
  field is commented with that field; accesses that are not provable (unknown
  base type, non-constant or indexed displacement, off-field offset) get no
  comment.
- `[si+4]` with a typed SI is commented; the same access inside an instruction
  whose body writes SI declines and gets no comment.
- `mov ax, [0x25c]` is commented with the typed global at `0x25c` when a `ds`
  segment default covers the instruction and that global exists; it gets no
  comment when no default covers it, when the resolved space has no symbol at
  that offset, or when the offset is off the boundary or the width disagrees.
- A `pre` and a `post` assertion at the same address for the same location are
  both loaded, and each reaches only its own side's use sites.
- An assertion on `dx` does not type a later use of `dl`, and a `mov dl, 0`
  between an assertion on `dx` and a `[bx]` access through DX stops the scan. An
  assertion whose location is an 8-bit half is loaded, round-tripped, and
  reported as `location-not-an-address-base`.
- Comment emission changes no executable statement: the generated output is
  the pure-faithful translation plus comment lines, and it compiles and behaves
  identically for the observed paths.
- No configuration path produces a build with typed-access comments switched
  off; the only variation is per-access.

### Non-goals (this pass)

- Rewriting accesses to typed accessor calls. The rewrite is deferred until
  SSA: hoisting wrapper construction out of the access needs value identity
  across statements, and that is what makes the rewrite both readable and
  allocation-free.
- SSA and named local recovery (`ushort count = troop.Count`).
- Loop structuring (gotos → `for`/`while`).
- Alias analysis and whole-program type inference.
- Any change that could make generated code diverge from observed behavior.
- Reaching use sites outside the asserting basic block. Those decline, and the
  decline is reported (NFR5).

### What the coming SSA work must preserve

The block-local scan above is written to be **deleted**, not extended: under SSA
a use site's type is the type of the value it references, and the two `when`
spellings become a clamp on a value's live-in and a clamp on a definition. Two
invariants keep the imported facts usable across that change, and both are
cheaper to honour while SSA is being designed than to retrofit:

- **A constant keeps its provenance.** An SSA value that came from a decoded
  instruction field carries that field, because the field's physical address is
  the key an offset pin is looked up by (§Offset pins). When constant folding
  merges two constants it **drops** provenance rather than picking one input:
  dropping disables a pin, which is fail-safe, while picking produces a
  confidently wrong symbol name.
- **Address-keyed facts stay translatable.** `(address, pre)` is the values
  live-in to that instruction and `(address, post)` its definitions, so the
  format needs no change when SSA lands — only the consumer does.

---

## Side quests

Enabling or adjacent work R1–R3 lean on that is not itself one of the three
requirements. None of these block R1.

### SQ1 — Record observed segment-register values per memory access

**This is worth doing because the static half alone leaves a gap.** That static
half is in the format (§Segment defaults), and it covers a first run and code that
never ran. The dynamic half is not captured at
all: today the CFG does not record the segment-register value used at each
instruction, so an access no default covers cannot be attributed to a concrete
space, and one a default does cover cannot be checked against what actually ran.
Both matter — a producer's default is a static claim over a range, so a register
whose value genuinely varies is exactly where it is wrong.

**SQ1 captures, per memory-accessing node, the effective segment value observed
for the access**, and ideally also which register supplied it. This lets an
absolute `DS:[disp]` access resolve to `(space, offset)` and from there to a
loaded `global`.

**The capture enables four things.** It attributes an absolute access no
`segmentDefaults` entry covers, in R2 and R3 alike, through the same resolution
query and one rung above the default (§Segment defaults). It lets R2 group globals
into `GlobalsOn<Space>` by observed space. It resolves concrete targets of a
dynamic near code pointer, which is where vtable and jump-table destinations land.
It supplies the disagreement check that turns a wrong or over-extended default into
a report line instead of a wrong name.

**This has a known limit.** The cheapest version records one segment value per
node, on first observation. Some instructions have an effective segment that
legitimately varies across runs, such as self-modifying code or code reused with
a different DS. Those need a *set*, not a scalar. Take that upgrade only when
the case actually appears, and note the scalar limit in a code comment at the
recording site.

### SQ2 — Persist observed runtime samples

Bulk per-instruction register/memory captures stay in the in-memory CFG. What is
worth writing back, and at what granularity given file-size cost, is its own
task.

### SQ3 — Import interrupt dictionaries

chani keeps interrupt annotations in a separate `.dict` file. Folding those into
the model is a follow-up.

