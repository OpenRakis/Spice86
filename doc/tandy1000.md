# Tandy 1000

Select the platform explicitly:

```powershell
dotnet run --project src\Spice86 -- --MachineType Tandy1000 -e path\to\program.exe
```

Omitting `--MachineType` preserves the existing PC/VGA platform.
`--MachineType Tandy` is an alias for `--MachineType Tandy1000`; both select
the same native platform, BIOS, memory, and sound defaults.

For OutRun, launch `OUTRUN.EXE` from its game directory and choose option 2,
`TANDY (16 COLOUR)`. The equivalent DOSBox Staging selection is `machine=tandy`.

## Platform boundaries

Tandy support uses DOSBox Staging commit
`27b1b7ca719c21ef3444f7ad1cf2d9cd439527cc` as its behavioral reference.
It targets that emulator's Tandy implementation, not every physical Tandy model.
PCjr-only cartridges, memory configuration, and executable-loading behavior are
not additional supported platforms.

Video uses the existing Spice86 frame publication and presentation path.
Native RGB modes, registers, memory banking, BIOS drawing, palettes, and beam
timing are in scope. Composite decoding, shaders, video capture, and other
presentation effects are intentionally excluded.

The CPU, disk, keyboard, mouse, RTC, PIT, PIC, and extended-memory services reuse
Spice86 components. Selecting Tandy does not impose an 8088-only CPU, disable RTC,
or disable all extended memory. Machine selection is separate from sound
selection.

## Conventional memory

The upstream implementation uses three different boundaries:

| Quantity | Tandy value |
|---|---|
| Physical conventional RAM backing | 640 KiB |
| BIOS INT 12h report, word at physical 00413h | 624 KiB |
| True conventional-memory word at physical 00415h | 640 KiB |

These are not interchangeable. DOS is outside this port's scope: Spice86's
existing DOS allocator, MCB chains, UMB policy, shell, loader, and environment
remain unchanged. DOSBox Staging's separate approximately 608 KiB allocation
limit is not ported. BIOS memory reporting is not a claim that Spice86's DOS
allocator enforces that limit.

Tandy display memory shares conventional RAM. CPU-visible and display-visible
banks can differ. The page register at 3DFh and the CRTC line-addressing controls
select the active mappings; 3DFh is not a palette data port.

## Native video modes

| BIOS mode | Output |
|---|---|
| 00h, 01h | 40-column text |
| 02h, 03h | 80-column text |
| 04h, 05h | 320 x 200, four colours |
| 06h | 640 x 200, two colours |
| 08h | 160 x 200, sixteen colours |
| 09h | 320 x 200, sixteen colours |
| 0Ah | 640 x 200, four colours |

Mode 07h is absent from the reference table. Its shared 0Eh entry is not
reachable on Tandy; this port preserves that rejection. VGA-specific debugger
tabs and MCP palette inspection are unavailable on Tandy. Generic framebuffer
and memory inspection remain available.

## Sound options

| Option | Default | Meaning |
|---|---|---|
| `--TandySoundMode` | `Auto` | `Auto`, `On`, `Psg`, or `Off` |
| `--TandyFadeout` | `off` | PSG fade-out selection |
| `--TandyFilter` | `on` | PSG filtering |
| `--TandyDacFilter` | `on` | DAC filtering |

`Auto` enables native sound for the Tandy platform. `Psg` omits the native DAC.
Turning sound off does not change the selected BIOS, video, or memory layout.
`On` forces the native ports even on the PC platform; it does not add PCjr or
standalone-card aliases.

The native PSG uses the NCR8496 variant at C0h-C1h. The native DAC uses C4h-C7h,
IRQ 7, and DMA channel 1. Both default filters use a third-order 120 Hz
high-pass and a second-order 4800 Hz low-pass filter.

Fade-out `on` waits 500 ms, then fades over 500 ms. A custom value such as
`--TandyFadeout "500 500"` specifies integer wait and fade durations in
milliseconds. Wait must be 100-5000 ms; fade must be 10-3000 ms. Invalid values
warn and disable fading.

