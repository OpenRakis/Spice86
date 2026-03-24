# Spice86 vs Dosbox-Staging: Rendering Performance Analysis

## Context

In Spice86 sync mode, profiling revealed rendering consumes up to **25% of the emulation loop time**. This document compares the Spice86 and dosbox-staging VGA rendering pipelines to identify why dosbox-staging is faster.

---

## 1. No Line-Level Change Detection (Biggest Win)

**Estimated impact: 10–30× on static/mostly-static frames**

Dosbox-staging caches every rendered scanline and uses `memcmp` to compare against the previous frame (`render.cpp:123`):

```cpp
if (std::memcmp(src_line_data, render.scale.cache_read, render.scale.cache_pitch) != 0) {
    // Only then: trigger scaler + GPU texture upload
}
```

If a scanline hasn't changed, **all downstream work is skipped** (scaling, format conversion, GPU upload). For most DOS games (menus, RPGs, adventures, strategy), 80–95% of scanlines are unchanged frame-to-frame.

Spice86 has **zero change detection**. `RenderScanline()` unconditionally rasterizes every scanline into `_backBuffer` every frame, and `CompleteFrame()` always swaps the entire buffer.

---

## 2. Memory Access Pattern: Direct Pointer vs. 2D Array Indexing

**Estimated impact: 2–4× per pixel access**

Dosbox-staging reads VGA memory as a flat `uint8_t*` with pointer arithmetic:

```cpp
const auto palette_index = *(linear_addr + masked_pos);
*line_addr++ = *(palette_map + palette_index);
```

Spice86 uses a C# 2D array `_memory.Planes[planeIndex, physicalAddress]` which involves:
- Bounds-checking on **two dimensions** per access
- Array base + `(row × stride + col)` index computation
- **4 separate 2D array accesses per character** (one per plane), even when the mode only needs 1 (e.g., 256-color mode)

---

## 3. Palette Lookup: Static LUT vs. Multi-Level Indirection

**Estimated impact: 1.5–3× per pixel**

Dosbox-staging pre-computes `vga.dac.palette_map[256]` as BGRX32 values at DAC write time. The scanline hot loop is a single array dereference:

```cpp
*line_addr++ = *(palette_map + palette_index);
```

Spice86's `GetDacPaletteColor()` does per-pixel:
- A `switch` on `PixelWidth8` (property access through state objects)
- In the non-8-bit path: attribute register lookup, 3 bitfield extractions, a conditional on `VideoOutput45Select`, a `PixelMask` AND, then the final palette read
- Each step traverses a chain like `_state.AttributeControllerRegisters.AttributeControllerModeRegister.PixelWidth8` — multiple object dereferences per pixel

---

## 4. Per-Scanline Scheduling Overhead

**Estimated impact: 5–10% frame overhead**

Spice86 fires **two scheduler events per scanline** (`OnScanlineActive` + `OnScanlineHBlank`) through `DeviceScheduler.AddEvent()`. For a 480-line frame, that's ~960 event enqueue/dequeue operations per frame on the emulation thread.

Dosbox-staging uses a single `PIC_AddEvent` per scanline (just the line draw). The hblank transition is implicit.

---

## 5. Batch Processing vs. Character-by-Character

**Estimated impact: 1.3–1.5× for 256-color mode**

Dosbox-staging's 256-color hot loop processes **4 pixels per inner iteration** to help the CPU pipeline:

```cpp
constexpr auto num_repeats = 4;
while (repeats--) {
    *line_addr++ = *(palette_map + *(linear_addr + (linear_pos++ & linear_mask)));
}
```

Spice86 processes **1 character at a time** with branching per character (blanking checks, mode dispatch via `if/else if/else`), plus the 4-plane read overhead.

---

## 6. 256-Color Mode: 4 Plane Reads vs. Linear Read

**Estimated impact: ~2–3× for mode 13h (320×200×256)**

In 256-color mode, dosbox-staging treats VRAM as a flat linear buffer and reads **1 byte per pixel** directly.

Spice86 still reads **4 separate planes** via `ReadVideoMemory()` and reconstructs 4 pixel values from `(plane0, plane1, plane2, plane3)`, because it uses the same generic plane-read path for all modes.

---

## 7. Mode Dispatch: Function Pointer vs. Runtime Branching

**Estimated impact: 1.2–2× per character**

Dosbox-staging assigns a mode-specific function pointer (`VGA_DrawLine`) once at mode-set time. The per-scanline call has zero branching:

```cpp
uint8_t* data = VGA_DrawLine(vga.draw.address, vga.draw.address_line);
```

Spice86 checks `In256ColorMode`, `GraphicsMode`, and `ShiftRegisterMode` **every character, every scanline** through property accessors on nested state objects.

---

## Summary Table

| Factor | Est. Impact | Spice86 | Dosbox-staging |
|--------|-------------|---------|----------------|
| **No line caching** | 10–30× on static frames | Re-renders everything | `memcmp` skip unchanged lines |
| **2D array plane reads** | 2–4× per pixel | `Planes[plane, addr]` × 4 | Flat pointer `*(base + offset)` |
| **Palette indirection** | 1.5–3× per pixel | Multi-level property chain | Single `palette_map[idx]` deref |
| **Mode dispatch per char** | 1.2–2× | `if/else` + property reads every char | Function pointer set once |
| **Double scheduler events** | 5–10% frame overhead | 2 events/scanline | 1 event/scanline |
| **No batching** | 1.3–1.5× for 256-color | 1 char at a time | 4 pixels per iteration |

---

## Key Takeaway

The **line caching** is by far the single biggest differentiator. For a game that only updates 10% of the screen per frame, dosbox skips 90% of rendering work. Spice86 always does 100%. Addressing this alone would likely cut the 25% rendering overhead dramatically for most workloads.

After that, the flat memory layout and pre-computed palette LUT would give the most return for implementation effort in the per-pixel hot path.
