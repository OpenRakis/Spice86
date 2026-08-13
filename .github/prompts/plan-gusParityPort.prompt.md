## Plan: DOSBox GF1 Parity Port

Replace the current C# GUS code entirely with a behaviorally equivalent port of the vendored DOSBox Staging classic GF1 implementation. The checked-out `gus.cpp` and private `gus.h` become the baseline, with a parity ledger mapping every reference constant, state field, register behavior, and method to C# and an executable test.

**Steps**
1. Freeze a C++-to-C# parity ledger covering the full DOSBox Staging GUS source and define the scope as classic GF1.
2. Remove and recreate `GravisUltraSound`, `GusVoice`, `GusTimer`, and `GusVoiceIrq` as explicit C# equivalents, with complete public XML documentation.
3. Port all voice behavior: 8/16-bit samples, fixed-point interpolation, volume ramps, panning, loop/bidirectional/rollover logic, IRQ latching, and 14-32 voice rate behavior.
4. Add DOSBox-equivalent emulated-time audio rendering and FIFO behavior so the mixer drains output but does not control hardware state timing.
5. Port timers, DMA, and IRQ handling with recurring scheduling, 8 KiB DMA cadence, terminal-count behavior, reset cancellation, address selection, and proper IRQ 2 handling.
6. Port every I/O/register side effect and wire OPL port `0x388` writes to the GUS AdLib mirror endpoint.
7. Validate configuration, device ownership, environment variables, and defaults; document the actual support level and setup in the README and website.
8. Add deterministic unit tests with real Spice86 DMA/PIC/scheduler/mixer dependencies for all parity-ledger behaviors.
9. Add NASM real-mode `.COM` regression fixtures, register them in sound integration tests, and duplicate applicable scenarios in generated-code tests.
10. Generate a separate ignored `tmp/gus-max-demo` program exercising all implemented GUS capabilities, with build/run instructions.
11. Run focused tests during implementation, then build, full non-`SingleStepTest` suite, fixture assembly, generated-code coverage, and manual compatibility smoke tests.

**Critical fixes included**
- Re-enable the mixer channel after reset register transitions so initialization does not permanently silence GUS output.
- Replace one-shot GUS timer behavior with recurring emulated-time events.
- Replace mixer-demand-driven state progression with elapsed emulated-time rendering.
- Schedule repeated DMA chunks rather than merely registering an unmask callback.
- Implement the outgoing OPL-to-GUS AdLib command mirroring path.
- Add the missing test suite, real-mode test program, and user documentation.

**Parity scope**
“Full” means full parity with DOSBox Staging’s classic GF1 emulation, not unsupported hardware DOSBox itself omits: onboard MIDI UART, audio recording/ADC, microphone input, InterWave extensions, DRAM monitoring, and instruction-by-instruction cycle accuracy. Those boundaries will be documented explicitly.

**Relevant files**
- `c:\Users\noalm\source\repos\Spice86Master\dosbox-staging\src\hardware\audio\gus.cpp`
- `c:\Users\noalm\source\repos\Spice86Master\dosbox-staging\src\hardware\audio\private\gus.h`
- `c:\Users\noalm\source\repos\Spice86Master\src\Spice86.Core\Emulator\Devices\Sound\GravisUltraSound.cs`
- `c:\Users\noalm\source\repos\Spice86Master\src\Spice86.Core\Emulator\Devices\Sound\GusVoice.cs`
- `c:\Users\noalm\source\repos\Spice86Master\src\Spice86.Core\Emulator\Devices\Sound\Opl3Fm.cs`
- `c:\Users\noalm\source\repos\Spice86Master\tests\Spice86.Tests\Emulator\Devices\Sound\`
- `c:\Users\noalm\source\repos\Spice86Master\tests\Spice86.Tests\Resources\Sound\`
- `c:\Users\noalm\source\repos\Spice86Master\tmp\gus-max-demo\`
- `c:\Users\noalm\source\repos\Spice86Master\README.md`
- `c:\Users\noalm\source\repos\Spice86Master\docs\index.html`

**Verification**
1. TDD each parity cluster with a failing focused test before its port.
2. Run focused GUS, sound, DMA, scheduler, and generated-code tests after each slice.
3. Build `src\Spice86.sln`.
4. Assemble and execute all committed NASM GUS fixtures under interpreter and generated-code paths.
5. Run `dotnet test tests\Spice86.Tests --filter 'FullyQualifiedName!~SingleStepTest'`.
6. Build and manually run the ignored maximum-capability demo with normal audio output.
7. Record manual results for the reference-noted compatibility paths: Jazz Jackrabbit, FastTracker 2, and 16-bit DMA usage such as Quake/Windows-driver style transfers.

The comprehensive persistent version is saved in `/memories/session/plan.md`.