Custom filters accept `"hpf ORDER HZ"`, `"lpf ORDER HZ"`, or both triples in
either order, for example `--TandyFilter "hpf 3 120 lpf 2 4800"`. Order must
be 1-16; frequency must be a positive integer. Use lowercase names and single
spaces without duplicate filter types. Invalid values warn and restore the
default `on` filters.

Boolean values are case-sensitive. Enabled aliases are `enabled`, `true`, `on`,
and `yes`; disabled aliases are `disabled`, `false`, `off`, `no`, and `none`.

Sound Blaster is independently configurable. BIOS sound services prefer an
available Sound Blaster DAC over the native Tandy DAC. This is distinct from
ownership of the direct native hardware.

The secondary DMA controller and Tandy sound cannot own their overlapping
C0h ports simultaneously. Tandy initialization releases the secondary
controller after Sound Blaster initialization. A later high-DMA request can
reactivate the secondary controller and evict Tandy sound. Arbitrary SB16
high-DMA operation and native Tandy sound are not guaranteed to coexist.

Native recording and joystick/DAC modes retain the limitations of the reference
implementation; this feature does not add host microphone capture.

## Source mapping

All paths in the left column refer to the pinned DOSBox Staging checkout.
Shared helpers are required even when their names do not contain "Tandy".

| Upstream source | Spice86 responsibility |
|---|---|
| `src\dosbox.h`, `src\dosbox.cpp` | `MachineType`, `MachineProfile`, CLI/runtime options, composition order |
| `src\ints\bios.cpp`, `bios.h` | Platform BIOS data, machine identity, INT 15h, sound BIOS, BDA and IVT callbacks |
| `src\dos\dos_memory.cpp` | Out of scope: DOS allocator and UMB policy are not ported |
| `src\hardware\memory.cpp` | Existing physical memory/MMU/A20 plus Tandy shared-memory decoding |
| `src\hardware\dma.cpp`, `dma.h` | `DmaBus`, `DmaChannel`, secondary-controller lifetime and reservation eviction |
| `src\hardware\audio\tandy_sound.cpp`, `tandy_sound.h`, `private\tandy_sound.h` | Native PSG/DAC, sound options, queue synchronization, port/DMA/IRQ lifetime |
| `src\hardware\audio\mame\sn76496.cpp`, `src\hardware\audio\private\mame\sn76496.h` | Selected NCR8496 synthesis core |
| `src\hardware\audio\soundblaster.cpp` | BIOS DAC routing, DMA reacquisition, compatible warmup behavior |
| `src\hardware\audio\speaker.cpp`, `src\audio\mixer.cpp`, `channel_names.h` | Startup order and existing Spice86 mixer/channel integration |
| `src\hardware\video\vga.h`, `vga.cpp` | Native register and derived-mode state |
| `src\hardware\video\vga_other.cpp`, `vga_crtc.cpp`, `vga_misc.cpp` | Register writes, CRTC addressing, status reads, native port registration |
| `src\hardware\video\vga_memory.cpp` | Shared RAM, banking, aperture aliases, independent display/CPU banks |
| `src\hardware\video\vga_draw.cpp` | RGB scanline decoding and applicable beam timing |
| `src\ints\int10.cpp`, `int10.h`, `int10_modes.cpp` | Tandy ROM configuration signature, INT 10h dispatch, mode metadata and mode setup |
| `src\ints\int10_memory.cpp`, `int10_pal.cpp` | BIOS pages, memory, palette and border operations |
| `src\ints\int10_put_pixel.cpp`, `int10_char.cpp` | BIOS pixels, characters, fonts, scrolling and clearing |
| `src\ints\int10_vptable.cpp` | Active Tandy BIOS video parameter table; disabled alternate table is not ported |
| `src\hardware\pic.cpp`, `timer.cpp`, `cmos.cpp`, `src\cpu\callback.cpp` | Reused interrupt, scheduler, clock, and callback infrastructure |
| `src\hardware\input\*`, `src\ints\bios_keyboard.cpp`, `bios_disk.cpp` | Reused input/disk behavior; no invented separate Tandy controller |
| `src\dos\dos.cpp`, `dos_execute.cpp`, `programs\boot.cpp`, `programs\mode.cpp` | Out of scope: no DOS kernel, loader, shell, or internal-command port |
| `src\main.cpp`, `src\shell\shell.cpp`, `src\utils\rwqueue.cpp` | Existing host lifecycle, shell/configuration and queue equivalents |
| `src\misc\video.cpp`, `video.h` | Only applicable hardware/output metadata; no host presentation port |
| `src\gui\render\*`, `src\capture\image\image_scaler.cpp` | Host shaders, presentation extras, and capture intentionally excluded |

