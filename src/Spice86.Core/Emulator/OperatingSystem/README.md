# DOS and Memory Layout

Spice86 implements DOS services in C#. Assembly code is written to emulated
memory where DOS programs expect interrupt handlers, device headers, and
entry points. The assembly stubs call back into the corresponding C# handlers.

## Conventional memory

Conventional memory ends at physical address `0xA0000` (640 KiB). The BIOS
reports the configured RAM size, capped at 640 KiB. DOS allocates conventional
memory through an MCB chain; its final block ends at segment `0x9FFF` when UMB
support is disabled. With UMB support enabled, a bridge MCB at `0x9FFF` can
link the conventional chain to the UMB chain at `0xD000`.

Memory is allocated in paragraphs of 16 bytes. The DOS memory manager starts
with one free MCB, immediately before the initial program's PSP. The PSP
segment is the configured program-entry segment minus `0x10`; the initial MCB
is one paragraph before that PSP. DOS allocations, program PSPs, environments,
and resident programs use this chain. The default allocation strategy is first
fit; INT 21h/AH=58h can change the strategy and, when UMBs are available, the
memory searched.

Programs that need conventional memory should allocate only what they use,
free unused blocks with INT 21h/AH=49h, and resize their own block with
INT 21h/AH=4Ah after loading. A resident program must keep its resident code
and data allocated. XMS and EMS provide separate memory; they do not increase
the free space in the conventional MCB chain.

The configured program-entry segment determines the initial MCB location. With
the default entry segment `0x170`, the initial PSP is at `0x160` and its MCB
is at `0x15F`. This is separate from the root `COMMAND.COM` PSP at `0x0060`.
Its initial free block contains `0x9E9F` paragraphs (649,712 bytes). This MCB
capacity is distinct from the 640 KiB total conventional memory reported by
INT 12h.

### Conventional-memory map

The rows are not all separate allocations. In particular, the low-memory
structures are within the conventional RAM range.

| Physical address | Segment:offset | Use |
| --- | --- | --- |
| `0x00000-0x003FF` | `0000:0000` | Interrupt Vector Table. Spice86 writes installed interrupt handler addresses here. |
| `0x00400-0x004FF` | `0040:0000` | BIOS Data Area, including conventional-memory size and keyboard state. |
| `0x00500-0x9FFFF` | `0050:0000` to `9FFF:000F` | Conventional RAM. `0x50` is the free-memory start constant, not a guarantee that all addresses above it are free. |
| `0x00600-0x006FF` | `0060:0000` | Root `COMMAND.COM` PSP. Its environment is placed at `0068:0000`, overlapping the PSP command-tail area. |
| `0x00700-0x0078B` | `0070:0000` | DOS SYSVARS structure (`0x8C` bytes). Its `FirstMCB` and `MemAllocScanStart` fields identify the actual initial MCB. |
| From `0x00B20` | `00B2:0000` | DOS Swappable Data Area (SDA), including the current PSP and InDOS state. |
| `0x01000-0x01007` | `0100:0000` | VGA static-functionality table written by Spice86. |
| `0x07C00-0x07DFF` | `07C0:0000` | Boot-sector load area, used when loading a boot sector. |
| From `initial PSP - 1` through segment `0x9FFF` | DOS MCB chain | Conventional free and allocated blocks. When UMBs are enabled, the block before `0x9FFF` ends at the bridge MCB there. |

SYSVARS `FirstMCB` and `MemAllocScanStart` are initialized to `initial PSP - 1`
(at `0x15F` with the default program entry segment). They identify the first
MCB in the memory manager's conventional chain.

## Upper memory blocks

UMB support is enabled by default with `--Umb true`. It requires XMS; disabling
XMS also disables UMBs. The UMB chain starts with an MCB at segment `0xD000`.
When EMS is enabled, the UMB covers `0xD0000-0xDFFFF`; without EMS, it extends
through `0xEFFFF`. The EMS page frame occupies `0xE0000-0xEFFFF`.

The conventional and UMB chains are unlinked at startup. INT 21h/AH=58h,
AL=02h queries the link state and AL=03h changes it. Allocation strategies
40h-42h search UMBs only; 80h-82h search UMBs before conventional memory.
XMS 3.0 functions 10h-12h also request, release, and resize UMBs.

## BIOS, DOS and emulator-owned memory

The `0xA0000-0xFFFFF` range contains video memory and emulator-owned regions.
Some of it is available to DOS as UMBs as described above; the remaining
regions are reserved for video, DOS tables, EMS, BIOS code, interrupt stubs,
and device headers.

