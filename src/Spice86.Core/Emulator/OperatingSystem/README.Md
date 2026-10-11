# DOS and Memory Layout

Spice86 implements DOS services in C#. Assembly code is written to emulated
memory where DOS programs expect interrupt handlers, device headers, and
entry points. The assembly stubs call back into the corresponding C# handlers.

## Conventional memory

Conventional memory ends at physical address `0xA0000` (640 KiB). The BIOS
reports the amount of conventional memory as the configured RAM size, capped
at 640 KiB. DOS memory allocation has its own limit: the initial MCB ends at
segment `0x9FFF`, immediately below video memory.

Memory is allocated in paragraphs of 16 bytes. The DOS memory manager starts
with one free MCB, immediately before the configured initial program's PSP.
The PSP segment is the configured program-entry segment minus `0x10`. DOS
allocations, program PSPs, environments, and resident programs use this MCB
chain. The default allocation strategy is first fit; INT 21h/AH=58h can change
the strategy.

Programs that need conventional memory should allocate only what they use,
free unused blocks with INT 21h/AH=49h, and resize their own block with
INT 21h/AH=4Ah after loading. A resident program must keep its resident code
and data allocated. XMS and EMS provide separate memory; they do not increase
the free space in the conventional MCB chain.

The configured program-entry segment affects the initial free block. With the
default entry segment `0x1000`, the initial MCB starts at `0x0FFF`. This is
separate from the root `COMMAND.COM` PSP at `0x0060`.

### Conventional-memory map

The rows are not all separate allocations. In particular, the low-memory
structures are within the conventional RAM range.

| Physical address | Segment:offset | Use |
| --- | --- | --- |
| `0x00000-0x003FF` | `0000:0000` | Interrupt Vector Table. Spice86 writes installed interrupt handler addresses here. |
| `0x00400-0x004FF` | `0040:0000` | BIOS Data Area, including conventional-memory size and keyboard state. |
| `0x00500-0x9FFFF` | `0050:0000` to `9FFF:000F` | Conventional RAM. `0x50` is the free-memory start constant, not a guarantee that all addresses above it are free. |
| `0x00600-0x006FF` | `0060:0000` | Root `COMMAND.COM` PSP. Its environment is placed at `0068:0000`, overlapping the PSP command-tail area. |
| `0x00700-0x0078B` | `0070:0000` | DOS SYSVARS structure (`0x8C` bytes). Its `FirstMCB` field is initialized to segment `0x78`. |
| From `0x00B20` | `00B2:0000` | DOS Swappable Data Area (SDA), including the current PSP and InDOS state. |
| `0x01000-0x01007` | `0100:0000` | VGA static-functionality table written by Spice86. |
| `0x07C00-0x07DFF` | `07C0:0000` | Boot-sector load area, used when loading a boot sector. |
| From `initial PSP - 1` through segment `0x9FFF` | DOS MCB chain | DOS memory manager's free and allocated blocks. The initial PSP is based on the configured program-entry segment. |

The SYSVARS `FirstMCB` field is initialized to `0x78`. The memory manager
constructs its initial chain at `initial PSP - 1`; with the default program
entry segment, this is `0x0FFF`. The value in SYSVARS and the manager's
initial-chain location therefore do not currently describe the same address.

## BIOS, DOS and emulator-owned memory

The `0xA0000-0xFFFFF` range is the upper-memory and BIOS address space. Spice86
uses parts of this range for video memory, VGA font data, DOS private tables,
the EMS page frame, BIOS code, interrupt stubs, and DOS device headers. It is
not available to the DOS MCB allocator.

| Physical address | Segment | Use |
| --- | --- | --- |
| `0xA0000-0xAFFFF` | `A000` | VGA graphics memory window. |
| `0xB0000-0xB7FFF` | `B000` | Monochrome text video memory. |
| `0xB8000-0xBFFFF` | `B800` | Color text video memory. |
| From `0xC0000` | `C000` | VGA ROM data mapping. Spice86 stores VGA font data here; it is not a complete VGA BIOS ROM. |
| `0xC8000-0xCFFFF` | `C800-CFFF` | DOS private table arena. DOS reserves entries here for internal tables; XMS reserves a paragraph for its guest entry stub. |
| `0xD0000-0xDFFFF` | `D000-DFFF` | Not allocated as DOS UMBs. |
| `0xE0000-0xEFFFF` | `E000` | EMS page frame, a 64 KiB window divided into four 16 KiB pages. |
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
blocks from this pool by handle; it does not consume conventional MCB space.
The High Memory Area begins at `0x100000` and ends at `0x10FFEF`. XMS manages
HMA allocation and the A20 gate. Upper Memory Blocks are not implemented, so
the DOS allocator cannot use the `0xC8000-0xFFFFF` region as UMBs.

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
- [DosSysVars.cs](./Structures/DosSysVars.cs): SYSVARS structure and initial `FirstMCB`.
- [DosTables.cs](./Structures/DosTables.cs): DOS private table arena.
- [DosProcessManager.cs](./DosProcessManager.cs): root PSP and process setup.
- [ExtendedMemoryManager.cs](../InterruptHandlers/Dos/Xms/ExtendedMemoryManager.cs): XMS and HMA.
- [ExpandedMemoryManager.cs](../InterruptHandlers/Dos/Ems/ExpandedMemoryManager.cs): EMS services and page frame.
- [EmmMemory.cs](../InterruptHandlers/Dos/Ems/EmmMemory.cs): EMS backing memory size.
- [MemoryAsmWriter.cs](../InterruptHandlers/Common/MemoryWriter/MemoryAsmWriter.cs): assembly stubs and callback instructions.
- [CallbackHandler.cs](../InterruptHandlers/Common/Callback/CallbackHandler.cs): callback registration and dispatch.
- [InterruptInstaller.cs](../InterruptHandlers/Common/RoutineInstall/InterruptInstaller.cs): interrupt stubs and IVT entries.
