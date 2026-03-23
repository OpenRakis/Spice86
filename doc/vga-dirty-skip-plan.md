# VGA Dirty-Skip Optimization Plan

## Goal

Eliminate expensive per-scanline pixel conversion and unnecessary front-buffer copies when nothing in the VGA state has changed since the last published frame.

Two coupled optimizations:

1. **Scanline skip** — in `Renderer.RenderScanline()`, skip pixel drawing (VRAM reads + Draw\* calls) when no VRAM or register change has occurred since the last complete frame. Internal state (scanline counters, address registers, line compare, vertical blanking flags) must still advance exactly as if rendering happened, so emulated hardware behaviour is preserved. If a write occurs mid-frame, skipping stops immediately for all remaining scanlines of that frame.

2. **Render copy skip** — in `Renderer.Render()`, skip the `_renderBuffer → output framebuffer` copy when no new frame was published (i.e., all scanlines of the previous VGA frame were skipped).

---

## Dirty-State Sources

Three independent sources can make a frame different from the previous one:

| Source | Change type | Current code path |
|---|---|---|
| VRAM | Any write to `VideoMemory.Planes[,]` | `VideoMemory.Write()` → one of four `HandleWriteMode*()` methods |
| VGA registers | Any port write in `VgaIoPortHandler.WriteByte()` | Attribute, CRT, sequencer, graphics, DAC, MiscOutput registers |
| Blink state | Blink phase toggle | `VgaTimingEngine.OnBlinkToggle()` sets `VgaBlinkState.IsBlinkPhaseHigh` |

All three sources run on the **emulation thread**. `Renderer.BeginFrame()`, `RenderScanline()`, and `CompleteFrame()` also run on the emulation thread. No cross-thread synchronisation is needed for the dirty flags themselves.

---

## Proposed Design

### 1. `VideoMemory` — VRAM dirty flag

Add `bool HasChanged { get; private set; }` and `ResetChanged()`. The crucial design decision is **where** to set it — see "VRAM Write Comparison: Efficiency Analysis" below for the full rationale.

**The flag must only be set when a plane byte actually changes value.** Setting it unconditionally on every `Write()` call — e.g., before the write-mode switch — defeats the optimization entirely for the common game pattern of copying an identical buffer every frame.

The comparison must be applied at the final `Planes[plane, offset] = tempValue` assignment inside each `HandleWriteMode*()` method, after all transformations (rotate, set/reset, ALU, bitmask) have been applied:

```csharp
// VideoMemory.cs (new members)
public bool HasChanged { get; private set; }
public void ResetChanged() => HasChanged = false;

// Pattern applied at every Planes[plane, offset] = tempValue site
// in HandleWriteMode0/1/2/3:
if (Planes[plane, offset] != tempValue) {
    Planes[plane, offset] = tempValue;
    HasChanged = true;
}
```

This pattern replaces the bare assignment in all four write-mode methods. Comparing the *final transformed* value (not the raw CPU-supplied byte) ensures correctness when games use ALU functions, bitmasks, or set/reset logic that happen to produce the same output byte.

> **HandleWriteMode1 detail** — `HandleWriteMode1` assigns `_latches[plane]` directly without a `tempValue` local. The implementer must introduce one:
> ```csharp
> byte tempValue = _latches[plane];
> if (Planes[plane, offset] != tempValue) {
>     Planes[plane, offset] = tempValue;
>     HasChanged = true;
> }
> ```

> **Note** — The read path (`Read()`) updates `_latches[]` as a side-effect but does not change pixel data visible on screen. Do **not** set `HasChanged` there.

### 2. `IVideoState` / `VideoState` — Register dirty flag

Add `bool IsRenderingDirty { get; set; }` to `IVideoState` and implement it in `VideoState`.

`VgaIoPortHandler.WriteByte()` sets this to `true` for any port write that can affect rendering. For safety the flag is set unconditionally at the top of `WriteByte()` (before the switch), covering address-register writes, data-register writes, DAC writes, MiscOutput, etc. Status-register writes from the timing engine go through `GeneralRegisters.InputStatusRegister1` directly, not through `VgaIoPortHandler.WriteByte()`, so they are never marked dirty here. Reads in `ReadByte()` must **not** set this flag.

`Renderer.BeginFrame()` resets `IsRenderingDirty = false` after reading it.

```csharp
// VgaIoPortHandler.WriteByte() — first line of the method body:
videoState.IsRenderingDirty = true;
```