| Physical address | Segment | Use |
| --- | --- | --- |
| `0xA0000-0xAFFFF` | `A000` | VGA graphics memory window. |
| `0xB0000-0xB7FFF` | `B000` | Monochrome text video memory. |
| `0xB8000-0xBFFFF` | `B800` | Color text video memory. |
| From `0xC0000` | `C000` | VGA ROM data mapping. Spice86 stores VGA font data here; it is not a complete VGA BIOS ROM. |
| `0xC8000-0xCFFFF` | `C800-CFFF` | DOS private table arena. DOS reserves entries here for internal tables; XMS reserves a paragraph for its guest entry stub. |
| `0xD0000-0xDFFFF` | `D000-DFFF` | UMB range when UMB support is enabled. The MCB is at `D000:0000`; usable memory starts at `D001:0000`. |
| `0xE0000-0xEFFFF` | `E000-EFFF` | EMS page frame when EMS is enabled; otherwise an additional UMB range when UMB support is enabled. |
| From `0xF0000` | `F000` | Supplied BIOS image area and default start for Spice86's interrupt and assembly stubs. |
| `0xF1000` | `F100` | EMS device header and INT 67h stub are both written at this address. |
| From `0xF8000` | `F800` | DOS device header area. XMS registers its `XMSXXXX0` header at `F800:0000`, also used as the first default device-header address. |

The interrupt and assembly stubs are written by a shared `MemoryAsmWriter`.
The start segment is `ProvidedAsmHandlersSegment`, defaulting to `0xF000`.
Installed handlers advance the writer's address as they are emitted. The
actual offsets and amount of memory used depend on the installed handlers.
The supplied system BIOS image is also loaded at `F000:0000`, with its reset
entry at `F000:FFF0`; the default stub area and BIOS image therefore share the
same segment.

Interrupt stubs call C# code with callback instructions written inline in
emulated memory. A callback instruction contains `FE 38` and a callback
number. The callback registry maps that number to a C# action; it does not
store the handler's C# code in guest memory. Interrupt callbacks can use their
interrupt vector as the number. Automatically allocated callback numbers
start at `0x100`.

When DOS services are installed, the memory dump replaces
callback instructions with INT/NOP bytes, so tools like Ghidra don't have to deal with that noise.

## XMS

The XMS manager implements XMS 3.0 services. DOS programs find the XMS entry
point through INT 2Fh, AX=4300h/4310h, then call the returned entry point to
request XMS functions.

The extended-memory pool is a separate RAM object of 15 MiB. XMS allocates
blocks from this pool by handle; these allocations do not consume conventional
MCB space. The High Memory Area begins at `0x100000` and ends at `0x10FFEF`.
XMS manages HMA allocation and the A20 gate. XMS also exposes UMB allocation;
those blocks come from the DOS UMB chain described above.

The XMS guest entry stub is placed in the DOS private table arena. The XMS DOS
device header is registered at `F800:0000`.

## EMS

The EMS manager provides services through INT 67h and registers the DOS device
`EMMXXXX0`. It has 8 MiB of separate backing memory, divided into 512 logical
pages of 16 KiB.

Four pages form the EMS page frame at `E000:0000-E000:FFFF`. The EMS system
handle allocates these pages. Programs map logical pages into the frame to
access them. EMS backing memory does not use conventional MCB space.

The implementation supports core EMS 3.2 functions and selected EMS 4.0
functions. VCPI and LIM EMS OS-specific functions are not implemented. The
INT 67h stub is written at `F100:0000`, the same address as the EMS device
header.

## Related code

- [MemoryMap.cs](../Memory/MemoryMap.cs): PC memory-map constants.
- [DosMemoryManager.cs](./DosMemoryManager.cs): conventional MCB chain.
- [DosMemoryAllocationStrategy.cs](./Enums/DosMemoryAllocationStrategy.cs): DOS allocation strategy values.
- [DosInt21Handler.cs](../InterruptHandlers/Dos/DosInt21Handler.cs): INT 21h memory services.
- [DosSysVars.cs](./Structures/DosSysVars.cs): SYSVARS structure and initial `FirstMCB`.
- [DosTables.cs](./Structures/DosTables.cs): DOS private table arena.
- [DosProcessManager.cs](./DosProcessManager.cs): root PSP and process setup.
- [Configuration.cs](../../CLI/Configuration.cs): DOS UMB command-line option.
- [ExtendedMemoryManager.cs](../InterruptHandlers/Dos/Xms/ExtendedMemoryManager.cs): XMS and HMA.
- [ExpandedMemoryManager.cs](../InterruptHandlers/Dos/Ems/ExpandedMemoryManager.cs): EMS services and page frame.
- [EmmMemory.cs](../InterruptHandlers/Dos/Ems/EmmMemory.cs): EMS backing memory size.
- [MemoryAsmWriter.cs](../InterruptHandlers/Common/MemoryWriter/MemoryAsmWriter.cs): assembly stubs and callback instructions.
- [CallbackHandler.cs](../InterruptHandlers/Common/Callback/CallbackHandler.cs): callback registration and dispatch.
- [InterruptInstaller.cs](../InterruptHandlers/Common/RoutineInstall/InterruptInstaller.cs): interrupt stubs and IVT entries.
