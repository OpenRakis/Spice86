**256-color SIMD rendering bug — analysis & proof

**Summary**: After the "simd rendering" commit (bafafd4f...), some games show correct vertical rendering but horizontally exhibit a repeating pattern: 4 pixels correct then a 12-pixel gap of black/garbage. The issue only appears in some games and manifests when `Render256ColorScanline` was changed to assume VRAM bytes for visible characters are contiguous.

**Symptoms**
- Vertical scanline ordering is correct.
- Horizontally, each line shows 4 correct pixels followed by a wide gap (roughly 12 pixels) of garbage; only the left side of the screen is visible.
- Affected games call `Render256ColorScanline` like working games do, but differ in VGA memory mapping modes.

**Relevant commit**
- Commit: bafafd4f1e895d30b4bd179ef44d1454369774f8
- File: [src/Spice86.Core/Emulator/Devices/Video/Renderer.cs](src/Spice86.Core/Emulator/Devices/Video/Renderer.cs)
- Change: replaced per-character ComputeAddress + per-character VRAM reads with a bulk contiguous VRAM span read then dispatched to SIMD renderer.

**Root cause (short)**
- The new code assumed per-character physical addresses are sequential and that a single contiguous span of VRAM bytes starting from `startPhysical * 4` covers the visible characters.
- VGA address mapping (`VgaAddressMapper.ComputeAddress`) in non-Byte modes (Word13, Word15, DoubleWord) and other mode bits (scan-line bit affecting address bit13/14) produce non-sequential physical addresses for successive character counters. The previous per-character loop called `ComputeAddress` for each character and read the correct interleaved bytes; the SIMD change lost this gather behavior and read the wrong bytes for many characters.
- The observed 4px + 12px gap pattern corresponds to how the interleaved VRAM bytes map under certain memory widths (some bytes end up placed / skipped in the contiguous read), explaining why some games are unaffected (they use Byte mapping) while others fail.

**Proof-of-fix performed (what was done to verify the hypothesis)**
- A minimal patch was applied to `Render256ColorScanline` that:
  - Gathers per-character physical addresses by calling `addressMapper.ComputeAddress(currCounter)` for each visible character,
  - Copies the 4 interleaved bytes (VRam[physical*4 .. +3]) into a temporary contiguous buffer,
  - Passes that contiguous gathered buffer to the SIMD renderer (`RenderDoubledScanline`).
- Result: the graphical corruption disappeared in the failing game(s), confirming the contiguous-span assumption was the bug.
- The patch is intentionally minimal (allocates a small temporary buffer) to prove correctness; it is not optimized for performance.

**How to reproduce**
1. Checkout the branch containing the `simd rendering` commit.
2. Run a game known to fail (one that shows the 4px + 12px horizontal gaps).
3. Observe the horizontal corruption.
4. Apply the gather patch (or run the branch with the patch applied) and re-run — the line gaps should disappear.

**Diagnostic checks (quick)**
- Inspect `_frameMemoryWidthMode` and `(_frameCharRowScanline & 1)` for a failing scanline; non-`Byte` modes are suspects.
- Compare per-character addresses: for i in [0..visibleChars): compute `addressMapper.ComputeAddress(startCounter + i)` and compare with `startPhysical + i`; inequality shows non-sequential mapping.
- Dump the bytes for the first 32 character slots using per-character `ReadVideoMemory` and compare against the contiguous VRAM slice read at `startPhysical*4 + i*4`.

**Recommended performant solutions (next steps)**
1. Fast-path detection + SIMD
   - Detect whether addresses for the visible characters are contiguous (cheap loop: check `ComputeAddress(start + i) == startPhysical + i` for a few samples or all visibleChars).
   - If contiguous, call SIMD renderer with the original contiguous span (fast, no allocation).
2. Hybrid approach with pooling
   - For non-contiguous cases, gather into a temporary buffer but avoid per-frame allocations by reusing a pooled buffer (`ArrayPool<byte>.Shared.Rent(maxBytes)`) and returning it.