> **Granularity note** — Address-register writes (e.g., `GraphicsControllerAddress`, `SequencerAddress`) are conservative — they don't directly change what is rendered. Including them avoids any risk of a missed dirty case with minimal impact, since register writes are rare during steady-state animation.

### 3. `VgaBlinkState` — Blink dirty flag

Add `bool HasChanged { get; private set; }` and `MarkChanged()`.  
`VgaTimingEngine.OnBlinkToggle()` calls `_blinkState.MarkChanged()`.  
`Renderer.BeginFrame()` reads and resets it.

```csharp
// VgaBlinkState.cs (new members)
public bool HasChanged { get; private set; }
public void MarkChanged() => HasChanged = true;
public void ResetChanged() => HasChanged = false;
```

### 4. `Renderer` — Core skip logic

#### New fields

```csharp
// True for the current frame if nothing is dirty at BeginFrame
// and no mid-frame write has yet been detected.
private bool _skipThisFrame;

// Number of output rows whose content would be in _frontBuffer
// (correctly reflecting what was last published). Used to recompute
// _frameDestinationAddressLatch if skip mode is exited mid-frame.
private int _frameOutputRows;
```

#### `BeginFrame()` changes

```
isDirty ← _memory.HasChanged || _state.IsRenderingDirty || _blinkState.HasChanged
_memory.ResetChanged()
_state.IsRenderingDirty ← false
_blinkState.ResetChanged()

_skipThisFrame ← !isDirty
_frameOutputRows ← 0

// Rest of existing BeginFrame logic unchanged (register latching, buffer allocation, etc.)
```

When `_skipThisFrame` is `true` the back buffer may have stale data from a previous use. It is intentionally left uninitialized because `CompleteFrame()` will not swap it.

#### `RenderScanline()` changes

At the top of the method, after the existing `!_frameActive` guard, check for a mid-frame write:

```
if (_skipThisFrame && (_memory.HasChanged || _state.IsRenderingDirty)) {
    _skipThisFrame ← false
    // Copy the last published frame into the back buffer to preserve
    // all rows that were skipped before this write.
    InitBackBufferFromFront()
    // Recompute the destination latch for the current scanline.
    _frameDestinationAddressLatch ← _frameOutputRows * Width
    _frameDestinationAddress ← _frameDestinationAddressLatch
}
```

The pixel-drawing block (the `for (int characterCounter …)` loop and its `Draw*` / `ReadVideoMemory` calls) is guarded:

```
if (!_skipThisFrame) {
    // existing per-character loop
}
```

All state-advancement code that follows the loop (line compare reset, vertical blanking, `_frameLineCounter`, double-scan index, char-row advance, `InitCharRow()`) runs **unconditionally** in both skip and non-skip paths.

`_frameOutputRows` is incremented at the point where `_frameDoubleScanIndex` wraps back to `0` (i.e., one complete output row has been emitted), **but only when not in vertical blanking**:

```csharp
// Inside the existing double-scan wrap block:
_frameDoubleScanIndex++;
if (_frameDoubleScanIndex >= _frameDrawLinesPerScanLine) {
    _frameDoubleScanIndex = 0;
    if (!_frameVerticalBlanking) {
        _frameOutputRows++;        // NEW
    }
    _frameCharRowScanline++;
    // … existing logic …
}
```

#### `CompleteFrame()` changes

```
if (_skipThisFrame) {
    _frameActive ← false
    return       // Do not swap front/back, do not signal _hasPendingFrame
}
// existing swap + Volatile.Write(_hasPendingFrame, 1)
```

#### `Render()` changes

When no new frame was published (`_hasPendingFrame == 0`), skip the copy entirely. The Avalonia `WriteableBitmap` retains its previous pixel content between `Lock()` calls, so the display continues showing the last published frame unchanged.

```csharp
public void Render(Span<uint> frameBuffer) {
    if (Interlocked.Exchange(ref _hasPendingFrame, 0) == 0) {
        return;   // No new frame — output buffer retains previous content
    }
    _renderBuffer = Interlocked.Exchange(ref _frontBuffer, _renderBuffer);
    uint[] render = Volatile.Read(ref _renderBuffer);
    if (render.Length > 0 && render.Length <= frameBuffer.Length) {
        render.AsSpan().CopyTo(frameBuffer);
    }
}
```

#### `InitBackBufferFromFront()` — new private method

