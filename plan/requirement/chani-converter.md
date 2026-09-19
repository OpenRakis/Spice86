# chani to Spice86 — converter specification

Part of the [Spice86 project file design](README.md). Read
[Purpose and Shared context](README.md) first, then
[project-file-format.md](project-file-format.md), which defines every
destination named here.

The converter is a standalone C# program. It lives outside Spice86 and imports
Spice86 for x86 decoding. It reads one or more `.chani` databases and the
binaries those databases name, and it writes one project file. Spice86 never
reads a `.chani` file.

The converter lives in **its own repository**, not in the Spice86 solution.
What it shares with Spice86 — the project-file model, serializer and validator,
the type grammar, and the x86 decoder — it takes as a library dependency from
the Spice86 repository, which is where that shared code is developed. The
converter contributes nothing back to that library beyond being its second
consumer, which is what keeps the format model producer-neutral (NFR1).

## Merging several producer databases

One program can be split across several producer databases, and one Spice86
project must hold all of them. Cryo Dune is the case. `dncdprg` is the exe,
`dnadl` is the AdLib driver, `dnsdb` is the SoundBlaster driver, and the program
loads one driver per run. A converter that emitted only the exe would drop every
fact about both drivers.

The merge happens **in the converter**, once, before Spice86 reads anything. It
is not the deferred `include` overlay. That entry is about re-importing a producer
database over a project Spice86 already complemented (§Deferred), which is a
different problem.

**A converter takes one primary module and any number of secondary modules.**

```
chani2spice86 --primary dncdprg.chani --module dnadl.chani --module dnsdb.chani \
              -o cryo-dune.spice86.yaml
```

The primary is the module whose file `launch.exe` names. Three rules follow:

- **`launch` comes from the primary alone.** A secondary module states no launch
  inputs, so it contributes none.
- **Every module's file becomes a plain `files` entry.** An entry states a path
  and a hash, so a merge of three modules declaring `format = exe` still produces
  one launch target and needs no loader check (§Launch and reproducibility).
- **`project:` comes from the primary's project name.** A secondary module's
  project name is not dropped. It becomes the space-name prefix below.

### The name prefix

All three Dune databases declare a space named `seg001`, and those three spaces
are different. Names are the keys of every fact, so the collision is resolved
before the file is written.

**Rename every space of a secondary module to `<module>_<space>`.** The primary
module's spaces keep the names the producer gave them. `<module>` is the
secondary's project name, lowercased, with every character outside `[a-z0-9_]`
replaced by `_`.

```yaml
addressSpaces:
  - { name: seg000, entryDelta: 0x0 }      # dncdprg, the primary
  - { name: seg001, entryDelta: 0xF4B }    # dncdprg, the primary
  - { name: segvga, dynamic: true }
  - { name: dnadl_seg001, dynamic: true }  # AdLib driver, no number yet
  - { name: dnsdb_seg001, dynamic: true }  # SoundBlaster driver, no number yet
```

**The prefix is unconditional for a secondary module, and a collision does not
trigger it.** A collision-triggered rename would make a name depend on which
other databases were in the merge. A name is a persisted key, so adding a fourth
module later would re-key facts in the three already converted. This rule depends
on the module alone. Converting `dncdprg` alone and converting all three give the
same name for every `dncdprg` space.

The separator is an underscore because `identifier` already accepts one (§Types
grammar), so `nearptr16@dnadl_seg001` parses with no grammar change. A dot would
need a grammar change and would read like field access.

**The rename reaches every place a space name appears.** The last two are easy to
miss:

- `address.space` on `functions`, `globals`, `assertions`, `offsetPins`,
  `labels`, `comments` and `targets`, plus every `targets` destination.
- `space` and `resolves` in `segmentDefaults`.
- the `@<space>` pin inside a type string. The converter therefore rewrites type
  text and not only YAML keys.

**Struct names take the same prefix**, because a struct name is also a key in the
type grammar. Two modules can each define a `Header` with a different layout.
Function, global and label names need no prefix: their key is the address, and R2
already resolves C# identifier collisions.

**The loader catches a bad merge.** Two `addressSpaces` entries with the same name
is already an inconsistency (§R1 validation), so an unrenamed merge is reported
rather than loaded.

