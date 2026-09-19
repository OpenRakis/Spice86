# Format — the Spice86 project file (`.spice86.yaml`)

Part of the [Spice86 project file design](README.md). Read
[Purpose and Shared context](README.md) first.

This document is the format contract. It states what the project file holds and
what the colocated graph artifact holds. What a converter must do is in
[chani-converter.md](chani-converter.md). What Spice86 does with a loaded file is
in [spice86-import.md](spice86-import.md).

YAML, one document per project, because every address-keyed fact carries
structured sub-fields (signature, type, layout) that a flat text format handles
poorly. Bulk machine-written graph data stays out of it (§Graph artifact).

The project file also carries **how to launch** the program, so opening it is
enough to run — no `--Exe`/`--ExeArgs` on the command line — and those same
launch inputs are what pin the numeric segment values, so a run and its
addresses are reproducible from the file alone.

## Top-level shape

```yaml
project: cryo-dune-3.7-cd

# How to launch. With this present, `spice86 cryo-dune.spice86.yaml` runs with
# no --Exe/--ExeArgs/etc. A command-line flag, if given, overrides the file.
launch:
  exe: DNCDPRG.EXE                    # --Exe; the launch target, and the `path`
                                      # of its own `files` entry
  exeArgs: ""                         # --ExeArgs
  cDrive: .                           # --CDrive (defaults to exe parent)
  programEntryPointSegment: 0x170     # --ProgramEntryPointSegment
  providedAsmHandlersSegment: 0xF000  # --ProvidedAsmHandlersSegment
  initializeDos: true                 # --InitializeDOS

# Files the run actually opened, hashed as they are read: the launch target the
# loader opens, and every file the program reads through DOS. An entry does not
# say which of the two it is, and `launch.exe` points at the launch target.
# Not a hand-declared input list and not a file->memory mapping: code
# can arrive via a compressed or overlay file whose bytes do not map linearly
# into memory, so no byte-range load expression is stored. Spice86 appends
# entries here as it observes new DOS file accesses.
#
# `hash` is `sha256:<64 lowercase hex>` over the whole file, and sha256 is the
# ONLY accepted algorithm. The spelling is exact: the `sha256:` prefix followed
# by exactly 64 lowercase hex digits, nothing else. Uppercase digits, another
# length, or any other prefix is an inconsistency, not a second supported form:
# the hash is compared, and two hashes of different algorithms compare unequal,
# so tolerating a second one would turn every binding lookup and every launch
# check into a silent mismatch.
#
# An entry states a path and a hash, and nothing else.
files:
  - { path: DNCDPRG.EXE, hash: sha256:0123... }   # the launch target
  - { path: DNVGA.BIN,   hash: sha256:abcd... }
  - { path: DN.HSQ,      hash: sha256:beef... }   # compressed

# Address spaces, by NAME. See the section below for the three kinds.
addressSpaces:
  - { name: seg000, entryDelta: 0x0 }     # part of the loaded exe image
  - { name: seg001, entryDelta: 0xF4B }   # paragraphs: image byte offset 0xF4B0 >> 4
  - { name: segvga, dynamic: true }       # DNVGA.BIN, read through DOS into a
                                          # DOS-allocated block (chani:
                                          # `load = [0x100..]:dnvga`), so its
                                          # number needs a binding
  - { name: vgaram, absolute: 0xA000 }    # hardware, configuration-independent

# Numeric segment values for `dynamic` spaces, per launch configuration.
# Written by a rebind (--BindSpace), never learned on its own.
bindings:
  - key:
      exe: sha256:0123...
      programEntryPointSegment: 0x170
      initializeDos: true
      exeArgs: ""
    map:
      segvga: 0x2F40

# Optional reference to the bulk graph artifact (§Graph artifact). Absent for a
# converter output that has no CFG data. A relative path is resolved against the
# directory holding the project file, never against the current directory.
graph: cryo-dune.cfg.json

# Reusable data layouts. Field offsets are explicit and may leave holes; `size`
# is the total layout size, and is optional - omitted when the layout ends in a
# variable-length field, which makes the struct non-stridable.
structs:
  - name: FrameTask
    size: 0xC
    comment: periodic callback slot
    fields:
      - { name: interval, type: uint16,           offset: 0x0, comment: ticks between fires }
      - { name: elapsed,  type: uint32,           offset: 0x2 }
      # 0x6..0x7 deliberately unknown
      - { name: callback, type: "farptr16<code>", offset: 0x8 }

  # A layout whose tail repeats a runtime number of times. No `size`: the length
  # is only known at runtime, so this struct is not stridable (§Repetition).
  - name: SubResourceTable
    comment: header of a freshly loaded resource buffer
    fields:
      - { name: tableBytes, type: uint16, offset: 0x0, comment: table length in bytes }
      # `type` is the ELEMENT type; `repeat` says how many follow. `unit: bytes`
      # because the header holds a byte length - the program itself derives the
      # entry count as `word >> 1`.
      - name: slots
        type: nearptr16
        offset: 0x2
        repeat: { count: tableBytes, unit: bytes }   # bytes | elements

# Functions. The address is a NAME CANDIDATE and a partition-root hint, never a
# boundary: the CFG partition algorithm decides what a function is. No ranges,
# chunks or tails; a producer's function body is discarded. The call graph is
# not stored either - the partitioner derives it.
functions:
  - address: { space: seg000, offset: 0x83 }
    name: init_game_ui
    comment: sets up the in-game UI
    signature:
      convention: pascal            # informational in phase 1
      params:
        - { name: troop, type: "nearptr16<Troop>", location: si, dir: in }
        - { name: count, type: uint16,             location: cx, dir: inout }
      returns:
        - { name: ok,    type: bool,               location: cf, dir: out }

# Type assertions: "at this point, this storage location holds this type."
# Direction-less, unlike a signature's in/out params. This is how a mid-function
# access is known to hit a struct field: type the register once here, and every
# `[si+disp]` through it resolves to a field. Not derivable by Spice86 (it
# observes values, not types).
#
# `when` picks which side of the instruction at `address` the fact describes:
# `pre` (the default) is the state on entry, before it executes; `post` is the
# state after, which types the value that instruction DEFINES. The pair
# (address, when) names exactly one program point, which is what makes the fact
# unambiguous on an instruction that overwrites the register it reads
# (§When an assertion holds).
assertions:
  - address: { space: seg000, offset: 0x2a }   # `mov si, ds:[si]`
    location: si
    when: pre                                 # the SI that gets dereferenced
    type: "nearptr16<FrameTask>"
  - address: { space: seg000, offset: 0x2a }   # same instruction
    location: si
    when: post                                # the SI it loaded
    type: "nearptr16<Troop>"

# Offset pins: "the constant whose bytes START at this address is an offset into
# space X." Keyed by the address of the constant's own bytes inside the
# instruction, NOT the instruction's address - that is what keeps it unambiguous
# and what the generator can look up (§Offset pins).
offsetPins:
  - address: { space: seg000, offset: 0x1e3 }  # the 0x25c of `mov ax, 0x25c` at 0x1e2
    type: "nearptr16@seg001"

# Default resolution space per segment register, over a range of CODE. `space`
# plus `start`/`end` is where the default applies; `resolves` is the data space
# the register's offsets index. `start`/`end` are optional and omitting both
# covers the whole space (§Segment defaults). A converter emits the whole-space
# form only; a narrower range needs a producer that exports its own resolution,
# which is deferred (§Deferred).
segmentDefaults:
  - { space: seg000, reg: ds, resolves: seg001 }
  - { space: seg000, reg: ss, resolves: seg001, start: 0xe387, end: 0xe3c0 }

# Branch destinations the branching instruction does not carry: an indirect jump
# through a table, a call through a pointer, a branch the program patches at
# runtime. Keyed by the address of the BRANCHING instruction, destinations listed
# flat. Nothing else is stored - no bytes, no blocks (§Branch targets).
targets:
  - address: { space: seg000, offset: 0x123d }   # dispatch through a jump table
    to:
      - { space: seg000, offset: 0x1011 }
      - { space: seg000, offset: 0x1027 }
    comment: main command dispatch, 23 arms

# Typed data globals. `type` is required; `name` is OPTIONAL - a producer that
# knows the type and nothing else states just the type (§Required fields per
# section). Spice86 never invents a `name` for one.
#
# A global is also where a DATA EXTENT comes from: `sizeof(type)` bytes from its
# address are data. There is no separate `ranges` section, because the extent is
# arithmetic on a type the file already carries rather than a second fact
# (§Data extents).
globals:
  - { address: { space: seg001, offset: 0x0 }, name: rand_bits, type: uint16 }
  - { address: { space: seg001, offset: 0x2 }, name: game_time, type: uint32,
      comment: ticks since boot }
  - { address: { space: seg000, offset: 0x15f }, type: uint16 }   # typed, unnamed
  # Extent known, layout unknown - N data bytes and nothing else claimed.
  - { address: { space: seg001, offset: 0x3aa6 }, type: "array<uint8, 0x9c40>" }

# Named but untyped locations. A label reaches the same naming channel as
# functions and globals WITHOUT asserting a layout. Use it when the type is
# unknown or not expressible. Distinct from a global, which always has a type.
# A label on an address the partitioner roots a method at supplies that method's
# name and is written back as a `functions` entry (§Labels). So a producer that
# cannot tell a function from a data location states a label, and Spice86 decides
# once and records the answer.
labels:
  - address: { space: seg001, offset: 0x25c }
    name: repeat_loopstart
    comment: saved loop-start cursors, 3 entries, no fixed element type

# Comments pinned to an address that carries no entity. A comment ABOUT an
# entity lives on that entity's own `comment` field instead (§Comments).
comments:
  - address: { space: seg001, offset: 0x188 }
    text: "cur_job_ptr := this descriptor; overwrites the playing job."
```

