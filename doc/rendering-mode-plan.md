# Dual Rendering Mode Plan: Sync vs Async

## Problem Statement

The current **sync** rendering model drives VGA pixel conversion from `VgaTimingEngine` events firing inside `DeviceScheduler.ProcessEvents()` on the emulation thread. Every CPU instruction cycle pays the cost of checking and executing scanline-level events (~800+ events/frame at 449 scanlines × active+hblank). Profiling confirms this penalises programs that are sensitive to raw emulation throughput.

The **previous** (pre-`3d3a2547`) code rendered entirely on the UI timer thread: `Renderer.Render()` did the full frame scan with busy-waits (`SpinWait`) to emulate horizontal line timing. This gave better performance but less deterministic VGA register state management.

**Goal:** support both modes with a run-time switch, minimal code duplication, and clear separation of concerns.

### Why Timing Approximation Won't Work

A purely clock-based flag approximation (e.g., computing `InputStatusRegister1` from `ElapsedTimeMs % frameDuration`) is insufficient because DOS programs read and write VGA registers mid-rendering and expect the effects to be visible at the correct point in the scan. The renderer must be actively progressing through scanlines — setting `DisplayDisabled`, `VerticalRetrace`, and latching registers — just like real hardware does. The register reads/writes must be interleaved with the scan progression to produce correct screen output.

### Core Insight: UI-Side Scheduler

Instead of approximating timing or creating a separate rendering path, we reuse the **exact same `VgaTimingEngine` and `DeviceScheduler`** infrastructure. The only difference between sync and async modes is **which thread calls `ProcessEvents()`** and **which clock drives scheduling**:

- **Sync:** `DeviceScheduler` lives on the emulation thread, `ProcessEvents()` is called every CPU instruction cycle, events fire at deterministic emulated times.
- **Async:** A second `DeviceScheduler` instance lives on the UI side. `ProcessEvents()` is called from the UI timer (~75Hz, see §5). Between timer ticks, events covering approximately one VGA frame accumulate and fire. Timing is non-deterministic but all the same scanline-level logic runs — register latching, flag toggling, pixel rendering — in the correct sequence.

---

## Architecture Overview

```
                        ┌──────────────────────┐
                        │   Configuration      │
                        │  RenderingMode enum  │
                        └──────┬───────────────┘
                               │
              ┌────────────────┴────────────────┐
              │                                 │
       Sync (accurate)                  Async (fast)
              │                                 │
  VgaTimingEngine on                 VgaTimingEngine on
  emulation-thread scheduler.        UI-thread scheduler.
  ProcessEvents() called every       ProcessEvents() called from
  CPU cycle in EmulationLoop.        UI timer (~75Hz, see §5).
  Deterministic timing.              Non-deterministic timing.
              │                                 │
              └────────────────┬────────────────┘
                               │
               Shared (100% identical):
               - VgaTimingEngine (same instance pattern)
               - DeviceScheduler (same class)
               - Renderer (BeginFrame/RenderScanline/
                 CompleteFrame/Render)
               - VgaBlinkState
               - All pixel conversion logic
```

---

## Detailed Design

### 1. Configuration: Add `RenderingMode` Setting

**File:** `Configuration.cs`  
**Change:** Add a CLI option `--RenderingMode` accepting `Sync` or `Async` (default: `Async` for performance).

```csharp
public enum RenderingMode { Sync, Async }

// In Configuration class:
public RenderingMode RenderingMode { get; init; } = RenderingMode.Async;
```

This keeps the system opt-in for sync accuracy when specific programs need it.

### 2. Rename `EmulationLoopScheduler` → `DeviceScheduler`

Now that the scheduler is used both on the emulation thread (for CPU-synchronous device events) and on the UI thread (for VGA rendering in async mode), the name `EmulationLoopScheduler` is misleading.

**Rename:**
- Class `EmulationLoopScheduler` → `DeviceScheduler`
- Class `EmulationLoopSchedulerMonitor` → `DeviceSchedulerMonitor`
- Folder `src/Spice86.Core/Emulator/VM/EmulationLoopScheduler/` → `src/Spice86.Core/Emulator/VM/DeviceScheduler/`
- Namespace `Spice86.Core.Emulator.VM.EmulationLoopScheduler` → `Spice86.Core.Emulator.VM.DeviceScheduler`

All existing call sites (`EmulationLoop`, audio devices, PIT, etc.) just update the type name — no behavioural change.

### 3. Renderer, VgaTimingEngine, DeviceScheduler: No Logic Changes