### A driver space with no number

A driver is loaded through DOS into an allocated block, so its space is `dynamic`
and the converter has no number for it. The format already carries that shape. A
`dynamic` space with no `bindings` entry is a name every fact keys on, with no
address yet.

The Dune flow needs no new mechanism:

1. The first run with AdLib binds neither driver. Spice86 mints a name for the
   segment it observes. The report names both declared driver spaces with their
   waiting fact counts (NFR5).
2. The user rebinds: `--BindSpace dnadl_seg001=seg_31A0`. The number lands in the
   `bindings` entry for that launch key (§Minting and rebinding).
3. A run with SoundBlaster has a different `exeArgs`, so it is a different binding
   key. The user rebinds `dnsdb_seg001` under that key.

After both rebinds, each configuration binds its own driver. The other driver's
facts are written back unresolved and unchanged (NFR5). The two drivers having
different sizes is already why `exeArgs` is part of the binding key (§Address
spaces).

**Mutual exclusion needs no new field.** `bindings` is keyed by launch
configuration, so the map under one key holds the driver that loads and omits the
other. That is where the exclusion is stated (§Rejected alternatives).

**The report splits the unbound case in two**, so a correct run stays quiet:

- a `dynamic` space that no `bindings` entry mentions under any key is **never
  bound**. Report it with its waiting fact count and the advice to rebind.
- a `dynamic` space bound under a **different** key is not in this configuration.
  That is expected, so report it at information level with no advice.

Without the split, every correct run reports one unbound driver forever. The user
then learns to ignore a line that also reports a real failure.

## Where a producer's annotations land

Each format section defines one destination. The three tables below are the
other direction, and they are the required reference for a converter. A shape or
a key with no row is a converter bug, not prose to re-read.

A converter answers two different questions per attr. One table cannot state
both, so there are three. Table A picks the entity an attr becomes, from its
`name` and its **effective type**. Table B maps every remaining attr key. Those
keys apply on top of whatever Table A produced, so one attr matches one Table A
row and any number of Table B rows. Table C covers the annotations that sit above
attr level. Each table is complete over its own domain, so each claim is checkable
on its own.

### Effective type

Table A reads an **effective type**, not the `type` key alone:

> The effective type is `code` when the attr states `type = code` **or** carries a
> `fn`. Otherwise it is the stated type, or absent when the attr states none.

The `fn` clause is there because a signature binds parameters to registers at a
call boundary, so a producer writes one only at a function entry. That makes `fn`
evidence of code on its own, and in the Dune databases 56 named attrs supply it
while stating no `type` (§Functions). Folding the clause into the effective type
keeps Table A two-dimensional and keeps its six rows complete.

This is the **only** place a Table B key reaches the entity decision. Every other
key applies on top of the entity Table A picked.

### Table A — the entity an attr becomes

Keyed by `name` and effective type, which is the only pair that decides the
entity. The six rows are every combination chani can express, so exactly one row
applies to every attr.

| `name`  | effective type | destination                | rule                                               |
|---------|----------------|----------------------------|----------------------------------------------------|
| present | `code`         | `functions`                | name + partition-root hint; `fn` fills `signature` (Table B) |
| absent  | `code`         | dropped, counted           | §Functions — only "is code" remains                |
| present | data type      | `globals`                  | named and typed; `sizeof(type)` is the data extent |
| absent  | data type      | `globals` with no `name`   | the type is the fact, extent included              |
| present | absent         | `labels`                   | named, no layout asserted. Names a method and is rewritten as a `functions` entry when the partitioner roots one there (§Labels) |
| absent  | absent         | no entity of its own       | the attr still carries its Table B keys            |

The fifth row is where a producer's uncertainty lands, and it is not a small
residue. 316 names in the Dune databases name code and state no `type`, against 891
that state `type = code`. The effective-type rule routes 56 of them to `functions`
on the strength of their `fn`, and the other 260 become labels. Promotion is what
turns those 260 into method names without asking the converter to decode anything
(§Labels).

### Table B — the other attr keys

chani accepts ten keys on an attr. `name` and `type` are Table A; the eight
below are every one that remains. A key whose destination depends on Table A
says so in its rule.

