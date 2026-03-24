# VGA Rendering Performance Plan

Targets items 2 (memory access), 5 (batch processing), 6 (256-color linear read), and SIMD from the analysis in
`rendering-performance-analysis.md`. Item 1 (line-level dirty skip) is already implemented. Item 3 (palette LUT) is
already implemented (`DacRegisters.PaletteMap` / `AttributeMap`). Item 4 (per-scanline scheduling overhead) and item 7
(mode dispatch branching) are out of scope for now.

---

## Step 1 — Single Flat Interleaved VRAM Array

### Why

The current `byte[,] Planes` 2D array in `VideoMemory` causes:
- Double-dimension bounds checking on every access (`Planes[plane, addr]`).
- No way to obtain a `Span<byte>` or `ReadOnlySpan<byte>` over a plane or a contiguous pixel range.
- The renderer must make 4 separate indexed array reads per character clock even in 256-color mode.
- SIMD intrinsics require a contiguous byte buffer; a 2D array cannot be pinned or passed to intrinsic load
  functions without unsafe gymnastics.

### Layout

Dosbox-staging uses a single flat buffer (`vga.mem.linear`, a `uint8_t*`) where the 4 planes are interleaved at
byte granularity, one 32-bit word per VGA address:

```
offset:   0      1      2      3      4      5      6      7   ...
content: p0[0] p1[0] p2[0] p3[0] p0[1] p1[1] p2[1] p3[1] ...
```

This means:
- **Plane `p` at VGA address `a`** → `linear[a * 4 + p]`
- **All 4 planes at address `a`** → `((uint32_t*)linear)[a]` (single 32-bit load, holds `p0|p1|p2|p3`)
- **Mode 13h chain-4 pixel N** → `linear[N]` directly (no transformation: plane = `N & 3`, offset = `N >> 2`,
  so `(N >> 2) * 4 + (N & 3) = N`)

Spice86 will adopt the same layout.

### Changes

**`VideoMemory.cs`**

Replace the old 2D array with a single flat interleaved buffer and a lightweight accessor that preserves
the existing `Planes[plane, address]` calling syntax. This keeps callers unchanged while providing a
contiguous `VRam` for bulk/SIMD operations.

```csharp
// Single flat interleaved buffer: plane p at VGA address a = VRam[a * 4 + p].
// All 4 planes at address a as a 32-bit word: MemoryMarshal.Cast<byte, uint>(VRam)[a].
// Mode 13h chain-4 pixel N: VRam[N] directly.
public byte[] VRam { get; } = new byte[4 * 64 * 1024];

// Preserve the `Planes[p, a]` syntax by exposing an accessor object with a two-parameter indexer
// that forwards to the flat `VRam` buffer. Callers can continue to use `Planes[plane, address]`.
public PlaneAccessor Planes { get; }

// Implement Planes as a small nested accessor type that is constructed with the owning `VRam` buffer.
// This avoids confusing placeholder code and shows a real, copy-paste-friendly implementation.
public readonly struct PlaneAccessor
{
    private readonly byte[] _vram;

    internal PlaneAccessor(byte[] vram)
    {
        _vram = vram;
    }

    public byte this[int plane, int address]
    {
        get => _vram[address * 4 + plane];
        set => _vram[address * 4 + plane] = value;
    }
}

// Example `VideoMemory` constructor wiring:
// public VideoMemory() {
//     VRam = new byte[4 * 64 * 1024];
//     Planes = new PlaneAccessor(VRam);
// }
```

Update `WriteValue`:
```csharp
private void WriteValue(int plane, uint offset, byte value) {
    int idx = (int)offset * 4 + plane;
    if (VRam[idx] != value) {
        VRam[idx] = value;
        HasChanged = true;
    }
}
```