## Address spaces

An address space has a **name**, which is the key used by every fact in the
project, and exactly one of three ways to obtain its numeric runtime segment:

| kind         | meaning                                    | number comes from                     |
|--------------|--------------------------------------------|---------------------------------------|
| `entryDelta` | anchored to the load base: the exe image, and what DOS allocates around it | `launch.programEntryPointSegment` + delta |
| `absolute`   | hardware or BIOS, configuration-independent | the stated value                      |
| `dynamic`    | allocated at runtime (DOS, unpacker)        | a `bindings` entry, put there by a rebind |

Most spaces in a single-exe DOS program are `entryDelta`, and their number is
deterministic from the launch configuration, so a new configuration recomputes
them and rediscovers nothing. Only allocation-dependent spaces need a binding.

**`entryDelta` is a segment delta, so its unit is paragraphs (16 bytes).** It is
measured from the base of the **load image** — the exe file with its MZ header
stripped, which is exactly what `programEntryPointSegment` names — so the space's
runtime segment is `programEntryPointSegment + entryDelta`, and the first space
of the image has `entryDelta: 0x0`. The delta is not measured from the entry
`CS:IP` in the MZ header, and no relocation table is involved: relocations fix
segment fields *inside* the image, never the base a space sits at.

**The anchor is the load base, not the image extent.** A space outside the image
is still `entryDelta` when it moves with the load base, which is the normal case
for what DOS allocates: allocation starts from the program's own block, so raising
`programEntryPointSegment` raises the allocation with it. Only a fixed region —
hardware, BIOS, the interrupt vector table — is `absolute`.

**`entryDelta` may be negative**, because a space can sit below the load base: the
PSP is one paragraph under it, and programs read it. `-0x10` and `-16` are both
valid on read and both mean -16 (NFR3). The writer emits hex, unless the chosen
YAML parser rejects a signed hex integer, in which case it emits decimal.