| attr key (chani)      | destination                                          | rule                                                                                      |
|-----------------------|------------------------------------------------------|-------------------------------------------------------------------------------------------|
| `fn`                  | the `functions` entry's `signature`, or `assertions` | the `signature` whenever the attr has a `name`, since a `fn` makes the effective type `code` and Table A then makes a `functions` entry. On a nameless attr, `in`/`inout` become assertions with `when: pre` and `out` is dropped (§Functions, and the code marker that is not one) |
| `comment`             | that entity's `comment`, or a `comments` entry       | on the entity when Table A made one; otherwise address-keyed text (§Comments)              |
| `ofs_seg` on a code address | `offsetPins`, keyed by the constant's bytes    | §Offset pins                                                                              |
| `ofs_seg` on a data address | the `type` of that global or field             | §Offset pins, third rider                                                                 |
| `targets`             | `targets`                                            | emitted as stored. Warn when the key is not an instruction start, which needs the binaries and is advisory: such a key never fires (§Branch target keys) |
| `let`                 | `assertions`, `when: pre`                            | a type binding with no direction (§When an assertion holds)                                |
| `assume`              | dropped, and counted when it differs                 | a segment-register binding, not a type. One equal to the space default states nothing new; one that differs needs a range end the converter cannot compute (§Segment default scope) |
| `arg[0]` / `arg[1]`   | dropped, counted                                     | a display format for one operand, and display is not a type fact — the same rule the `hex()`/`dec()`/`bin()`/`char()` type wrappers get (§The type mapping) |

### Table C — annotations above attr level

chani carries annotations at three scopes outside an attr: the project, a file,
and a segment, plus a struct block. These rows are every key at those scopes.

| annotation (chani)                      | destination                              | rule                                                                                      |
|-----------------------------------------|------------------------------------------|-------------------------------------------------------------------------------------------|
| `project[<name>]`                       | `project:`, or the space-name prefix     | the project name for the primary module; for a secondary module it prefixes that module's space and struct names (§Merging several producer databases) |
| `arch = 8086`                           | dropped                                  | Spice86 already knows it runs x86, so the value adds nothing                               |
| `notes`                                 | dropped, counted                         | free text about the whole project, and the format has no project-level comment field        |
| `file[x]` `path`                        | a `files` entry                          | the entry's `path`, for a primary and a secondary module alike. An entry states no role, and `launch.exe` names the launch target from the converter's own `--primary` choice (§Launch and reproducibility) |
| `file[x]` `format`                      | dropped, counted                         | the container shape, and Spice86 derives that from the path extension instead. `exe`, `com` and `bin` all reach the same `files` entry. A converter warns when the primary's extension is none of `.EXE`, `.COM` or `.BAT`, because Spice86 then loads it as a BIOS image |
| `file[x]` `hash`                        | recomputed as sha256, or omitted         | chani stores sha1, which is not convertible (§The launch target hash)                        |
| `segment` `load = exe[..]`              | `addressSpaces`                          | delta in paragraphs                                                                       |
| `segment` `start` / `end`               | dropped, counted                         | the pair sizes the producer's own annotation store; a fact's extent comes from its type instead, and `addressSpaces` states no size (§Data extents) |
| `segment` `assume`                      | a `segmentDefaults` entry with no `start`/`end` | covers the whole space (§Segment defaults)                                          |
| `segment` `type = code` / `type = data` | dropped, counted                         | a free-form tag, not a byte-level fact; extents come from the typed attrs (§Data extents)   |
| `struct[<name>]` and its fields         | `structs`                                | converter derives the offsets, and reads both of chani's layout spellings (§Struct layout)   |

## Functions, and the code marker that is not one

A `functions` entry carries two facts at one address: a **name** for the naming
channel, and a partition-root hint. A producer can state the second without the
first — chani's attr `type` and `name` are independent fields, so `type = code`
with no name is legal and common: 111 of the 1002 code attrs in the Dune
`dncdprg` database, 107 of them carrying nothing else at all.

**That shape carries nothing this format imports.** Remove the name that is not
there and what remains is "this address is code", which is the widening fact the
narrow/widen rule already refuses (§Deferred, entry-point seeding) — a
`functions` address is explicitly not a boundary and seeds nothing, so the hint
alone changes no behavior. Spice86 learns that an address is code by executing
it. The positions confirm the reading: they cluster right after a named entry
(`0684 play_PRESENT_HNM`, then bare `0686`, `0687`, `068f`), so they are
mid-routine jump-in points the producer needed to lay out instructions, not
functions.