The entire rendering pipeline — `Renderer`, `VgaTimingEngine`, `DeviceScheduler` — has **no logic changes**. The same classes, same methods, same event chain:

```
OnFrameStart → BeginFrame
  → OnScanlineActive → RenderScanline → OnScanlineHBlank
  → ... (repeat for each scanline)
  → OnVerticalRetraceStart → CompleteFrame
  → OnFrameStart (next frame)
```

The only difference is which thread drives `ProcessEvents()` and what clock is used. This is a wiring concern, not a logic change.

### 4. Async Mode Wiring: UI-Side Scheduler

In async mode, create a **separate** `DeviceScheduler` instance dedicated to VGA rendering, and wire `VgaTimingEngine` to it instead of to the emulation-loop scheduler:

```csharp
VgaBlinkState vgaBlinkState = new();
Renderer vgaRenderer = new(memory, videoState, vgaBlinkState, loggerService);

DeviceScheduler vgaScheduler;
if (configuration.RenderingMode == RenderingMode.Sync) {
    // VGA events fire on the emulation thread, driven by emulated clock.
    vgaScheduler = emulationLoopScheduler;
} else {
    // VGA events fire on the UI thread, driven by the same clock.
    // ProcessEvents() will be called from the UI timer instead of the emulation loop.
    vgaScheduler = new DeviceScheduler(_emulatedClock, loggerService);
}

VgaTimingEngine vgaTimingEngine = new(videoState, vgaScheduler,
    _emulatedClock, vgaRenderer, vgaBlinkState);
```

**Key points:**
- `VgaTimingEngine` always exists and always schedules the same events.
- In sync mode, `vgaScheduler` is the same scheduler the emulation loop already processes → events fire per CPU cycle as today.
- In async mode, `vgaScheduler` is a new instance. The emulation loop's scheduler has no VGA events → zero cost per CPU cycle. The UI timer is responsible for calling `vgaScheduler.ProcessEvents()`.

### 5. UI Timer Frequency

The existing UI timer runs at **60Hz** (`RefreshInterval = 1000.0 / 60.0` in `MainWindowViewModel` and `HeadlessGui`). In async mode this timer becomes the VGA frame pump.

**Problem at 60Hz:** VGA mode 13h and most text modes run at ~70.09Hz. A 60Hz UI pump is consistently slower than the VGA, so every tick must drain 1.17 frames worth of events — regularly rendering an extra partial or complete scanline set beyond one frame. This means one excess `BeginFrame→Scanlines→CompleteFrame` cycle happens roughly every 6 ticks, doing work whose output is immediately overwritten by the next frame.

**Fix:** Set the UI timer to **~75Hz** for both modes (`RefreshInterval = 1000.0 / 75.0`). This gives a small cushion above the 70.09Hz VGA rate, ensuring each tick drains slightly less than one VGA frame's worth of events (~0.93 frames). The common case is that `ProcessEvents()` fires exactly one `CompleteFrame` per UI tick, occasionally zero (when the VGA frame hasn't fully elapsed yet).

75Hz is chosen because:
- It is above all common VGA modes (70Hz, 60Hz)
- It is not so high that the UI thread spins wastefully
- It aligns with common monitor refresh rates (75Hz), for potential future vsync
- At 75Hz output vs 60Hz display, Avalonia simply presents the same frame twice — no visible harm

### 6. UI Timer Integration: Call `ProcessEvents()` from `VgaCard`

In async mode, `VgaCard` receives the UI-side scheduler and calls `ProcessEvents()` before copying the frame buffer. This ensures all pending VGA events fire (completing frames) before the UI reads pixels:

```csharp
public class VgaCard {
    private readonly DeviceScheduler? _uiScheduler;

    // Constructor gains optional scheduler parameter:
    public VgaCard(IGuiVideoPresentation? gui, Renderer renderer,
        ILoggerService loggerService, DeviceScheduler? uiScheduler) {
        // ...
        _uiScheduler = uiScheduler;
    }

    private unsafe void Render(UIRenderEventArgs uiRenderEventArgs) {
        // Process all pending VGA timing events (async mode only).
        _uiScheduler?.ProcessEvents();

        if (!EnsureGuiResolutionMatchesHardware()) return;
        Span<uint> buffer = new Span<uint>(
            (void*)uiRenderEventArgs.Address, uiRenderEventArgs.Length);
        _renderer.Render(buffer);
    }
}
```