Update latch loads in `Read` / `Write`:
```csharp
// Load all 4 plane latches in one 32-bit read
uint word = MemoryMarshal.Cast<byte, uint>(VRam.AsSpan())[(int)offset];
_latches[0] = (byte)word;
_latches[1] = (byte)(word >> 8);
_latches[2] = (byte)(word >> 16);
_latches[3] = (byte)(word >> 24);
```

**`IVideoMemory.cs`**

Keep compatibility by exposing the raw `VRam` and the `Planes` accessor (indexer) so callers can
continue to use the `Planes[plane, address]` syntax.

```csharp
public interface IVideoMemory : IMemoryDevice {
    /// <summary>
    /// Raw interleaved VRAM buffer. Plane p at VGA address a = VRam[a * 4 + p].
    /// Mode 13h chain-4 pixel N = VRam[N].
    /// </summary>
    byte[] VRam { get; }

    /// <summary>
    /// Accessor that preserves the `Planes[plane, address]` syntax while forwarding
    /// to the interleaved `VRam` buffer. Implemented as a small helper type with a two-parameter
    /// indexer on `VideoMemory`.
    /// </summary>
    PlaneAccessor Planes { get; }

    /// <summary>
    /// Returns a read-only span over the interleaved VRAM starting at the given linear byte offset.
    /// Use for sequential or SIMD bulk reads.
    /// </summary>
    ReadOnlySpan<byte> GetLinearSpan(int linearByteOffset, int length);
}
```

In `VideoMemory` the `PlaneAccessor` will forward to `VRam`:

```csharp
public byte PlaneIndexer(int plane, int address) => VRam[address * 4 + plane];
// PlaneAccessor will call into VideoMemory to perform reads/writes so existing call sites remain unchanged.
```

**`Renderer.cs` — ReadVideoMemory and DrawTextMode**

`ReadVideoMemory` becomes:
```csharp
private (byte plane0, byte plane1, byte plane2, byte plane3) ReadVideoMemory(int memoryAddressCounter, VgaAddressMapper addressMapper) {
    ushort physicalAddress = addressMapper.ComputeAddress(memoryAddressCounter);
    int baseIdx = physicalAddress * 4;
    byte plane0 = (byte)(_framePlanesEnabled[0] ? _memory.VRam[baseIdx]     : 0);
    byte plane1 = (byte)(_framePlanesEnabled[1] ? _memory.VRam[baseIdx + 1] : 0);
    byte plane2 = (byte)(_framePlanesEnabled[2] ? _memory.VRam[baseIdx + 2] : 0);
    byte plane3 = (byte)(_framePlanesEnabled[3] ? _memory.VRam[baseIdx + 3] : 0);
    return (plane0, plane1, plane2, plane3);
}
```

`DrawTextMode` font read (unchanged callsites using `Planes` syntax):
```csharp
// Font data is in plane 2
byte fontByte = _memory.Planes[2, fontAddress + scanline];
```

**Other consumers** (debug views, GDB memory inspector, etc.): keep using `Planes[p, a]` — the property will be
backed by the new interleaved `VRam` buffer via the `PlaneAccessor` indexer. Callers that need bulk access can
use `VRam` or `GetLinearSpan` directly.

### Validation

All existing tests must pass. No visual regressions. The per-pixel access performance should be roughly equivalent
to the old path for scalar code (Step 1 is a prerequisite, not the performance win itself); the wins come in
Steps 2–4.

---

## Step 2 — Zero-Copy Linear Read for Mode 13h

### Why

With the interleaved flat array from Step 1, mode 13h chain-4 pixel N is already at `VRam[N]` — there is no
conversion, no interleaving step, and no copy. This step updates the renderer to exploit that directly instead of
going through the 4-plane tuple path.

### Current path (characters × 4 plane reads, then 8 pixels)