**Converter duty: drop the code marker, keep the rest of the attr.** Dropping the
marker is not dropping the annotation. Every other fact at that address converts
by its own rule and is unaffected: `ofs_seg` becomes an offset pin, `comment` a
`comments` entry (§Comments), `targets` a branch-target entry, `let` an
assertion. The converter reports how many markers it dropped, so the number is
visible rather than inferred from a smaller `functions` list.

**A synthesized name is not the answer.** chani already mints `loc_<linear hex>`
for these while loading and flags them as auto-labels so they are never written
back as user names. A converter that passed one through as `name` would write
exactly the `unknown_XXXX` placeholder R2 exists to replace. Once it is in the
project file it is indistinguishable from a name a user chose, and every later
round trip writes it back — the same leak NFR4 closes for numeric addresses.

**A signature with no name becomes assertions.** The one variant that does carry
content is an unnamed attr with a `fn` (3 in the Dune databases: `seg000:6757`
and `seg000:68eb` carry `type = code`, and `seg000:92c9` carries no `type` at
all). Its `in` and `inout` bindings state exactly what an `assertions` entry
states — this location holds this type on entry — so they convert to assertions at
that address with `when: pre`. The `out` bindings are dropped: an `out` is a
return value, and nothing in R1–R3 consumes one. R3 already treats a
function-entry `in` param and an assertion as the same input (§R3), so no type
fact is lost and `functions[].name` stays mandatory.

