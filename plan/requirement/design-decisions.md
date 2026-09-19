# Design decisions — rejected and deferred

Part of the [Spice86 project file design](README.md). Every rule in the other
three documents that says "rejected" or "deferred" points here.

## Rejected alternatives

Keys and sections this format does not accept, one line of reason each. The
other documents state the rule and point here. Every spelling below is reserved, and
the loader reports it as an inconsistency (§R1 validation), which is what stops a
converter from inventing its own. These are rejected, not planned: §Deferred holds
the work that is planned.

| spelling | rejected because |
|---|---|
| `source: user \| observed \| inferred` on a fact | codegen guards on speculative-vs-observed, and the CFG already holds that per node. Ranking producers against each other only matters for re-import, which is deferred (§Deferred, merging several project files) |
| `kind: eol \| pre \| plate` on a comment | the slot follows from where the comment is stored (§Comments). `functions[].comment` is the plate comment, and `CSharpSourceWriter` writes whole lines, so no comment has a column to sit in |
| `role` on a `files` entry | `launch.exe` already names the launch target, and two statements of one fact can disagree (§Launch and reproducibility) |
| `group` / `alternativeOf` on an address space | `bindings` is keyed by launch configuration, so the map under one key already omits the driver that does not load (§Merging several producer databases) |
| a `ranges` section | a `globals` entry states an address and a type, and a type has a size, so the extent is arithmetic on a fact the file already carries (§Data extents) |
| an expression as an `array` count, `array<nearptr16, tableBytes/2>` | `word >> 1` is code that runs at `seg000:0098`, so a copy of it in a type string sits beside an emulator that already runs the original (§Repetition) |

---

## Deferred

Decided to be out of this pass, recorded so the assumptions in the other three
documents stay honest.

- **Overlays are two separate problems**, and phase 2 must not start by solving
  only the first: (1) knowing which overlay is live at runtime, which is
  game-dependent detection; (2) real-mode **aliasing** — two spaces can overlap
  physically at different segment bases, so one byte has several
  `(space, offset)` keys and it is undefined which one owns a fact. Until then
  the project assumes a single overlay. Representation needs no new field: two
  names bound to the same number already give separate namespaces.

  Reporting the aliasing miss needs no space size either, and comparing whole
  spaces is the wrong check. `addressSpaces` states no extent, and assuming a
  64 KiB window would report every adjacent allocation. `dncdprg` `seg000` and
  `seg001` sit `0xF4B` paragraphs apart and do not overlap, yet a 64 KiB window
  over `seg000` would run `0xB50` bytes into `seg001`. The check that reports the
  real miss is per fact: the loader already derives a byte extent for every
  `globals` entry (§Data extents), so those extents can be indexed by physical
  address. A report then fires when a resolved access lands inside a fact keyed in
  a different space, which is one lookup at the site R2 already calls the
  space-resolution query. It stays out of this pass because phase 1 offers no way
  to repair the miss it would report.

- **Merging several project files stays out.** The shape would be `include` with a
  flat last-wins overlay, so
  a converter output can be clobbered on re-export without losing Spice86-side
  work. Phase 1 is a single file: read, run, rewrite; re-importing is a clobber
  resolved outside Spice86. When it happens, the cheap shape is keeping the
  parsed producer document in memory and diffing at export — not tagging origin
  on every fact. NFR3 is what keeps this open.

  This entry is about re-import only. Merging several *producer databases* into
  one project file is a different problem. That merge happens in the converter,
  and it is in this pass (§Merging several producer databases).

- **Enums, named bitfields, unions and typedefs stay out.** They are the bulk of
  an IDA `til` or Ghidra `DataTypeManager` export, and flag bitfields on a
  status byte are everywhere in DOS code. The spellings are reserved now and the
  semantics come later.

- **Streams of variable-length records stay out.** `array<T, N>` promises random
  access — element `i` at `base + i * stride` — so it requires a sized `T`. A
  run of records whose length varies (a struct ending in `cstring`) has no
  stride and can only be walked forward from the base. That is a different
  capability, so it gets a different name rather than a widened `array`: naming
  it `array` would promise indexing that no accessor can deliver, leaving R2 to
  emit either a lying constant stride or a hidden walk on every `[i]`. The
  `stream` spelling is reserved so a converter does not invent one. It is the
  third rung of the repetition ladder in §Repetition: `array` fixes both count
  and stride, `repeat` lets the count vary, `stream` lets both vary.

  Not needed yet, and the two levels disagree about that, which is worth
  recording. chani's grammar *permits* it — its layout model is a byte-consuming
  cursor with no field offsets and no struct size, so a variable-length array
  element costs it nothing. Its databases *contain* none: the 245 `ResTableEntry`
  (`{u16, cstr}`) uses are separately addressed globals, each with its own name
  and a companion label on the inner string, so the producer already did the walk
  and wrote down every result. A converter meeting one owes the same duty as an
  ambiguous offset pin (§Offset pin keys): warn and drop, keeping the names as
  `labels`, never emit an `array` that cannot stride.

  When it lands, the minimum consumer is a per-element runtime length accessor
  plus forward iteration from the base; O(1) indexing is not recoverable. Scope
  limit worth stating, because it is easy to over-read: this is only about
  finding element `i`. Offsets *inside* one record are static even when its total
  length is not, so pointer-based access (`*ResTableEntry` in a register, fields
  at displacement 0 and 2) is untouched and needs no stream.