```
ReadVideoMemory(addr)
 → physicalAddress = addressMapper.ComputeAddress(addr)    // 1 struct call
 → plane0 = VRam[physicalAddress * 4 + 0]                 // 4 separate reads
 → plane1 = VRam[physicalAddress * 4 + 1]
 → plane2 = VRam[physicalAddress * 4 + 2]
 → plane3 = VRam[physicalAddress * 4 + 3]
 → Draw256ColorMode(...)                                   // expand to 8 pixels
```

One outer loop iteration yields 8 pixels (4 planes × 2 pixels each) and requires 7+ operations to reconstruct them.

### New path for 256-color (inside RenderScanline's horizontal loop)

The mode check is placed as an `if / else if / else` **before** the horizontal loop begins — not in `BeginFrame`
because DOS programs can write to the VGA I/O registers between scanlines (or even mid-frame for split-screen
effects). The check is hoisted once per scanline call, which is acceptable.

```csharp
// Hoisted before the character loop — re-read each scanline since VGA state may change
// between scanlines (mid-frame mode switches, split-screen, etc.)
bool in256ColorMode = _state.GraphicsControllerRegisters.GraphicsModeRegister.In256ColorMode;
bool inGraphicsMode = _state.GraphicsControllerRegisters.MiscellaneousGraphicsRegister.GraphicsMode;

if (in256ColorMode) {
    RenderScanline256ColorLinear(frameBuffer, paletteMap, visibleStart, visibleEnd);
} else if (inGraphicsMode) {
    // existing per-character planar loop (unchanged)
} else {
    // existing text mode per-character loop (unchanged)
}
```

`RenderScanline256ColorLinear` reads the VGA linear address for the *first visible pixel* of the scanline (derived
from `memoryAddressCounter` and `addressMapper` at the first visible character), then reads the pixel run as a
contiguous span:

```csharp
private void RenderScanline256ColorLinear(Span<uint> frameBuffer, uint[] paletteMap,
    int vramLinearStart, int pixelCount) {
    ReadOnlySpan<byte> vram = _memory.GetLinearSpan(vramLinearStart, pixelCount);
    int dest = _frameDestinationAddress;
    for (int i = 0; i < pixelCount; i++) {
        frameBuffer[dest++] = paletteMap[vram[i]];
    }
    _frameDestinationAddress = dest;
}
```

`GetLinearSpan` returns `VRam.AsSpan(linearByteOffset, length)` — zero allocation, zero copy.

**Note on address computation**: for mode 13h, `VgaAddressMapper.ComputeAddress(counter)` translates the memory
address counter using the DoubleWord mode shift. The linear start byte offset into VRam is
`ComputeAddress(firstVisibleCounter) * 4` (converting VGA word address to the interleaved byte index). This must be
computed once per scanline before this new loop.

### Validation

Mode 13h games must produce pixel-for-pixel identical output. The per-scanline rendering time for 256-color mode
should drop measurably as 4 plane reads + tuple overhead per character is replaced by sequential span reads.

---

## Step 3 — Batch Processing (Loop Unrolling) for 256-Color

### Why

Processing one pixel per inner loop iteration leaves ILP (instruction-level parallelism) on the table. The CPU's
superscalar and out-of-order execution units can overlap palette lookups and memory writes if multiple independent
iterations are grouped together — the same technique dosbox uses with `num_repeats = 4`.

### Change inside `RenderScanline256ColorLinear`

```csharp
private void RenderScanline256ColorLinear(Span<uint> frameBuffer, uint[] paletteMap,
    int vramLinearStart, int pixelCount) {
    ReadOnlySpan<byte> vram = _memory.GetLinearSpan(vramLinearStart, pixelCount);
    int dest = _frameDestinationAddress;
    int i = 0;

    // Process 4 pixels per iteration; loads and stores are independent — lets the CPU pipeline deeper.
    int count4 = pixelCount & ~3;
    while (i < count4) {
        frameBuffer[dest]     = paletteMap[vram[i]];
        frameBuffer[dest + 1] = paletteMap[vram[i + 1]];
        frameBuffer[dest + 2] = paletteMap[vram[i + 2]];
        frameBuffer[dest + 3] = paletteMap[vram[i + 3]];
        dest += 4;
        i += 4;
    }
    // Handle tail (0–3 pixels)
    while (i < pixelCount) {
        frameBuffer[dest++] = paletteMap[vram[i++]];
    }
    _frameDestinationAddress = dest;
}
```