```csharp
private void InitBackBufferFromFront() {
    uint[] front = Volatile.Read(ref _frontBuffer);
    if (front.Length == _backBuffer.Length) {
        front.AsSpan().CopyTo(_backBuffer.AsSpan());
    }
    // Length mismatch (first ever frame, or resolution change in the same frame):
    // leave _backBuffer as-is; the dirty scanlines will overwrite from the
    // current output row, and earlier rows will be black (zero-initialized).
}
```

Thread safety: the UI thread (`Render()`) swaps `_frontBuffer` and `_renderBuffer` references atomically but never writes to array contents. `InitBackBufferFromFront()` runs on the emulation thread, captures a snapshot reference, and reads its content — which is safe because the captured array is never mutated concurrently.

---

## Frame Lifecycle Walkthrough

### Case A — Nothing changed (static screen)

```
BeginFrame()     isDirty=false → _skipThisFrame=true, reset dirty flags
RenderScanline() × N  skip pixel drawing, advance all state, _frameOutputRows advances
CompleteFrame()  _skipThisFrame=true → return early, no swap, _hasPendingFrame stays 0
Render()         _hasPendingFrame==0 → return immediately, bitmap retains previous content
```

Result: zero VRAM reads, zero pixel conversions, zero buffer copies per frame.

### Case B — Dirty before frame start (typical animation)

```
BeginFrame()     isDirty=true → _skipThisFrame=false, reset dirty flags
RenderScanline() × N  normal pixel drawing
CompleteFrame()  swap back↔front, _hasPendingFrame=1
Render()         _hasPendingFrame==1 → swap front↔render, copy to output bitmap
```

Result: identical to current behaviour.

### Case C — Mid-frame write (program writes VRAM/registers during active display)

```
BeginFrame()        isDirty=false (since last frame) → _skipThisFrame=true
RenderScanline() 0..K-1    skipped (state still advances, _frameOutputRows increments)
<CPU writes VRAM or register>  VideoMemory.HasChanged or IsRenderingDirty set
RenderScanline() K  detects mid-frame dirty:
                    _skipThisFrame=false
                    InitBackBufferFromFront() → fill rows 0..K-1 from last published frame
                    recompute _frameDestinationAddressLatch = _frameOutputRows * Width
                    render row K normally
RenderScanline() K+1..N-1    render normally
CompleteFrame()     swap back↔front, _hasPendingFrame=1
Render()            copy to output bitmap
```

Result: rows 0..K-1 come from the previous published frame (correct, unchanged); rows K..N-1 are freshly rendered. The published composite frame is visually correct.

---

## Impact Analysis

### Correctness

- **Internal state fidelity**: All scanline-level state (`_frameLineCounter`, `_frameCharRowScanline`, `_frameDoubleScanIndex`, `_frameRowMemoryAddressCounter`, `_frameVerticalBlanking`, `InitCharRow()` calls) advances every `RenderScanline()` call regardless of skip mode. From the emulated program's perspective the VGA beam is functioning normally.

- **Line compare**: `_frameRowMemoryAddressCounter` reset at `LineCompareValue` happens in the unconditional state path. ✓

- **Vertical blanking flag**: Set at `_frameVerticalDisplayEnd` in the unconditional path. ✓

- **`InputStatusRegister1`**: Written by `VgaTimingEngine` directly on `GeneralRegisters`, not via `VgaIoPortHandler.WriteByte()`. Not marked as rendering-dirty. ✓

- **Mid-frame blink gap**: The mid-frame dirty check (`_memory.HasChanged || _state.IsRenderingDirty`) does not include `_blinkState.HasChanged`. A blink toggle that fires between `BeginFrame()` and the triggered scanline will not exit skip mode mid-frame; the blink change is caught at the next `BeginFrame()`. This is acceptable because the blink period (~500 ms) is orders of magnitude longer than a frame (~16 ms), making mid-frame blink transitions exceedingly rare.

- **Double-scan mode**: `_frameOutputRows` is incremented only when `_frameDoubleScanIndex` wraps AND not in vertical blanking — matching exactly when `_frameDestinationAddressLatch` advances in normal rendering. ✓

- **Resolution change mid-frame (pathological)**: A mode switch (CRT register write) while a frame is in progress sets `IsRenderingDirty`. Skip mode exits. `InitBackBufferFromFront()` finds a length mismatch (new back buffer already allocated in `BeginFrame()`); copy is skipped gracefully; early rows render black. This is acceptable — a mode switch mid-frame is inherently a corrupt display state on real hardware too.