**Why this works:**
- The UI timer fires at ~75Hz. The VGA runs at ~70Hz. On average, 0.93 VGA frames of events accumulate per UI tick.
- `ProcessEvents()` drains them all — the scheduler executes every overdue event in time order.
- `VgaTimingEngine` chains events as usual: `OnFrameStart` → scanlines → `OnVerticalRetraceStart` → `CompleteFrame`. Approximately one complete frame gets rendered into the back/front buffer per tick.
- `Renderer.Render()` copies the latest completed frame to the UI.
- Occasional ticks where no frame completed (VGA frame not yet elapsed) are fine — `Render()` copies the most recent `_renderBuffer`, which is the last complete frame.

### 7. Thread Safety Considerations

**Async mode introduces cross-thread access to VGA registers:**

| Resource | Emulation thread | UI thread (async mode) |
|----------|-----------------|----------------------|
| VRAM (`VideoMemory.Planes`) | Writes | Reads (via `RenderScanline`) |
| VGA registers (`IVideoState`) | Writes | Reads (via `BeginFrame`/scanline latching) |
| `InputStatusRegister1` flags | Reads (program polls port 0x3DA) | Writes (via `VgaTimingEngine` events) |
| `Renderer._frame*` fields | Not accessed | Written exclusively |
| `Renderer._frontBuffer` / `_renderBuffer` | Read via `Render()` | Written via `CompleteFrame()` / read via `Render()` |

**Analysis:**
- **VRAM reads vs writes:** Identical to the pre-`3d3a2547` async code. Worst case: torn pixels within a single frame. Acceptable trade-off for performance mode. No correctness issue since the triple-buffer isolates the UI from mid-frame changes.
- **VGA register reads vs writes:** The emulation thread writes registers; `BeginFrame` latches them on the UI thread. A torn register read can cause a single garbled frame. Same behaviour as the old async renderer. Acceptable.
- **`InputStatusRegister1` flags:** The `VgaTimingEngine` writes `VerticalRetrace` and `DisplayDisabled` from the UI thread. The emulation thread reads them via port 0x3DA. These are simple booleans — no tearing risk. The timing will be non-deterministic (flags transition when `ProcessEvents()` runs on the UI thread, not in lock-step with CPU cycles). This is the inherent trade-off of async mode and is acceptable — programs that need precise flag timing should use sync mode.
- **Renderer `_frame*` fields:** Only accessed from the thread running `ProcessEvents()`. In async mode that's exclusively the UI timer thread. No conflict.
- **Triple-buffer swap:** `Interlocked.Exchange` and `Volatile.Write/Read` are already used. Thread-safe in both modes.

### 8. Spice86DependencyInjection Wiring (Full)

```csharp
VgaBlinkState vgaBlinkState = new();
Renderer vgaRenderer = new(memory, videoState, vgaBlinkState, loggerService);

bool isSyncRendering = configuration.RenderingMode == RenderingMode.Sync;

// In sync mode, VGA events go on the emulation loop's scheduler.
// In async mode, a separate scheduler is created for the UI thread.
DeviceScheduler vgaScheduler = isSyncRendering
    ? emulationLoopScheduler
    : new DeviceScheduler(_emulatedClock, loggerService);

VgaTimingEngine vgaTimingEngine = new(videoState, vgaScheduler,
    _emulatedClock, vgaRenderer, vgaBlinkState);

// ...

// Pass the UI-side scheduler to VgaCard only in async mode.
DeviceScheduler? uiScheduler = isSyncRendering ? null : vgaScheduler;
VgaCard vgaCard = new(_gui, vgaRenderer, loggerService, uiScheduler);
vgaCard.SubscribeToEvents();
```

### 9. VideoCardViewModel Adaptation

`VideoCardViewModel` reads `VgaTimingEngine.LastFrameDuration`. Since `VgaTimingEngine` exists in both modes, no adaptation is needed. The value will reflect:
- In sync mode: deterministic emulated frame duration.
- In async mode: wall-clock duration between frame starts as processed on the UI thread (less meaningful but still valid).

### 10. Emulation Loop: No Changes

`EmulationLoop.RunLoop()` continues to call `emulationLoopScheduler.ProcessEvents()` every cycle. In sync mode this fires VGA events; in async mode the emulation-loop scheduler has no VGA events, so `ProcessEvents()` returns immediately (the queue count check `if (_queue.Count == 0) return;` is the first thing it does).

Other scheduled events (audio, PIT timers) remain on the emulation-loop scheduler regardless of rendering mode.

---

## Summary of File Changes