**A named attr with a `fn` is a function, whatever its `type` says.** A signature
binds parameters to registers at a call boundary, so a producer only writes one at
a function entry. That makes the `fn` key evidence of code in its own right, and
it is the evidence that decides the entity when `type` is absent. The Dune
databases agree: all 156 `fn` attrs sit in `dncdprg` `seg000`, none appears in
`seg001` or `segvga`, and none states a data type. So the rule costs nothing to
check and it recovers 56 signatures that would otherwise degrade to assertions
(§Where a producer's annotations land, effective type).

**The data shape is not the mirror of this.** A typed data attr with no name
(`attr[seg000:015f]: type = u16`, common) looks like the same case and is not.
Remove the name from a code attr and what remains is "this address is code",
which widens and is refused. Remove it from a typed data attr and what remains
is a type at an address — narrowing, and unrecoverable by running, the same gap
that motivates `assertions`. So it lands: a `globals` entry with a `type` and no
`name` (§Where a producer's annotations land). Dropping it would delete a whole
population of typed data with nobody able to tell, which is exactly the failure
NFR5 exists to make visible.

**Still no synthesized name.** The rule above is unchanged for data: a nameless
global is written back nameless. R2 needs a C# identifier and derives one from
the address at emit time (§R2), which is generated source and not a stored fact.
Writing `unknown_015f` into `name` instead would be indistinguishable from a name
a user chose, and every later round trip would write it back.

## Converter duties

Each duty below sits in the format section it serves. A duty states what the
converter computes, and what it does when it cannot compute it.

### Deriving an address space delta

**Converter duty: derive the delta, in paragraphs.** A producer states the
space's position as a byte offset into the header-stripped load image that
`entryDelta` is measured from (§Address spaces) — chani writes
`load = exe[0xf4b0..0x1336c]` — so the delta is that offset `>> 4`. The
offset must be paragraph-aligned; when it is not, no segment base expresses it,
so the converter warns and drops the space rather than rounding, which would
shift every fact in it by up to 15 bytes. Same duty as an ambiguous offset pin:
resolved where the producer's database is still available.

### The launch target hash

**Converter duty: recompute the hash, or omit it.** A producer may store a
different digest — chani has sha1 only — and that value is not convertible, so
the converter hashes the file itself with sha256 when it has the file, and
**omits `hash`** when it does not. Omitting is safe: absence is never a
diagnostic (NFR2), the binding key then matches on the remaining launch inputs,
and Spice86 fills the hash in on the first run that opens the file. Passing the
producer's sha1 through would be rejected by the loader instead.

### Data extents and segment tags

**Per-annotation coverage is measured against two regions, not one.** In
`dncdprg`, `seg001` is the one segment that is entirely data. Its `load` backs
16060 bytes and its `end` is `0xdd1e`. So 40546 of its 56606 bytes are not in the
EXE image at all. The file-backed part is covered densely: 1124 typed attr starts
over 16060 bytes, with a largest unannotated gap of 1960 bytes. The part past the
image holds 340 typed starts and one unannotated run of 21901 bytes. That run
begins at `RESOURCE_GLOBDATA`, which the program fills at runtime. A single
percentage over the whole segment mixes the two regions and reads low. Most of
what it counts as missing is memory the producer never read and speculation does
not reach (§Deferred, whole-segment data tags).

**Converter duty: derive extents from the producer's typed annotations. Drop a
segment-level tag, whether it reads `code` or `data`.** chani's
`segment[seg000]: type = code` is documented in its own format reference as a
free-form string tag, conventionally `code` or `data`. Both tags are dropped, for
different reasons. The reasons are worth separating, because the code-tag reason
does not carry the data tag.

A `code` tag has no destination. There is no code range in this format, so there
is nothing to convert it into, and reading it as "no data here" would be wrong at
byte granularity in the direction that loses data. In `dncdprg` `seg000` is tagged
`code` and holds 73 data-typed attrs totalling 1564 bytes, `segvga` is tagged
`code` and holds 18 more, and the two sound-driver databases are a single segment
tagged `code` carrying typed data. Honouring the tag would delete every one of
those extents.

A `data` tag does have a destination: one unnamed `array<uint8, N>` global over
the segment extent. It is still dropped, for two reasons that are about the
producer rather than about byte granularity.

- **The tag carries no analysis.** chani stores it as an unconstrained string.
  Its only readers render the IDA-style segment header and decide whether to print
  `assume cs:`. Nothing in chani marks bytes from it, and nothing checks it against
  the attrs, so it states a convention and not a result. `seg001` is tagged `data`
  and holds zero `code` attrs among its 1464 typed ones, but no step in the
  producer connects those two facts.
- **A static producer does not speak for bytes it never read.** Most of `seg001`
  is past the EXE image, so chani annotated nothing there either way. The absence
  of a `code` attr in that range is silence, not corroboration. That range is also
  where a bulk extent buys least, because speculation reaches it only through a
  branch target that lands inside it.

**The segment's `start` / `end` pair is dropped with the tag, and for the same
reason.** The pair is the number that would size that bulk global, so the two
drops are one decision. The pair is also not a claim about the bytes. chani sizes
its own annotation store from it: it computes `end - start` and allocates the
address-attribute array from the result. That allocation is what lets a producer
annotate bytes past the load image, and `dncdprg` `seg001` holds 340 typed starts
there. So the pair states how far annotations may reach, not what the bytes are.
`addressSpaces` therefore carries no size field, and no requirement in R1, R2 or
R3 reads one.

The real data map is per-annotation, and the producer already computes it: for
each attr whose `type` is a data type, the extent is `byte_size` of that type at
that address, floored at one byte — chani's own `mark_data_attributes`. So:

- **Emit the `globals` entry and let the extent follow from its type.** There is
  nothing extra to write. `attr[seg000:0337]: type = [IntroPart; 48]` becomes a
  global of type `array<IntroPart, 48>`, and 576 bytes from `0x337` are data
  because that is what the type measures.
- **Reuse the producer's sizing rather than reimplementing it.** A variable-length
  type is measured from the bytes (`cstr` reads to its terminator), so the
  converter needs the image the `load =` expression names — 254 of `seg001`'s 1464
  data attrs are this shape. Without the bytes the type still converts and the
  extent is simply absent.
- **Unannotated bytes claim nothing.** chani renders them as per-byte `unknown`,
  which is a default and not a statement, so gaps stay unclassified. The format
  can carry a bulk region as one unnamed `array<uint8, N>` global, and no
  conversion emits one in phase 1. The segment tag is the only input that would
  produce it, and that tag is dropped. So a bulk region stays a user-requested
  converter option rather than a derived fact (§Deferred, whole-segment data tags).

### The type mapping

Converter mapping, for chani (the other producers translate the same way):

| chani              | Spice86                             |
|--------------------|-------------------------------------|
| `u16` / `u8` / `u32` | `uint16` / `uint8` / `uint32`     |
| `unknown` (the `db` default) | `uint8`, and a run of them `array<uint8, N>` — data extent, no layout claimed |
| `signed(u16)`      | `int16` — signedness is the type, not a display modifier |
| `hex(T)`, `dec(T)`, `bin(T)`, `char(T)` | dropped; display is not a type fact |
| `str(N)` / `cstr`  | `string(N)` / `cstring`             |
| `ofs16`            | `nearptr16`                         |
| `ofs16(seg001)`    | `nearptr16@seg001`                  |
| `[ofs16(seg001); 3]` | `array<nearptr16@seg001, 3>`      |
| `*Troop` / `**Troop` | `nearptr16<Troop>` / `nearptr16<nearptr16<Troop>>` |
| `(T, U, …)`        | warn and drop the binding, and count it. A binding carries one location, so a tuple cannot say where each member lives; several values are several `params` / `returns` entries, which is structure rather than a type |
| `()`               | nothing. No value is an absent `returns` entry, and absence is never a diagnostic (NFR2) |
| `code` (attr type) | the pointee in `farptr16<code>` / `nearptr16<code>` |
| `assume = ds:seg001` (segment level) | a `segmentDefaults` entry covering the space |
| `assume` on an attr | dropped. Silently when it equals the space default, with a warning when it differs, because a range needs an end the converter cannot compute (§Segment default scope) |

### Struct layout

**Converter duty: derive the offsets, or warn and drop.** A producer may hold a
layout as an ordered field list with no offsets and no total size, position being
the sum of the preceding field sizes — chani does, and a byte-consuming cursor is
enough for it because it always has the bytes in hand. The format wants static
offsets, so the converter is what makes them explicit. Same division as an offset
pin: the ambiguity is resolved where the producer's database is still available,
never encoded into the format.

- **Offset** is the packed cursor over the preceding fields, with no alignment or
  padding inserted. A producer with no alignment model has none to translate, and
  inventing one would move every field after the first odd-sized one.
- **`size`** is that same cursor over all fields, and is emitted whenever every
  field has a fixed size. It has to be: without it the layout is not stridable,
  and `array<Struct, N>` over it is an error rather than a degradation. Note what
  this `size` does *not* carry — derived from the fields, it is never larger than
  their sum, so a converted struct states no padding and no unknown tail. Only a
  producer that knows the record's real extent can say that.
- **A trailing variable-length field** (`cstring`, or a `repeat`) leaves every
  offset before it static. Emit the fields and omit `size`; that is the expressible
  case and the one that occurs — chani's `{u16, cstr}` resource entry is the only
  one in the Dune databases.
- **A variable-length field that is not last** makes the offsets after it
  underivable, so the converter warns and drops the struct, keeping the names as
  `labels`. Same duty as an unstridable `array` element (§Deferred, streams). R2
  declines this shape too (`layout-not-expressible`), but that check only ever
  sees a hand-written project file: a conversion cannot reach it, because there
  are no offsets to write down.

A converter also reads **every** layout syntax its producer accepts, not only the
current one. chani has two — a `field[name]: type = …` sub-dict per field, and an
older flat `name = type` property — and one Dune database is written in each, so a
converter that handles only the current form silently loses the other's structs.
Once offsets are derived, field **order is the layout**, which makes this more
than a syntax detail: chani's flat form inherits its document model's last-wins
property rule, where a repeated field name moves that field to the end and shifts
every offset after it, while the sub-dict form never dedupes. Reusing the
producer's own parser rather than writing a second one is what keeps a converted
layout identical to what the producer itself would compute.

### Location bindings

**Converter duty: map the location one-to-one, and drop a stack slot with a
count.** chani's location set is the accepted set in §Locations minus the 32-bit
registers, so every register and flag spelling converts unchanged: `@si` becomes
`location: si`, and `@cf` becomes `location: cf`. A stack-slot binding is dropped
and counted, and every other key on that attr converts by its own rule (Table B).
`@bp` is not a stack slot — it binds the BP register itself, and the 4 uses in
`dncdprg` hold a callback offset rather than a frame pointer.

### Offset pin keys

**Converter duty: resolve, or warn and drop.** chani keys `ofs_seg` on the
instruction's address, so a converter must decode that instruction and locate the
16-bit offset field the pin refers to. Exactly one candidate: emit the pin at its
address. Zero, or two or more: **warn and do not emit.** Ambiguity is resolved in
the converter, where the producer's database is still available to look at, and
never encoded into the format.

**This is the one duty that cannot be deferred to runtime, and the one that makes
the binaries a hard input.** Two other duties looked like they needed a decoder and
do not: a `targets` key that is not an instruction start never fires (§Branch
targets), and a name whose address may be code or data goes to `labels` and is
promoted from the real CFG (§Labels). Both defer because Spice86 later knows the
answer. A pin cannot, because the pin's **key** is the thing being computed. Filed
at the instruction's address instead, it lands where
`CSharpAstEmitter.VisitInstructionFieldNode` never looks it up, so nothing at
runtime can recover it.

A `.chani` file holds no bytes. It names its files and states a `load =`
expression per segment, and chani itself reads those files at load time. So a
converter owes the same input: the binaries beside the database, decoded with the
same rules. §Data extents and segment tags already asks for them, to size a
`cstr`. The difference is what absence costs. A missing `cstr` extent degrades —
the type still converts. A missing pin does not degrade: roughly 860 of the Dune
annotations simply do not convert, so a converter run without the binaries must
say so in its report rather than appear to succeed.

A converter that shares Spice86's decoder gets this key right by construction
rather than by inspection, since `FieldWithValue.PhysicalAddress` is both what that
converter would compute and what the emitter looks up. A separate decoder has to
agree on which fields count as candidates. That agreement is worth a cross-check
over every `ofs_seg` annotation rather than a spot test.

### Segment default scope

**Converter duty: emit the space-level default, and drop the attr-level ones.** A
producer repeats the same assumption per function — 32 of dnadl's 89 attrs carry an
`assume` and all 32 are the `ds:seg001` its segment directive already states — so a
mechanical translation restates it many times. Drop each of those silently, because
it states nothing new.

Warn and drop one that differs, such as `ss:seg001` at `seg000:e387` in `dncdprg`.
A range needs an end, and the converter cannot compute one. The producer's own
resolution ends at a clobber (`mov ds, ax`, `lds`, an interrupt). Finding that point
needs a decoder and a CFG, and a converter reading the producer's database has
neither. Ending at the next annotation would state a range the producer never held.
Dropping loses a name and states nothing false. Reaching this range at all needs the
producer to export its own resolution (§Deferred).

### Branch target keys

**A key that is not an instruction start never fires, so checking it is advisory.**
chani reads the attr only while laying out an instruction at that offset, so a
`targets` written mid-instruction is already ignored there. The seeding rule
(§Branch target seeding) gives the same outcome on the Spice86 side for free: a
seed fires when a node is
first created at the source address, a node is only ever created at an instruction
start, and the explorer asks whether targets are keyed at an address it already
holds. Nothing ever asks about a mid-instruction key, so a dead entry stays dead.

**Converter duty: check the key when the binaries are at hand, and warn.** This is
worth doing and is not load-bearing. A converter that decodes the source address
drops the entry with a warning, which tells the user their annotation does nothing —
actionable at convert time, where the producer's database is still open. A converter
without the binaries emits the entry unchecked and loses no correctness. Unlike an
offset pin, nothing here has to be computed: the key is the address chani already
stored, so there is no ambiguity to resolve and no reason to refuse the entry.

The runtime side cannot report a dead entry usefully. A target that never fired is
indistinguishable from one whose branch the session never reached, so the report
counts seeded targets and says nothing about the rest. The convert-time warning is
the only place the distinction is available.

### Comment placement

**Converter duty: attach to the entity when there is one.** A producer may hold
one comment field per address next to the name and signature — chani's `Attr` does
— so the same annotation could go to either place. The rule is mechanical: when
the attribute also produces an entity entry at that address, its comment goes on
that entry; otherwise it becomes a `comments` entry. A multi-line comment needs no
special handling, since the only slot is a line-per-line block.