- **Stack-slot locations stay out.** A producer can bind a type to a signed
  bp-relative stack slot, and chani spells it `@ +6` for an incoming argument or
  `@ -0x10` for a local. The spelling is reserved and does not parse in this
  pass (§Locations). Three facts make deferring it cheap, and all three are
  about the producer rather than about cost.

  Nothing in chani produces one. `Location::Stack` is only ever built by the
  binding text parser, so there is no prologue analysis and no frame-pointer
  detection behind it. A user writes the offset by hand, or the slot does not
  exist.

  Nothing in chani consumes one either. Its type-propagation state keeps a
  bp-keyed map of slot types, and no query reads it: the listing renderer maps a
  `[bp+disp]` operand to the BP register and asks whether BP holds a pointer. So
  a stack binding is parsed, round-tripped, and never rendered.

  The three Dune databases hold zero stack bindings, against 27 8-bit halves and
  17 flags. There is no data to convert and no producer behavior to match. The 4
  `@bp` bindings are the BP register holding a callback offset, not frame
  references.

  When it lands, a stack slot is a second addressing mode for a binding and not a
  new fact kind, so the `(address, location, when)` key needs no change. The
  consumer cost is the real one: a slot type has to be tracked through memory, and
  R3's block-local register scan does not do that.

- **`noreturn` is not a generator or runtime input.** `CfgGeneratorContext`
  derives a call continuation from observed edges and already handles the none
  case. Its only consumer would be `SpeculativeExplorer.EnqueueContinuation`
  under trust, so the payoff is narrower than it looks.

- **Entry-point seeding stays out.** A producer's entry points would *widen*
  speculation with nothing to bound the widening, which the narrow/widen rule
  treats as the reluctant case. `SeedKnownSafe` stays reserved for
  emulator-installed handlers. Indirect branch targets were deferred alongside
  entry points, on the grounds that imported blocks already carry the edges a
  jump table would have seeded. That does not hold for a converter which emits
  no blocks — a chani conversion carries none — so it left the annotation with
  no path at all. They are in the format now, as an untrusted seed on its own
  explorer path (§Branch targets). An entry point keeps no equivalent bound, so
  it stays out.

- **Whole-segment data tags stay out.** A producer may tag an entire segment as
  data, and honouring the tag would pre-poison every byte in it. In `dncdprg`
  that is 56606 bytes of `seg001`. Two reasons keep it out, and neither one is
  byte granularity. Byte granularity is the argument against the `code` tag,
  which is dropped outright for a different reason: the format has no code range
  to convert it into (§Data extents and segment tags).

  The tag carries no analysis. chani's format reference calls `segment` `type` a
  free-form string tag, and chani stores it that way. Its only readers render the
  segment header and choose whether to print `assume cs:`. Nothing marks bytes
  from it, and nothing checks it against the attrs.

  The bulk it would cover is where the mechanism pays least. Of `seg001`'s 56606
  bytes, the `load` expression backs 16060 with file bytes and 40546 are past the
  image. One unannotated run of 21901 of those starts at `RESOURCE_GLOBDATA`, a
  buffer the program fills at runtime. Speculation reaches that range only through
  a branch target that lands inside it, so pre-poisoning it prevents little. The
  file-backed part is already covered densely by per-annotation extents: 1124 typed
  starts over 16060 bytes, largest gap 1960 bytes.

  Being wrong is cheaper than it looks, which is why this is deferred rather than
  refused. Poison is read only on the speculative path (§Data extents). A wrong
  bulk claim therefore costs speculative pre-discovery, not execution coverage.
  The imported set is also re-derived from the file on every load. The format needs
  no change when this is wanted: a bulk region is one unnamed `globals` entry of
  type `array<uint8, N>`. That keeps it a user-requested converter option, and it
  keeps the claim attributable to whoever made it.

  Two things to settle before it lands. First, nothing prunes an imported extent
  when an address inside it executes. A buffer that later receives executed code
  then blocks speculation into it for the rest of the run. Second, nothing reports
  how many bytes an imported extent pre-poisoned. A wrong bulk claim would show up
  as missing generated code with no line pointing at the cause, and the reload that
  fixes it is only cheap once the user can tell what to fix.