Shared Spice86 components are reused rather than replaced wholesale with
DOSBox's CPU, DOS kernel, host frontend, or native C++ libraries.
Source correspondence alone is not proof of runtime equivalence.

## Verification status

The solution builds. Manual checks cover platform startup, BIOS memory and ROM
data, text output and scrolling, shared-RAM aliasing, sound selection, pause and
resume, and shutdown. An existing FreeDOS XCOPY program produces matching final
text on the default PC and Tandy platforms.

OutRun has also been checked using MCP against the installed DOSBox Staging
0.83.0 (7b400), configured with `machine=tandy`. Its 320 x 200 mode 09h credits
screen matches the reference raw image pixel for pixel. The game reaches the
race, detects Tandy sound, and executes native PSG and DAC programming.
Detection requires the complete firmware configuration block at FC000h,
including its 21h signature; the model byte at FFFFEh alone is insufficient.

Exhaustive runtime parity against the pinned DOSBox Staging revision has not
been established. Other graphics modes, synthesis fidelity, DAC transfer
completion, BIOS sound callbacks, and DMA ownership transitions still need
comparative coverage. The OutRun run uses Spice86's default clock; matching
CPU speed and exact audio output have not been established.

## Automated coverage

`tests\Spice86.Tests\Resources\TandyTests` contains original, short ASM probes
of the hardware paths observed in OutRun, not copied game code or assets:

- `tandy_detection.asm`: the model byte at FFFFEh, the FC000h signature,
  INT 15h/AH=C0h, and the distinct 624/640 KiB BIOS memory reports. The same
  program also runs on the default PC and must not detect Tandy.
- `tandy_mode09.asm`: mode 09h, four-way scanline interleaving, conventional-RAM
  aliasing, independent processor/display banks, BIOS pixels, BIOS and direct
  palette writes, and bounded vertical-retrace polling. The oracle checks
  rendered RGB pixels and the BIOS page-bank shadow.
- `tandy_sound.asm`: native BIOS DAC detection, C0h/C1h PSG programming,
  C4h-C7h DAC register readback, DMA 1 terminal count, and IRQ 7 delivery.

`TandyScenarioCatalog` registers all four scenarios in the shared ASM catalogue.
`TandyPlatformTest` runs them interpreted and JIT-compiled.
`GeneratedCodeScenarioTest` discovers each program, generates and compiles C#,
then executes it on a fresh machine of the same platform with the same oracle.
The existing guard rejects application instructions falling back to the
interpreter. Platform suffixes keep PC and Tandy goldens separate. These probes
use synchronous rendering and the deterministic emulated clock.

Run the focused hardware checks and the suite from the repository root:

```powershell
dotnet test tests\Spice86.Tests -- --filter-class '*TandyPlatformTest*'
dotnet test tests\Spice86.Tests -- --filter-not-class '*SingleStepTest*'
```

Reassemble a changed probe with NASM, preserving its committed `.com` resource:

```powershell
nasm -f bin -o tests\Spice86.Tests\Resources\TandyTests\tandy_detection.com tests\Spice86.Tests\Resources\TandyTests\tandy_detection.asm
```

Use the repository's serial catalogue regeneration procedure for listing, CFG,
and generated-source goldens. Normal test runs must not write goldens.

## Attribution

The DOSBox-derived platform implementation credits the DOSBox Team and the
DOSBox Staging Team and retains its applicable GPL-2.0-or-later notices.
The MAME-derived NCR8496 core separately credits Nicola Salmoria and retains
its BSD-3-Clause terms.

The notices and license texts are distributed under
`licenses\dosbox-staging`. These terms do not replace the license for unrelated
Spice86 code.