- **First frame after startup**: Dirty flags default to `false`. At `BeginFrame()` for frame 0, `_skipThisFrame = true`. The BIOS writes VRAM before the first frame's scanlines begin (or between early scanlines), setting `VideoMemory.HasChanged`. Mid-frame skip exit triggers, `InitBackBufferFromFront()` finds `_frontBuffer = Array.Empty<uint>()` (length 0 ≠ back buffer), copy is skipped gracefully. Scanlines from the first dirty one onward render normally. ✓

### Performance (steady-state static screen)

- VRAM reads eliminated: ~80 characters × N scanlines per frame.
- Pixel conversions eliminated: same magnitude.
- Buffer swap in `CompleteFrame()`: skipped.
- `Render()` copy (~320×200 to ~1920×1080 uint array): skipped every UI tick.

### Performance (active animation)

- Zero overhead for dirty frames: the only additions are two `bool` field reads at the top of `RenderScanline()` and the `_frameOutputRows++` increment at double-scan wrap — both branch-predictably cheap.

### Thread safety

All dirty flags (`VideoMemory.HasChanged`, `IVideoState.IsRenderingDirty`, `VgaBlinkState.HasChanged`) are read and written exclusively on the emulation thread. No synchronisation primitives are needed.

`InitBackBufferFromFront()` uses `Volatile.Read(ref _frontBuffer)` to capture a snapshot of the front-buffer reference. Array contents are only written by the emulation thread; the UI thread swaps references atomically but never mutates array data. The read is safe.

`Render()` already uses `Interlocked.Exchange` for `_hasPendingFrame` and `_frontBuffer`/`_renderBuffer` swaps. The new early-return path does not touch these in any new way.

### Interaction with the async rendering mode described in `rendering-mode-plan.md`

The dirty-skip optimization is mode-agnostic: it operates entirely within `Renderer` and the dirty-flag producers. Whether `VgaTimingEngine` runs on the emulation thread (sync) or a UI-side scheduler (async), the same logic applies. No changes to `VgaTimingEngine`.

### `HeadlessGui` path

`HeadlessGui.DrawScreen()` routes through the same `RenderScreen` → `VgaCard.Render()` → `Renderer.Render()` chain. If no new frame was published, `Render()` returns early and `_pixelBuffer` retains its previous content. Headless tests that capture frames by polling the pixel buffer will continue to see the last published frame content — which is correct behaviour for a static screen.

---

---

## VRAM Write Comparison: Efficiency Analysis

### The Problem

Most DOS games use a double-buffering pattern: they unconditionally blit a full off-screen buffer to VRAM every frame, regardless of whether any pixel changed. A naive dirty flag (`HasChanged = true` on every `Write()` call) is always `true`, so the scanline skip never fires. The optimization becomes useless for the exact workload it is most needed for.

The solution is to compare the final computed byte against the existing plane value and only set `HasChanged` when they differ. This section analyses why that comparison is cheap.

### Why Read-Before-Write Is (Almost) Free

