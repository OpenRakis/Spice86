# Task 24 — Binary-dependent converter duties

chani converter. The duties that need the program binaries as input.

## Source

- [chani-converter.md](../requirement/chani-converter.md): §Deriving an
  address space delta, §The launch target hash, §Data extents and segment
  tags, §Offset pin keys, §Branch target keys

## Goal

Implement the duties that read the binary image or decode instructions.
Offset pin resolution is the one duty that cannot be deferred to runtime,
so the binaries are a hard input for it.

## Deliverables

- Address space delta: parse chani's `segment load = exe[<paragraphs>]`
  and emit `entryDelta` (paragraphs, per the format).
- Launch target hash: recompute SHA256 from the binary (chani stores SHA1,
  which is not convertible). Omit the hash when the binary is unavailable;
  never emit SHA1.
- Data extents: emit a `globals` entry per typed attr and let the extent
  follow from the type. Variable-length types (`cstring`) need the binary
  to measure; reuse the producer's sizing. No bulk emission of
  unannotated regions.
- Offset pin keys: decode the instruction at the `ofs_seg` address (import
  Spice86's x86 decoder) and locate the 16-bit offset field the pin
  refers to. Exactly one candidate: emit the pin keyed by the field's
  address. Zero or two or more candidates: warn and do not emit. A run
  without binaries must say in its report that pins were not resolved,
  not appear to succeed.
- Branch target keys: when binaries are available, check the key is an
  instruction start; warn and drop otherwise (advisory; a mid-instruction
  seed never fires anyway). Without binaries, emit unchecked.

## Acceptance

- Hash of a known binary matches `sha256:<64 lowercase hex>` of its
  content; a run without the binary omits the hash.
- An ambiguous `ofs_seg` (zero or several 16-bit fields) is reported and
  not emitted; an unambiguous one lands at the field's address.
- A `cstring` global's extent matches the measured string length.

## Dependencies

- Tasks 21-23; Spice86's decoder available as a consumable package or
  source reference.