| File | Change |
|------|--------|
| `Configuration.cs` | Add `RenderingMode` enum and property |
| `Renderer.cs` | **No logic changes** |
| `VgaTimingEngine.cs` | **No logic changes** |
| `EmulationLoopScheduler.cs` → `DeviceScheduler.cs` | Rename class, monitor class, namespace; move folder from `VM/EmulationLoopScheduler/` → `VM/DeviceScheduler/` |
| `VgaCard.cs` | Accept optional `DeviceScheduler?` for UI-side scheduling; call `ProcessEvents()` before `Render()` |
| `MainWindowViewModel.cs` / `HeadlessGui.cs` | Set timer to ~75Hz in async mode |
| `Spice86DependencyInjection.cs` | Create separate scheduler in async mode; wire to `VgaTimingEngine` and `VgaCard` |
| `IVgaRenderer.cs` | **No changes** |
| `VideoCardViewModel.cs` | **No changes** (VgaTimingEngine exists in both modes) |
| All files importing old namespace | Update `using Spice86.Core.Emulator.VM.EmulationLoopScheduler;` → `using Spice86.Core.Emulator.VM.DeviceScheduler;` |

---

## Code Duplication Analysis

| Concern | Duplication? |
|---------|-------------|
| Pixel conversion (Draw*Mode, ReadVideoMemory) | **None** — shared `Renderer` |
| Register latching (BeginFrame) | **None** — same method |
| Scanline rendering (RenderScanline) | **None** — same method |
| Frame completion (CompleteFrame) | **None** — same method |
| UI copy (Render) | **None** — same method |
| Timing flag management (VerticalRetrace, DisplayDisabled) | **None** — same `VgaTimingEngine` events |
| Blink state | **None** — same `VgaTimingEngine.OnBlinkToggle` event |
| Event scheduling logic | **None** — same `DeviceScheduler` class |

**Total new code:**
1. `DeviceScheduler` — class rename + folder move (zero logic change)
2. `VgaCard` — accept optional scheduler, one `?.ProcessEvents()` call (~5 lines)
3. `Spice86DependencyInjection` — conditional scheduler creation (~8 lines)
4. `Configuration` — enum + property (~5 lines)
5. `MainWindowViewModel` / `HeadlessGui` — timer frequency change (~2 lines)

---

## Risks and Mitigations

| Risk | Mitigation |
|------|-----------|
| Programs polling `InputStatusRegister1` see non-deterministic transitions in async mode | This is inherent to async. The flags still transition correctly within the VGA event sequence — just not locked to CPU cycle boundaries. Programs needing determinism should use sync mode. |
| `ProcessEvents()` burst on UI thread is too slow | At ~70 FPS VGA with ~900 events/frame, ~840 events fire per UI tick at 75Hz (~13.3ms between ticks). Each event is trivial (flag set or scanline render). At 75Hz pump above the 70Hz VGA rate, the burst is bounded to less than one frame's worth. If it becomes an issue, cap the number of events per UI tick. |
| Race between VRAM writes (emulation thread) and VRAM reads (UI thread scanline render) | Same situation as pre-`3d3a2547` async code. Worst case: intra-frame tearing. Acceptable for performance mode. |
| VGA register torn reads across threads | Simple value types (int, bool, byte). Practically no risk. A stale value produces at most one garbled frame. |
| Multiple complete frames rendered per UI tick, wasted work | At 75Hz UI vs 70Hz VGA, average is ~0.93 frames per tick. Most ticks render exactly 0 or 1 complete frame. Occasional zero-frame ticks (no `CompleteFrame` fired) are fine — `Render()` shows the last completed frame. |
| Switching modes at runtime | Not planned for V1. Requires restart. Avoids complexity of draining in-flight scheduler events and re-wiring. |
| `DeviceScheduler` used from both threads simultaneously | Not an issue — each mode has its own instance (or shares only when the same thread drives it). In async mode the VGA scheduler is exclusively accessed from the UI timer thread. |

---

## Implementation Order

1. Rename `EmulationLoopScheduler` → `DeviceScheduler` (class, monitor, namespace, folder); update all import sites
2. Add `RenderingMode` enum and config property
3. Add optional `DeviceScheduler?` parameter to `VgaCard`; call `ProcessEvents()` before rendering
4. Wire conditional scheduler creation in `Spice86DependencyInjection`
5. Update UI timer to ~75Hz in async mode (`MainWindowViewModel`, `HeadlessGui`)
6. Test both modes with known programs