Modern x86 CPUs implement a **write-allocate** cache policy: when a store instruction targets an address not currently in cache, the CPU issues a **Read For Ownership (RFO)** to load the cache line before the write commits. This means the hardware always reads before it writes — the application just does not see it. An explicit application-level read before the store therefore adds **no additional cache miss** when the cache line is not present. When the cache line is already hot (as VRAM almost always is during a game's blit loop), the read is a single L1/L2 hit with sub-nanosecond latency.

In C#, `Planes[plane, offset]` is a managed 2D array. The JIT already emits a bounds check and computes the linear index `plane * 65536 + offset` for the write. The added read uses identical index expressions; the JIT's redundant-bounds-check elimination removes the duplicate check, leaving only the extra load instruction.

### Branch Predictability

In the static-frame case the comparison `Planes[plane, offset] != tempValue` evaluates to `false` for every single byte written. Modern branch predictors saturate their training tables within a handful of iterations and predict an always-not-taken branch with near-100% accuracy. After warmup the misprediction rate rounds to zero and the branch contributes one cycle of throughput cost — indistinguishable from noise.

In the changing-frame case the branch is `true` for every written byte, equally predictable in the opposite direction.

### Cost Estimate for a Full-Screen Blit

Mode X (planar 320×200): 320×200/4 = 16 000 bytes per plane × 4 planes = 64 000 plane bytes written per frame.

With the compare-before-write pattern, each byte write becomes: one load + one compare + one conditional store. The cache lines covering 64 000 bytes of `Planes` are reused across the blit loop and reside in L1/L2. At 4 GHz with 64-byte cache lines and 4 bytes of throughput: the comparison load for 64 KB of sequential plane memory falls well within L1/L2 bandwidth and adds roughly 5–15 μs per frame — well under 0.1 % of a 16.7 ms frame budget.

Mode 13h (chained 320×200): `DecodeWriteAddress` maps each CPU write to a single plane, so only one plane byte is touched per `Write()` call. The same analysis applies.

### Alternatives Considered and Rejected

#### Snapshot compare at `BeginFrame()`

Keep a 256 KB snapshot of `Planes` from the previous frame. At `BeginFrame()`, compare current vs snapshot using `MemoryMarshal` + `Span<T>.SequenceEqual` (SIMD-accelerated, ~256 KB in ~50–100 μs on an L2-warm comparison). If equal, `_skipThisFrame = true`.

**Rejected because**: it requires an extra 256 KB allocation; the snapshot must be updated every rendered frame; and it cannot detect mid-frame writes without retaining per-write tracking anyway — the mid-frame write exit path in `RenderScanline()` (`_memory.HasChanged`) still needs the per-write flag. So this approach adds cost without eliminating the need for the compare-before-write pattern.

#### Chunk-dirty bitmap

Divide the plane address space into N equal chunks (e.g., 64-byte chunks → 1024 chunks per plane × 4 planes = a 512-bit / 64-byte bitmap). On any write, set the bit for the target chunk. At `BeginFrame()`, test the bitmap.

**Rejected because**: setting a chunk dirty on any write — without comparing the value — still fires for same-value blits. A chunk is only meaningful if combined with read-before-write at the chunk boundary, which reduces to the same cost as byte-level compare-before-write but with extra bookkeeping.

#### OS-assisted write protection (`mprotect` / `VirtualProtect`)

Mark VRAM pages read-only; catch page-fault traps on write; set dirty bits in the fault handler.

**Rejected because**: crossing managed/unmanaged boundaries for every write fault is orders of magnitude more expensive than a single byte comparison; it is not portable across Linux/Windows/macOS; and it cannot be safely combined with the C# GC and managed heap.

### Summary

Compare-before-write at the `Planes[plane, offset]` assignment sites is the correct and efficient approach:

| Approach | Per-write overhead | Extra memory | Handles mid-frame | Same-value detection |
|---|---|---|---|---|
| Naive always-dirty | ~0 | 0 | N/A (always dirty) | No |
| **Compare-before-write (chosen)** | **~1 cache-hot load + compare** | **0** | **Yes** | **Yes** |
| Snapshot at BeginFrame | ~0 per write | 256 KB | No (still needs flag) | Yes (but delayed) |
| Chunk-dirty bitmap | ~1 bit set | ~64 B | Yes | No (without compare) |
| mprotect page faults | Syscall + fault handler | 0 | Yes | Yes |

---

## Files to Modify

| File | Change summary |
|---|---|
| `VideoMemory.cs` | Add `HasChanged` property and `ResetChanged()`; replace every bare `Planes[plane, offset] = tempValue` assignment in `HandleWriteMode0/1/2/3()` with the read-before-write compare pattern (§1 and Efficiency Analysis); `HandleWriteMode1` requires introducing a `tempValue` local for `_latches[plane]` |
| `IVideoState.cs` | Add `bool IsRenderingDirty { get; set; }` |
| `VideoState.cs` | Auto-implement `IsRenderingDirty` |
| `VgaBlinkState.cs` | Add `HasChanged` property, `MarkChanged()`, `ResetChanged()` |
| `VgaIoPortHandler.cs` | Set `_videoState.IsRenderingDirty = true` at top of `WriteByte()` |
| `VgaTimingEngine.cs` | Call `_blinkState.MarkChanged()` in `OnBlinkToggle()` |
| `Renderer.cs` | Add `_skipThisFrame`, `_frameOutputRows` fields; update `BeginFrame()`, `RenderScanline()`, `CompleteFrame()`, `Render()`; add `InitBackBufferFromFront()` |

`IVgaRenderer.cs`, `VgaCard.cs`, `MainWindowViewModel.cs`, `HeadlessGui.cs` require **no changes**.

---

## Non-Goals / Out of Scope

- Skipping the `InvalidateBitmap` call in `MainWindowViewModel.DrawScreen()` when no new frame was published. This would require changing the `IVgaRenderer.Render()` return type and is a separate concern.
- Tracking which specific VRAM addresses or register fields changed (sub-dirty tracking). The current frame-granularity dirty flag is sufficient and avoids complexity.
- Restoring individual skipped scanline regions from the front buffer (only the full-frame copy on mid-frame exit is done). Partial-row tracking adds complexity with minimal gain.