A **binding** is keyed by the launch inputs that move segment values: the exe
hash, `programEntryPointSegment`, `initializeDos`, and `exeArgs`. `exeArgs` is in
the key for two reasons, and the second is the bigger one: it changes the PSP and
environment size, and it can also decide **which module the program loads**,
which moves every space allocated after that module. Cryo Dune selects its sound
driver this way, and DNADL and DNSDB are not the same size.

An unrecognised key is not an error — it means "run it and see". `entryDelta` and
`absolute` spaces recompute from the new configuration and need nothing; a declared
`dynamic` space is rebound once for that configuration (§Minting and rebinding).

**The numeric scalar address has one spelling**: `<segment>:<offset>` in a single
YAML scalar, both parts hexadecimal with an optional `0x` prefix, `:` as the only
delimiter, and no whitespace — `0x170:0x83` and `170:83` name the same address.
An un-prefixed part is hex, never decimal, which is what
`SegmentedAddress.TryParse` already accepts and what existing dump artifacts
contain. A scalar that does not fit this shape is an inconsistency
(§R1 validation); a scalar that does is accepted anywhere a `{ space, offset }`
mapping is expected and reverse-mapped on load (NFR4).

## Launch and reproducibility

- `launch` records the command-line inputs that produce a run. A flag on the
  command line overrides the file value.
- `launch.exe` is a **reference into `files`**: its value is the `path` of a
  `files` entry, and the hash lives only there.
- **`launch.exe` is the only statement of which file is the program.** A `files`
  entry carries no role field, and the spelling `role` stays reserved (§R1
  validation, §Rejected alternatives). `launch` is a single mapping, so a project
  cannot name two launch targets, and there is nothing to check for uniqueness.
- **The container shape decides nothing here.** Spice86 picks its loader from the
  path extension: `.EXE`, `.COM` and `.BAT` load as DOS programs, and every other
  extension loads as a BIOS image. So a COM program is an ordinary launch target,
  and no field states the shape.