### Validation

Pixel-perfect output. Benchmark on a typical mode 13h game scanline — expect 1.3–1.5× improvement over the
one-at-a-time version.

---

## Step 4 — SIMD Rendering for 256-Color Mode

### Why

With the flat interleaved array (Step 1) and contiguous span (Step 2), the 256-color scanline hot loop is now
SIMD-ready: N consecutive bytes from VRam are N palette indices, and we need to gather N 32-bit pixels from
`paletteMap`.

The bottleneck operation is a **scatter-gather palette lookup**: read a vector of byte indices, use each as a
32-bit gather index into `paletteMap`, write the resulting uint32 pixels to the frame buffer. .NET exposes this
through `System.Runtime.Intrinsics`.

### Runtime feature detection

```csharp
private static readonly bool UseAvx2 = Avx2.IsSupported;
private static readonly bool UseSse41 = Sse41.IsSupported;
```

Selected once at class construction; the delegate (or if-chain) switches between:
1. AVX2 path — 8 pixels per gather, best throughput
2. SSE4.1 path — 4 pixels per gather, scalar-extract + SIMD store
3. Scalar fallback — Step 3 batch code

### AVX2 path (8 pixels per iteration)

```csharp
// unsafe block (required for fixed / pointer arithmetic)
fixed (byte* vramPtr = vram)
fixed (uint* destPtr = frameBuffer)
fixed (uint* palPtr = paletteMap) {
    int i = 0;
    int count8 = pixelCount & ~7;
    while (i < count8) {
        // Load 8 byte indices; widen each to int32 for use as gather indices
        // (Avx2.GatherVector256 requires int32 indices, scale = 4 for uint32 palette entries)
        Vector256<int> idx = Avx2.ConvertToVector256Int32(
            Sse2.LoadScalarVector128((long*)(vramPtr + i)).AsSByte()).AsInt32();

        // Gather 8 palette entries: palPtr[idx[j]] for j in 0..7
        Vector256<int> pixels = Avx2.GatherVector256(
            (int*)palPtr, idx, scale: 4);  // scale=4 because uint is 4 bytes

        // Store 8 uint32 pixels
        Avx2.Store((int*)(destPtr + dest), pixels);
        dest += 8;
        i += 8;
    }
    // Handle remainder with scalar batch (Step 3)
    ...
}
```

**Key properties:**
- `vpgatherdd` (the AVX2 gather) loads 8 non-contiguous 32-bit values in one instruction.
- The `scale: 4` argument accounts for `uint` element size, so `idx[j]` is treated as a byte-scaled index into
  the `uint[]` palette array.
- The output is stored with `Avx2.Store` (unaligned store, fine on modern CPUs).

### SSE4.1 path (4 pixels per iteration, no gather instruction)

SSE2/SSE4.1 lacks a native integer gather. Instead: load 4 byte indices, extract each with `Sse41.Extract`, do 4
scalar palette lookups, pack the 4 uint32 results into a `Vector128<uint>`, and store:

```csharp
while (i < count4) {
    // Load 4 bytes and widen to 4 int32 indices
    Vector128<int> idx = Sse41.ConvertToVector128Int32(
        Sse2.LoadScalarVector128((int*)(vramPtr + i)).AsSByte());

    uint p0 = palPtr[(uint)Sse2.Extract(idx.AsInt16(), 0)];
    uint p1 = palPtr[(uint)Sse2.Extract(idx.AsInt16(), 2)];
    uint p2 = palPtr[(uint)Sse2.Extract(idx.AsInt16(), 4)];
    uint p3 = palPtr[(uint)Sse2.Extract(idx.AsInt16(), 6)];

    Vector128<uint> pixels = Vector128.Create(p0, p1, p2, p3);
    Sse2.Store((uint*)(destPtr + dest), pixels);
    dest += 4;
    i += 4;
}
```