- **Automatic binding of a `dynamic` space by content stays out.** Phase 1 needs
  the user to rebind a minted space onto a declared one (§Minting and
  rebinding). The automatic form is one short byte pattern per space, at a
  stated offset: when code first executes in an unbound segment, compare memory
  against the candidate patterns and bind the one that matches. The producer
  already has the bytes, and the rule that a `dynamic` space carries no
  relocations (§Importing a producer's blocks) is what makes them comparable.
  Because it reads memory and not the file, it works for a compressed module,
  which the file link cannot. The same mechanism also detects a **stale**
  binding — compare memory at the stored number against the pattern — which is
  the only way to detect one at all, so both problems are answered by one field.
  Distinct from the per-fact byte anchor below: one anchor per space,
  identifying the space, not gating a fact. Deferred because a rebind is a
  one-time action per launch configuration, so the manual form is not painful.

- **Differential detection of a minted space's kind stays out.** Minting picks
  `absolute` or entry-relative from the reserved device-base table, which one
  observation cannot verify: a constant segment and one computed from the load
  base produce the same number (§Minting and rebinding). Two runs with different
  `--ProgramEntryPointSegment` settle every minted kind at once — an absolute
  space's observed number does not move and an entry-relative one's does.
  Deferred because a wrong kind is repaired by editing one line in
  `addressSpaces`, with no fact re-keyed, and the possibly-stale report already
  points at it.

- **Relocating image-space bytes in a converter stays out.** It is mechanically
  easy, since the MZ reloc table is a list of words to rebase, but it would make
  converter output binding-specific, breaking the invariant that numbers live
  only in `addressSpaces` and `bindings`. Decode-from-memory has no such
  problem.

- **A producer's derived segment resolution stays out.** A producer resolves
  `[disp]` from its own dataflow, not from its `assume` annotations alone. chani
  reads the MZ relocation table to learn which immediates are segment
  paragraphs, then carries those values per instruction. It queries that result
  first and reads the annotations only as a fallback. The derived result covers
  code that never ran, which is the case §Segment defaults exists for. A
  whole-space default cannot state it. The format needs no change to hold it,
  because a default is already range-scoped per register. The gap is production,
  not expression. Deriving the result needs a decoder and a CFG, so only the
  producer can emit it. Changing a producer to serve the conversion is a
  non-goal. The residual is also narrower than it looks. Spice86 reads the same
  relocation table (`DosExeFile.RelocationTable`) and applies the fixups, so it
  builds the same paragraph-to-space map itself. SQ1's observed values already
  beat a static dataflow for code that ran. Only the never-executed part is
  left.

- **Operand-index annotation stays out.** Producers can annotate operand N of an
  instruction. The format has no operand index and does not need one for the
  annotation that appears in volume — chani's `ofs_seg`, "this immediate is an
  offset into X" — which is carried as an offset pin keyed by the constant's own
  address (§Offset pins). That key is finer than an operand index, matches the
  generator's AST, and cannot be ambiguous. What stays out is annotating an
  operand that is *not* a constant in the instruction stream (a register operand,
  say), which has no address of its own to key on. `assertions` already covers
  the register case at instruction granularity, so nothing concrete is waiting on
  this.

- **Path-sensitive facts stay out.** An assertion holds at its program point on
  every path that reaches it (§When an assertion holds), so a fact true of only
  one predecessor cannot be stated in general. `when: post` covers the common
  case — the fact holds on the path through *this* instruction — but a fact
  qualified by an arbitrary predecessor needs both a qualifier on the fact and a
  path-sensitive consumer. Fail-safe in the meantime: an over-applied type
  yields a misleading name, never divergent behavior.

- **A per-fact byte anchor stays out.** It would put expected bytes on an
  addressed fact, for stale-import detection and variant disambiguation. The
  graph artifact's per-node signatures cover it for a producer that emits
  blocks. A blockless conversion has no such check, and its residual protection
  is the speculative guard at run time, which turns a stale fact into a rejected
  node rather than a wrong one. For `targets` an anchor would also be actively
  harmful: a patched branch never matches the bytes the producer decoded, so
  anchoring would drop exactly the annotation it was meant to protect (§Branch
  targets). Worth revisiting alongside overlays.

- **Honouring calling conventions in generated code stays out.** Phase 1 stores
  `signature.convention` and emits it as a comment.