- **Paths compare case-insensitively, with separators normalized.** DOS paths are
  case-insensitive, so `launch.exe` and a `files` `path` are matched that way.
  Normalizing means exactly two things: `\` and `/` compare equal, and letter
  case is ignored ordinally — no trimming, no resolution against the file
  system, and no Unicode folding beyond ASCII case. `SUB\DNCDPRG.EXE` matches
  `sub/dncdprg.exe` and nothing else does.
  This lookup is also what supplies the exe hash for a `bindings` key, so a
  spelling that matches nothing leaves that key with no hash.
- **A project with no launch target is valid.** A converted driver database states
  no launch inputs, so it has no `launch` section and no entry is distinguished.
  Absence is never a diagnostic (NFR2), and `--Exe` on the command line supplies
  the target when the user wants to run it.
- `programEntryPointSegment` is load-bearing: it decides where the program is
  placed and therefore every `entryDelta` space's number. Changing it does not
  invalidate stored facts, because facts are name-keyed.

## Data extents, and why they are worth importing

A data extent feeds the speculative explorer as a **pre-poison**, so speculation
stops there before decoding it. Imported extents live in their own set. The
explorer consults the union of that set and the learned poison set.

This does not duplicate the existing poison behavior, it complements it. Poison
is populated today in two places only: `SpeculativeExplorer` on an invalid
decode, and `SpeculativeReconciler` on a signature mismatch at an address that
actually executed. Data that decodes to *valid* instructions is never executed,
so it is never reconciled and never poisoned — it stays in the graph, gets
generated, and inflates the partition set. A producer's data map is the only
thing that catches it. Per byte covered, this is the narrowing import with the
highest value.

**Only the speculative path reads poison.** `CfgNodeIndex.PoisonSet` is consulted
inside `SpeculativeExplorer` and nowhere else, and `CfgNodeFeeder` does not look
at it. So an extent costs speculative pre-discovery only. Code that actually
executes inside one still gets a node through the normal path. That bounds what a
wrong extent can do. It also explains why an extent over bytes that speculation
never reaches buys nothing.

**An extent is not a fact of its own.** A `globals` entry already states an
address and a type, and a type already has a size, so the extent is arithmetic on
a fact the file carries: a global covers `[address, address + sizeof(type))`.
There is no `ranges` section (§Rejected alternatives).

Which follows from that, and none of it needs new syntax:

- **A `name` is not required for an extent.** `globals` requires a `type` and
  makes `name` optional (§Required fields per section), so an unnamed
  global is a typed run of bytes — exactly the shape a producer emits when it
  knows a region is data and nothing else.
- **Extent with no layout is `array<uint8, N>`.** Stride 1, indexable, sized: the
  promise `array` makes is one the accessor can keep, so this is not the widened
  `array` §Repetition refuses. It is what a Ghidra undefined block and a chani
  run of `db` convert to.
- **A type with no static size yields no extent.** `cstring`, or a struct with no
  `size`. Not a diagnostic: the R2 accessor scans for its terminator at runtime
  (the scanning accessor itself is deferred, `terminator-scan-deferred`),
  and a dozen unpoisoned bytes between poisoned neighbours cannot be fallen into,
  since falling through requires decoding the poisoned bytes before them. If it
  ever matters, the fix is scanning memory in the loader under R2's cap — not a
  format field.
- **A `labels` entry contributes no extent.** It has no type by definition. A
  label says the producer did not know the layout; it does not say how far the
  not-knowing reaches.
- **Overlapping extents are normal.** A bulk `array<uint8, N>` with typed globals
  inside it, or a struct with a label on an inner string, both overlap. The loader
  unions them; there is nothing to report.

Three riders:

- **Only learned poison is persisted.** The imported set is never written to the
  `CfgReload` artifact, so it is re-derived from the file on every load.
  Correcting a wrong `size` or `count` removes the poison the old type produced.
  That is what keeps a type-derived extent fail-safe.
- **An extent seeds poison only once its space resolves.** An extent in an unbound
  space waits with the rest of that space's facts (NFR5). Landing it at a number
  that is not its own would stop speculation over real code there.
- **The imported set holds one entry per byte.** It is a
  `HashSet<SegmentedAddress>`, so a 64K data space costs 65536 entries. That is
  acceptable to start. The upgrade path is an interval set, to be taken only if
  the entry count measurably hurts.

## Graph artifact

Bulk, machine-written CFG data stays in its own file, referenced by `graph:`.
It is the `CfgReload` artifact: nodes, typed edges, ordered block membership, and
learned poison. Imported extents are not written here (§Data extents).

**A relative `graph:` path is resolved against the project file's own
directory.** The current directory is not used, so moving a project and its
artifact together keeps the link working, and running Spice86 from anywhere reads
the same file. An absolute path is taken as given.

Reasons to keep the artifact separate:

- It carries per-node byte signatures and node ids — machine-exact, voluminous,
  and not something to hand-edit next to a struct definition.
- Keeping it out means the project file loses nothing on a Spice86 → Spice86
  round trip: selector variants, `maxSucc`, and the aligned-vs-misaligned
  continuation distinction stay where they already round-trip correctly.

Requirements on it:

- Addresses are name-keyed on write like everywhere else. Today `addr`,
  `entryPoints`, `poisonedAddresses` and `CfgBlocks` `entry` are numeric; they
  change on write, and numeric stays accepted on read (NFR4).
- It carries a **`bindingKey`** matching a `bindings` key, and is rejected on
  mismatch — but an artifact without one loads (NFR4). Today there is no such
  guard at all: nodes are restored blind and only the
  current node is matched against memory, so a different
  `programEntryPointSegment` restores a graph at meaningless addresses.

## Importing a producer's blocks

A producer whose database records a CFG (IDA, Ghidra) emits its blocks into the
graph artifact: block entries, extents, and edges. They are loaded **at startup**
and inserted as **speculative** nodes, so `MethodEmitter`'s per-instruction
`VerifySpeculativeEntryOrFail` guard already covers a wrong guess — a bad import
costs dead generated code, never divergent behavior.

chani computes the same graph while loading, and stores none of it in the
`.chani` file. So a chani conversion has no blocks to pass on, and re-deriving
them is a non-goal (§Shared non-goals).

Startup, not dump time: patching a foreign block set into a live graph at
snapshot time would bypass the normal linking and reconciliation path, and some
code is gone from memory by then.

The bytes to decode come from one of two places, chosen by the address space's
kind. Only spaces the producer itself declared are in play here, since blocks come
from a producer and minting is Spice86-side, so `entryDelta` means the image in
this section. The two hard cases are disjoint, so neither needs describing in the
format:

- **An image space (`entryDelta`) is resident at startup but relocated.** The
  importer decodes from **memory**, which already has the fixups applied. The
  producer emits no bytes.
- **A dynamic space is not resident at startup.** A producer holds it at base 0
  with no relocations, so its bytes are byte-identical to what memory will hold.
  The producer emits the **bytes**.

In the importer this is one line (`block.Bytes ?? memoryImage`) followed by a
single decode path. General rule: **bytes are portable exactly when the space
has no relocations.** A dynamic space that is itself relocated (a DOS overlay
manager relocating an overlay at load) falls back to decode-from-memory by the
same rule.

Supplied bytes are **decode input only, never a node signature.** The `__`
wildcards in a node signature mean "field read from memory at execution time"
and drive dynamic-dispatch codegen; a relocated immediate is static, just
unknown to the producer. Nothing in this path may put a relocated field into a
signature.

Ordering consequence: a `dynamic` space has no number on a first run, so its
blocks cannot be placed. They are skipped with an info-level report and imported
from the second run on, once the binding is recorded. Image spaces always
import.

A producer may emit an optional instruction **length** per node. The importer
compares it against the decode and skips on mismatch — the cheapest check that
fails when a region is not what the producer saw (not yet unpacked, or already
overwritten). Anything that slips through is speculative and guarded at runtime.

## Types

The format uses **its own type vocabulary**, both to read like Spice86
(`uint16`, not `u16`) and because Spice86 targets 32-bit code that a 16-bit-only
producer grammar cannot express. A converter **translates** into these; it is
never a pass-through.

- **Integers state width and sign explicitly**: `uint8` `uint16` `uint32` `int8`
  `int16` `int32`. Signedness is first-class, so there is no `signed()` wrapper.
  `bool` names a boolean byte.
- **Strings come in two forms**: `string(N)` (fixed N-byte buffer) and `cstring`
  (terminated, NUL by default). The terminator is not always NUL in DOS code —
  INT 21h/09h prints a `$`-terminated string, and high-bit or `0xFF` sentinels
  are common in game data — so `cstring` takes an optional terminator byte:
  `cstring(0x24)`. It must be a single byte value; `cstring` alone means
  `cstring(0x00)`.
- **An array is spelled `array<T, N>`.** `T` must be **sized**, because an array
  is random access: element `i` is at `base + i * sizeof(T)`. An array whose
  element size cannot be determined is an **error**, not something to widen the
  type over — that shape is a `stream` (§Deferred).
- **Pointers are one family**, and the numeric suffix is the **offset width**.
  Near is offset-only, far prepends a 16-bit segment: `nearptr16` (2 bytes),
  `nearptr32` (4), `farptr16` (16:16, 4), `farptr32` (16:32, 6). Each takes an
  optional pointee `<T>`: a scalar, a struct name, or **another pointer** for a
  data pointer (`nearptr16<Troop>`, `nearptr16<nearptr16<Troop>>`), `<code>` for
  a code pointer (`farptr16<code>`), or omitted for an untyped address. A code
  pointer is just a pointer whose pointee is `code`; there is no separate
  function-pointer type.

  Nesting is allowed because producers state it: a walking cursor over an array
  of record pointers is `**Troop` in chani, and without nesting a converter
  would have to either drop the inner type or flatten it into the wrong claim
  that the pointed-to word *is* a `Troop`. Depth is not capped; it is bounded by
  what the producer knows. Nesting is a **type** statement, not an extra
  accessor: R3 still rewrites one dereference, so the inner type says what the
  loaded word means and a further binding on the register holding it (§type
  assertions) is what makes the second dereference typed.

  A **near** pointer may carry an optional resolution space `@<name>` that
  **pins** the space its offset indexes: `nearptr16<code>@seg001`. `@` is not
  required, and when **absent the space is dynamic** — supplied at runtime by
  the ambient segment register, possibly varying between uses. A dynamic near
  pointer is not an incomplete static one: Spice86 does **not** bake a space
  back into the type, because freezing a runtime-varying value into a static pin
  would break the faithful guarantee. Concrete targets reached through one are
  recorded as observed graph edges instead (see SQ1). What the ambient register
  holds may still be known statically, from a producer's segment default
  (§Segment defaults); that stays a separate range-scoped fact and is never
  folded into the type. Far pointers carry their segment in the value, so `@` is
  near-only.
- **A struct is named**: `FrameTask`.

`code` as a pointee means a code pointer; it is never a field type on its own.

### Grammar

A type string is the same language everywhere one appears — struct fields,
`globals`, `assertions`, signature params, `offsetPins`:

```
type          = int-type | "bool" | string-type | array-type | pointer-type | struct-name ;