This is not a true gather but it reduces store overhead by writing 4 pixels at once with a single SIMD store.

### ARM AdvSimd path

For ARM64 (used on Apple Silicon and potentially arm64 Windows), the equivalent of AVX2 gather does not exist as a
single instruction. Use the SSE4.1-equivalent approach with AdvSimd loads and stores, or rely on the scalar batch
fallback. The scalar Step 3 path is already fast enough on ARM due to its high IPC.

### File organisation

The SIMD rendering logic is extracted to a `static` helper class (keeping `Renderer.cs` readable):

```
Renderer.cs                       — orchestration, scanline loop, mode dispatch
VgaRenderer256ColorScalar.cs      — Step 3 batch scalar (the safe fallback)
VgaRenderer256ColorSimd.cs        — Step 4 SIMD: AVX2 + SSE4.1 with unsafe
```

`Renderer.cs` selects the right implementation in the 256-color branch:

```csharp
if (UseAvx2) {
    VgaRenderer256ColorSimd.RenderScanlineAvx2(frameBuffer, _memory.VRam, paletteMap,
        vramLinearStart, pixelCount, ref _frameDestinationAddress);
} else if (UseSse41) {
    VgaRenderer256ColorSimd.RenderScanlineSse41(frameBuffer, _memory.VRam, paletteMap,
        vramLinearStart, pixelCount, ref _frameDestinationAddress);
} else {
    VgaRenderer256ColorScalar.RenderScanline(frameBuffer, _memory.VRam, paletteMap,
        vramLinearStart, pixelCount, ref _frameDestinationAddress);
}
```

### Memory pinning

The `byte[] VRam` and `uint[] paletteMap` arrays need to be pinned for the duration of the SIMD loop to prevent
GC relocation:

```csharp
fixed (byte* vramPtr = vram)          // vram is a ReadOnlySpan<byte> from GetLinearSpan
fixed (uint* palPtr = paletteMap)
fixed (uint* destPtr = frameBuffer)   // frameBuffer is Span<uint> from _backBuffer
{
    // AVX2 or SSE4.1 loop
}
```

All three spans/arrays are on the managed heap; using `fixed` inside an `unsafe` method is the correct approach.
This requires `VgaRenderer256ColorSimd.cs` to be compiled with `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in
its project (already present in `Spice86.Core`).

### Validation

Three code paths must produce pixel-for-pixel identical output:
- AVX2 result = SSE4.1 result = scalar result

Test coverage: add a test that runs the same scanline data through all three paths and compares output. Benchmark
on both x86-64 (with AVX2) and a CPU without AVX2 to verify the fallback.

---

## Step Dependency Order

```
Step 1 (flat interleaved VRam)
    │
    ├──→ Step 2 (zero-copy linear read for 256-color)
    │        │
    │        └──→ Step 3 (batch / loop unrolling)
    │                 │
    │                 └──→ Step 4 (SIMD — AVX2 + SSE4.1)
    │
    └──→ (all other modes: planar EGA, text — benefit from single-dimension
          access and contiguous latch loads, but do not get the SIMD treatment
          in this plan because they are not the hot path for most DOS games)
```

---

## Files Changed

| File | Steps |
|------|-------|
| `VideoMemory.cs` | 1 |
| `IVideoMemory.cs` | 1 |
| `Renderer.cs` | 1, 2, 3, 4 (orchestration only) |
| New: `VgaRenderer256ColorScalar.cs` | 3 |
| New: `VgaRenderer256ColorSimd.cs` | 4 |
| All other `IVideoMemory.Planes` consumers (debug views, GDB) | 1 |