3. Chunked SIMD with contiguous runs
   - Scan the per-character physical address sequence for contiguous runs (length >= some threshold), call SIMD on each contiguous run, and use scalar/gather for the small remainders.
4. Vectorized gather (advanced)
   - Implement true gather using hardware intrinsics (complex, platform-dependent). Only worth it if benchmarks show bottleneck and sufficient support across targets.

**Files / symbols to review**
- `Renderer.Render256ColorScanline` — current implementation and the temporary gather patch used for proof.
- `Renderer.ReadVideoMemory` and `VgaAddressMapper.ComputeAddress` — confirm mapping behavior.
- `VideoMemory.VRam` layout (interleaved 4-plane layout) and `VideoMemory.GetLinearSpan` usage.
- `IVgaRenderer256Color.RenderDoubledScanline` implementations (scalar / SSE / AVX2) — ensure they expect contiguous palette indices.

**Suggested immediate patch (performance-safe)**
- Implement the detection of contiguous addresses first; only gather when needed. Use `ArrayPool<byte>` when gathering to avoid GC churn.

**Notes**
- The minimal gather patch proved the bug is address-mapping related rather than a SIMD renderer defect.
- Performance tuning should focus on avoiding allocations and maximizing contiguous runs to keep SIMD benefits.

---
Generated for automated/AI analysis. If you want, I can also produce a small benchmark harness to measure gather vs contiguous SIMD performance on typical failing scenes.

## Minimal gather patch (proof-of-fix)

The following C# snippet was applied as a minimal, correctness-first proof. It gathers the 4 interleaved bytes per character (using `ComputeAddress`) into a temporary contiguous buffer and then calls the existing `RenderDoubledScanline` SIMD path.

```csharp
private void Render256ColorScanline(Span<uint> frameBuffer, uint[] paletteMap,
   VgaAddressMapper addressMapper, int memoryAddressCounter) {
   int visibleChars = _frameHorizontalDisplayEnd - _frameSkew;
   if (visibleChars <= 0) {
      return;
   }

   int startCounter = memoryAddressCounter;
   for (int c = 0; c < _frameSkew; c++) {
      startCounter += (c & _frameCharacterClockMask) == 0 ? 1 : 0;
   }

   int maxBytes = visibleChars * 4;
   byte[] gathered = new byte[maxBytes];
   int gatheredBytes = 0;

   int currCounter = startCounter;
   for (int i = 0; i < visibleChars; i++) {
      ushort physical = addressMapper.ComputeAddress(currCounter);
      int baseIdx = physical * 4;
      if (baseIdx + 4 > _memory.VRam.Length) {
         break;
      }
      gathered[gatheredBytes++] = _memory.VRam[baseIdx];
      gathered[gatheredBytes++] = _memory.VRam[baseIdx + 1];
      gathered[gatheredBytes++] = _memory.VRam[baseIdx + 2];
      gathered[gatheredBytes++] = _memory.VRam[baseIdx + 3];

      int charIndex = _frameSkew + i;
      currCounter += (charIndex & _frameCharacterClockMask) == 0 ? 1 : 0;
   }

   if (gatheredBytes <= 0) {
      return;
   }

   ReadOnlySpan<byte> vram = gathered.AsSpan(0, gatheredBytes);
   int pixelCount = gatheredBytes * 2;
   if (_frameDestinationAddress + pixelCount > frameBuffer.Length) {
      pixelCount = frameBuffer.Length - _frameDestinationAddress;
   }
   if (pixelCount <= 0) {
      return;
   }
   int byteCount = pixelCount / 2;

   _renderer256Color.RenderDoubledScanline(frameBuffer, vram, paletteMap, byteCount, ref _frameDestinationAddress);
}
```

Notes:
- This version allocates a temporary `byte[]` per scanline; for production use replace it with `ArrayPool<byte>.Shared.Rent()` or implement contiguous-run fast-paths to avoid allocations.
- The patch proves the bug is in address mapping vs. contiguous VRAM assumptions and informs a performant fix strategy.