int-type      = "uint8" | "uint16" | "uint32" | "int8" | "int16" | "int32" ;

string-type   = "string" "(" length ")"                (* fixed N-byte buffer *)
              | "cstring" [ "(" byte-value ")" ] ;     (* terminator, default 0x00 *)

array-type    = "array" "<" type "," count ">" ;

pointer-type  = near-kind [ "<" pointee ">" ] [ "@" space-name ]
              | far-kind  [ "<" pointee ">" ] ;
near-kind     = "nearptr16" | "nearptr32" ;
far-kind      = "farptr16" | "farptr32" ;
pointee       = "code" | type ;

struct-name   = identifier ;   (* a name in `structs` *)
space-name    = identifier ;   (* a name in `addressSpaces` *)

count         = number ;       (* >= 1 *)
length        = number ;       (* >= 1 *)
byte-value    = number ;       (* 0x00..0xFF *)
number        = decimal-digits | "0x" hex-digits ;
identifier    = letter , { letter | digit | "_" } ;
```

Whitespace between tokens is insignificant, so two spellings of one type parse
equal (NFR3). The writer emits one space after an `array` comma and none
elsewhere.

Four properties are structural rather than prose, which is the point of writing
it down:

- **`@` is near-only and pointee-independent.** Both are separate optional parts
  of `pointer-type`, so a bare pin (`nearptr16@seg001`) and a typed pin
  (`nearptr16<FrameTask>@seg001`) are equally valid, and `farptr16@seg001` does
  not parse. The offset-pin example above relies on the bare form.
- **Nesting is unrestricted.** `pointee` is `type`, and `array`'s element is
  `type`, so pointers nest (`nearptr16<nearptr16<Troop>>`), arrays hold pinned
  pointers (`array<nearptr16@seg001, 3>` — 27 uses in the Dune databases), and
  arrays nest in each other. Depth is bounded by what the producer knows.
- **`code` appears only as a pointee**, so it cannot be a field or element type.
- **`count` is a literal**, not an expression (§Repetition). A runtime count is
  the `repeat` field attribute, which is not part of this grammar: a `repeat`
  field's `type` is the ordinary element type.

Two constraints the grammar cannot state, checked by the loader (§R1
validation): an `array` element must be **sized**, and a `struct-name` /
`space-name` must be defined.

Reserved spellings (`enum`, `union`, `stream`, and the typedef and bitfield
forms) are **not** in this grammar and do not parse in this pass (§Deferred).

Struct layout rules: field `offset` is explicit and holes are allowed. `size`,
the total layout size, is **optional** — supply it whenever it is known, because
it carries three things nothing else does: it makes `array<Struct, N>` stride, it
bounds the R2 accessor class, and it is the only way to state bytes that belong
to the struct *after* its last known field. That last case is both alignment
padding and an unknown tail — a record whose extent the producer knows while its
contents it does not.

`size` is absent when it cannot be determined: a struct ending in a
variable-length field, `{uint16, cstring}` being the shape that occurs in
practice. Absence is not a diagnostic (NFR2), and it costs exactly two things.
The struct is **not stridable**, so it may not be an `array` element; and the
accessor class has no overall bound, so each field accessor bounds itself.

Field offsets are unaffected by an absent `size`. A variable-length field at a
known offset still has a known offset, so a **trailing** one leaves every offset
in the struct static and the layout fully expressible. Only a variable-length
field with fields *after* it makes later offsets non-static, and that is what R2
declines (§R2).

The layout is validated for **non-overlap**, not for contiguity — a producer
typically knows a few offsets and nothing between them. When `size` is present,
no field may extend past it.

Enums, named bitfields, unions and typedefs are **not in this pass** but their
spellings are reserved, so converters do not invent their own (§Deferred).

## Locations

A `location` says **where a value lives**. It appears in exactly two places: a
signature's `params` / `returns`, and an `assertions` entry. A location is
storage and never an address, so a global or a struct field is never one.

The accepted set is closed. A spelling outside it is an inconsistency (§R1
validation):

```
location = gp32 | gp16 | gp8 | sreg | flag ;

gp32     = "eax" | "ecx" | "edx" | "ebx" | "esp" | "ebp" | "esi" | "edi" ;
gp16     = "ax"  | "cx"  | "dx"  | "bx"  | "sp"  | "bp"  | "si"  | "di" ;
gp8      = "al"  | "ah"  | "cl"  | "ch"  | "dl"  | "dh"  | "bl"  | "bh" ;
sreg     = "es" | "cs" | "ss" | "ds" | "fs" | "gs" ;
flag     = "cf" | "zf" | "sf" | "of" | "pf" | "af" ;
```

Spellings are lowercase, matching `reg` in `segmentDefaults`. The 32-bit names
are here for the reason the type vocabulary carries `uint32`: Spice86 runs 32-bit
code that a 16-bit-only producer grammar cannot express (§Types). A producer with
no 32-bit locations emits none, and absence is never a diagnostic (NFR2).

`cs` is accepted here while `segmentDefaults` rejects it (§Segment defaults). The
two keys ask different questions. A `location` names storage, and CS is storage.
A `reg` there names the register an offset resolves through, and CS states
nothing about that.

**A stack slot is not in this pass.** A producer can bind a type to a signed
bp-relative slot, and chani spells it `@ +6` for an incoming argument or `@ -0x10`
for a local. That spelling stays reserved and does not parse here (§Deferred,
stack-slot locations).

**Two locations that overlap are two distinct keys.** `dx` and `dl` name
overlapping bytes. An assertion on each at one program point is two facts, not a
conflict, because the `(address, location, when)` key compares by spelling.

### An 8-bit half of a 16-bit binding

27 of the location bindings in the Dune databases are 8-bit halves, which makes
this the second-most-used location kind. Four rules cover it. The first two answer
the two directions, and they answer them differently.

**A binding on a half binds that half alone.** An assertion or a param on `al`
types AL. It does not type AX, and AH keeps whatever it had.

**A binding on a 16-bit location does not type its halves.** An assertion on `dx`
of type `nearptr16<Troop>` says nothing about `dl`. Narrowing the type is not
sound: the low byte of a near pointer is not a near pointer, and it is not half a
`Troop` either.

**A write to either side stops the backward scan.** R3 walks back from a use site
to the nearest assertion for a location, stopping at any write to it (§R3). A
write to any *overlapping* location stops it too. So `mov dl, 0` stops a scan for
`dx`, and `mov dx, ax` stops a scan for `dl`. Stopping is the fail-safe
direction, because the scan then finds no type and the access stays raw.

Naming is what chani does with a parent binding, and this format has nowhere to
put it. chani's listing rewrites `dl` to `skill_sum.lo` and `dh` to
`skill_sum.hi`, which is a display name for one half of a named value. Nothing in
R1–R3 renames a register: generated code reads the emulator's register fields,
and named local recovery is deferred to the SSA work (§R3 non-goals). So a half
binding is stored, round-tripped, and shown in the signature comment R2 emits,
and it needs no C# identifier.

**A half is a value, never an address base.** 16-bit addressing takes its base
from `bx` or `bp` and its index from `si` or `di`. Under 32-bit addressing every
`gp32` register is a legal base, `eax` and `esp` included. So an 8-bit location
can never base a lowered access, and neither can a flag, a segment register, or
`ax`/`cx`/`dx`/`sp`. R3 declines an assertion on any of them and reports
`location-not-an-address-base` (NFR5). Declining is expected here rather than a
defect. That covers the 27 half bindings and the 17 flag bindings in the Dune
databases: the facts are real, and this pass has no consumer for them.

## Repetition

Repetition has three spellings, one per capability set. A single widened `array`
would give one name three different contracts, so each rung is named for what it
can actually do:

| spelling      | count   | stride  | indexable             | sized |
|---------------|---------|---------|-----------------------|-------|
| `array<T, N>` | static  | static  | yes, constant bound   | yes   |
| `repeat`      | runtime | static  | yes, runtime bound    | no    |
| `stream`      | runtime | runtime | no, forward walk only | no    |

`array<T, N>` is the common case and covers every repetition in the Dune
databases. `stream` is deferred (§Deferred).

**`repeat` is a field attribute, not a type.** The field's `type` is the
**element** type, and `repeat` says how many of them follow:

```yaml
- { name: tableBytes, type: uint16, offset: 0x0 }
- name: slots
  type: nearptr16
  offset: 0x2
  repeat: { count: tableBytes, unit: bytes }
```

`count` names another field of the same struct, holding the number at runtime.
`unit` says what that number counts: `elements`, or `bytes`, in which case the
element count is `count / sizeof(T)`.

The unit is not speculative — the one real instance needs it. Dune's sub-resource
offset table (`adjust_sub_resource_pointers`, `seg000:0098`) stores its length in
**bytes** and the program derives the entry count as `word >> 1`. As a unit that
is two enum values.

It is an attribute rather than type syntax because a type string appears in five
places — struct fields, `globals`, `assertions`, signature params and
`offsetPins` — and a reference to a sibling field is meaningful in only one of
them. Keeping it off the type means no type spelling is context-dependent, and
that costs nothing in practice: the motivating table is reached through a
register, so an assertion carries the ordinary portable
`nearptr16<SubResourceTable>` while the runtime count stays inside the struct
definition.

**The format carries no expression language.** The spelling
`array<nearptr16, tableBytes/2>` is
**rejected, not deferred** (§Rejected alternatives), and the decisive reason is
ownership rather than cost. `word >> 1` is code: it runs at
`seg000:0098`. Restating it in a type string puts a second, hand-maintained copy
of that computation in the format, beside an emulator that already performs the
first one, and the two can disagree. That inverts the split in §Shared context —
heavy analysis stays in the producers, Spice86 emits a faithful translation of
what it observed. A parser generator needs an expression language because it has
no other source of truth. Spice86 has a running program.

Three costs beyond the parser and the evaluator, recorded in case the question is
reopened:

- **It is a second trust boundary.** The expression consumes a program-supplied
  value, and its result feeds an address computation. Division by zero and 16-bit
  overflow become the loader's problem at exactly the point where being wrong
  means an out-of-range read.
- **R3 stops lowering.** Lowering requires an offset expression of one register
  plus at most one constant term (§R3). A stride derived from an expression is not
  a compile-time constant, so those accesses fall back to raw — the opposite of
  what the feature exists for.
- **There is no natural stopping point.** `tableBytes/2` invites `count-1`, then a
  field of a nested struct, then a conditional. No principled boundary is left
  after the first operator, and the writer then needs an AST printer to round-trip
  what it parsed (NFR3).

Not having it is cheap and visible. A shape the format cannot state is declined by
the converter, or degrades to a `label` that keeps the name without the layout,
and NFR5 puts it in the report with a reason. The failure mode is a missing
accessor and a report line, never a wrong offset.

The known limit is that `unit` is a two-value enum. If a second shape appears, the
upgrade is another knob on this attribute: `scale` and `bias` beside `unit`. Those
two cover off-by-one and stride-multiple headers without a grammar. Expressions
are worth reopening only at a third genuinely different shape, with three real
examples to design against instead of zero.

A repeated field is variable-length, so it obeys the rules already stated with
nothing added. The struct gets no `size`, it is not stridable, it may not be an
`array` element, and fields after it are declined by R2. In practice a `repeat`
is therefore the last field.

## When an assertion holds

`location: si` keyed on an address alone is ambiguous, for the same reason an
instruction-keyed offset pin was. `mov si, ds:[si]` has two SI values at one
address — the pointer that is dereferenced and the word that is loaded — and "SI
holds a `nearptr16<FrameTask>` here" names neither. A register has no address of
its own, so unlike an offset pin this cannot be fixed with a finer key. It is
fixed by making the program point part of the fact.

**An assertion is keyed by `(address, location, when)`, and `(address, when)`
names exactly one program point.** §Locations defines the accepted `location`
set and states what two overlapping locations mean. `when: pre` is the state on
entry to the instruction, before it
executes; `when: post` is the state after it completes, which is the same thing
as typing the value that instruction defines. In the example the two SI values
are two assertions at one address, one per side, and neither is ambiguous.

`pre` is the default, because it is what producers already mean: chani applies an
attr `assume` before the instruction at that address and carries it forward
through its segment dataflow, so its facts are "in effect on entry, and onward".
A converter that has no notion of side emits `pre` and is correct.

`post` exists because the alternative was worse. Without it, typing a loaded
value means keying the fact at the **next** instruction's address, and that
address may mean something else entirely: it can be a merge point, where a `pre`
fact applies to every predecessor rather than just the path through the load, and
after a branch there is no single next address at all. `post` states the fact at
the point it is actually known, and needs no decode to find a neighbouring
address.

Two consequences worth stating:

- **An assertion applies on every path reaching its program point.** At a merge
  point a `pre` fact therefore covers all predecessors. `post` is finer — it is a
  point inside one instruction, so it cannot over-apply that way — but a general
  path qualifier is still out of scope (§Deferred).
- **A wrong assertion costs a wrong name, not wrong behavior.** Pre-SSA R3
  only comments the access, so a wrong type yields a misleading comment above
  an unchanged faithful access. The post-SSA rewrite keeps the property: a
  field accessor over the same base and the same displacement reads the same
  bytes. Off any field boundary, R3 does not fire and the raw access stands.

The consumer cost of `when` is close to zero, which is why it is in this pass
rather than deferred: R3's pre-SSA scan walks back from a use site to the nearest
assertion for that location, and `pre` versus `post` only decides whether the
assertion at the scan's own starting instruction counts. An off-by-one, not a
mechanism. Under SSA the two spellings become a clamp on a value's live-in and a
clamp on a definition respectively, both of which SSA represents natively.

A `post` assertion on a location the instruction does not write is redundant, not
wrong — the post-state equals the pre-state — so it is accepted silently rather
than reported.

`when` is meaningless on an offset pin and is not accepted there: a constant in
the instruction stream has no before and after.

## Offset pins

A producer knows something about a 16-bit offset constant that Spice86 cannot
learn by running: **which address space the offset indexes.** chani spells it
`ofs_seg` on an attr and it is its second-most-common annotation after
type/name/comment — ~900 uses in the Dune databases, ~860 of them on code
addresses carrying no type at all. Two cases hide behind it:

- **An immediate carries it**, as in `mov ax, 0x25c` where `0x25c` is a pointer
  value. Spice86 observes a number in a register and nothing more; that it indexes
  `seg001` is unrecoverable by any amount of running — the same
  observes-values-not-types gap that motivates `assertions`.
- **A displacement carries it**, as in `mov ax, [0x25c]`. Here the space comes
  from the ambient segment register, which SQ1 captures at runtime, but only for
  instructions that actually executed. The pin is static, so it also covers code
  that never ran and is available on a first run.

**The pin is keyed by the address of the constant's own bytes**, not by the
instruction's address. `mov ax, 0x25c` at `seg000:0x1e2` puts its immediate at
`seg000:0x1e3`, and that is the key. This is the whole design decision, and it
buys two things at once.

*It is unambiguous by construction.* An instruction with two 16-bit constants
(`mov word [0x100], 0x200`) has them at two different addresses, so there is
nothing to disambiguate and no operand index to invent. The format never carries
an ambiguous pin.

*It is the handle the generator already has.* The generator emits from an AST
that has no notion of "operand N". Both cases above converge on a single node
type: an immediate becomes an `InstructionFieldNode` via
`InstructionFieldAstBuilder`, and a ModRM displacement or an absolute
`OFFSET_FIELD_16` becomes one via `ModRmAstBuilder`. That node holds the decoded
`FieldWithValue`, whose `PhysicalAddress` is exactly the address of the constant's
bytes — it is the only expression node in the AST with a back-reference to where
its value came from. So the consumer is one lookup at one visit site
(`CSharpAstEmitter.VisitInstructionFieldNode`), keyed on a value the node already
carries, and it covers immediates and displacements with the same line. An
instruction-keyed fact could not be consumed there at all: the emitter knows
which *field* it is rendering, not which operand of which instruction.

The type is the existing grammar (§Types): `nearptr16@seg001` — 2-byte offset,
unknown pointee, resolution space pinned. When the producer also knows the
pointee it says so (`nearptr16<FrameTask>@seg001`) and R3 gets a typed access out
of the same entry. The stated width must match the decoded field length; a
mismatch is an inconsistency.

Three riders:

- **A pin does not propagate.** `assertions` type a register and reach forward to
  its use points (R3); a pin types one constant at one address and stops. It is
  never an input to the register-type scan.
- **A pin does not apply to a non-static field.** When `UseValue` is false the
  field is written by the program, the node resolves to a memory read instead of
  a literal, and the producer's statement about the constant no longer describes
  what runs. Skip the pin there; the existing flag is the guard, no new state.
- **The data-side use needs nothing new.** A `dw` holding an offset into a
  segment — the handful of `ofs_seg` uses on typed data attrs — is a `global` (or
  struct field) of type `nearptr16@seg001`.

## Segment defaults

A near offset does not say which space it indexes, and usually neither does the
instruction: `mov ax, [0x25c]` takes its space from whatever DS holds. A producer
states the answer statically, per segment register — chani spells it
`assume = ds:seg001` once per segment, and that one line is what makes every
`[disp]` in that segment resolve to `seg001`. All three Dune databases carry one.

**This is an offset pin's fact class at a coarser scope**, and the same argument
imports it: the default is static, so it covers code that never ran and works on a
first run, which is what SQ1's observed values cannot do.

`space` with `start`/`end` is the **code** range the default applies to;
`resolves` is the **data** space the register's offsets index. `start` and `end`
are optional, and omitting both covers the whole space — the shape a
segment-level directive converts to.

```yaml
segmentDefaults:
  - { space: seg000, reg: ds, resolves: seg001 }
  - { space: seg000, reg: ss, resolves: seg001, start: 0xe387, end: 0xe3c0 }
```

`reg` is one of `ds`, `es`, `ss`, `fs` or `gs`. **`cs` is
not accepted**: the space hosting the instruction is already known, so a `cs`
default states nothing, and a producer that prints one (chani synthesizes
`assume cs:<segment>` for its listing) has it dropped by the converter.

**Resolution order: narrowest wins.** Several facts can answer for one access, so
the order is fixed:

1. an **offset pin** at the constant's own address (§Offset pins)
2. an **assertion** on the base register whose type carries `@space` (§Types)
3. the **observed** segment value for that node, when SQ1 has one
4. the **`segmentDefaults`** entry for the enclosing range
5. nothing — the access stays raw and unnamed

A static fact disagreeing with an observed value at one site is a report line, not
a silent choice (NFR5, `observed-space-disagrees`): either the annotation is wrong
or the code runs with a different DS than the producer assumed.

**A default names, it never addresses.** Resolution decides which symbol a number
refers to and which layout an offset falls in. It never changes an emitted address
computation: generated code keeps reading through the runtime segment register, so
`UInt16[ds, 0x25c]` stays `UInt16[ds, 0x25c]` and only gains the name (R2) or a
field accessor over the same bytes (R3). Nothing on this path may constant-fold a
space into a generated address.

A producer may state a default while Spice86 may not infer one, for the reason
already given for `@` (§Types). A wrong one costs a name and not behavior, as a
wrong assertion does (§When an assertion holds).

## Branch targets

A producer records, by hand, the destinations of a branch whose address the
branching instruction does not carry: an indirect jump through a table, a call
through a function pointer, a branch whose operand the program patches at
runtime. chani spells this `targets` on an attr keyed by the **branching
instruction's address**. The Dune `dncdprg` database has 151 of them, carrying
319 edges.

The structure is chani's: one source address, a flat list of destinations.

```yaml
targets:
  - address: { space: seg000, offset: 0x123d }
    to:
      - { space: seg000, offset: 0x1011 }
      - { space: seg000, offset: 0x1027 }
```

**Nothing else is stored.** Not the bytes of the branching instruction, not the
destination's instructions, not blocks. Bytes and signatures would repeat what
the graph artifact already holds, and the destination's instructions are what the
speculative explorer decodes from live memory once it has the edge. The only
irreducible fact here is the edge itself.

This does not duplicate the CFG either. The CFG holds edges Spice86 observed;
this holds an edge Spice86 will never observe, because the arm is not taken in
the recorded run. The two sets are disjoint in content and only alike in shape.

**This is the one fact in the format that widens speculation.** It is accepted,
while entry-point seeding is not (§Deferred), because the widening is bounded:
the source is an instruction the program actually executed, so an imported target
only adds arms of a branch that was already reached. An entry point has no such
anchor.

## Comments

**A comment about an entity lives on that entity; `comments` is only for an
address that carries no entity.** `functions`, `globals`, `labels`, `targets`,
`structs` and struct fields each have their own `comment` field, so a comment on
a named thing is attached to the thing rather than floated beside it at the same
address. `comments` covers what is left: a remark about one instruction inside a
body.

That split is what removes the placement question. A comment has exactly two
possible slots in generated C# — a doc comment on the method, or a line in the
body — and which one applies follows from where the comment is stored, so no
`kind` field selects it (§R2). `functions[].comment` becomes the doc comment,
falling back to a line at the entry address when the partitioner did not root a
method there — the same fallback a loaded `signature` already uses. Everything
else becomes a line.

A `kind` on a comment is **not** an accepted key, and the spelling stays
reserved, so a converter does not invent one (§Rejected alternatives).

Both spellings at one address is not an error. A hand-written file carrying a
`functions[].comment` and a `comments` entry at the same address gets a doc
comment and a body line: two slots, nothing to merge.

## Required fields per section

Three sections take a name, and they differ in what is mandatory:

| section     | `name`   | `type`                   |
|-------------|----------|--------------------------|
| `functions` | required | n/a (`code` is implicit) |
| `globals`   | optional | required                 |
| `labels`    | required | none by definition       |

Each section requires the fact that is its reason to exist and nothing more. A
`functions` address alone seeds nothing, so the name is required. A `globals`
type is unrecoverable by running, so it is required while the name is not. A
`label` is a name and only a name.

`functions` and `labels` therefore differ in exactly one thing a converter can
state: whether a signature is attached. Both reach the method-naming channel,
`functions` unconditionally and `labels` through promotion (§Labels). So a
converter that cannot tell code from data picks `labels` and loses nothing, and one
that has a signature to attach picks `functions` because it has somewhere to put
it.
