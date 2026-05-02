# Maupiti TSR Live-CFG Walkthrough (Instruction by Instruction)

## Scope

This walkthrough is derived from live CFG output already captured in:

- D0288AAE38E0F90C92A32A50B1233CD3A457B1268A87B6E01B95C7B2452BF01B/spice86dumpListing.asm

It covers the small TSR install path that is executed through these routines:

- 017D:EF37 caller stub
- 126E:0000 main TSR setup routine
- 126E:0329 parameter block builder helper
- 126E:0399 and 126E:039E entry selectors
- 126E:03A6 open/create dispatcher
- 126E:042D far callback invoker
- 126E:043E file/context preparation helper
- 1235:0000 low-memory trampoline setup helper
- 109E:0000 TSR keep-memory terminate helper

No raw memory disassembly is used here; only lines present in the live CFG listing.

## High-Level Behavior

The TSR path does five major things:

1. Captures process/memory context and computes resident boundaries.
2. Saves original interrupt vectors for a selected set.
3. Installs its own interrupt handlers.
4. Builds runtime parameter blocks and runs open/create setup logic.
5. Terminates-and-stays-resident via INT 21h AH=31h, keeping the resident footprint.

## 017D:EF37 Caller Stub

| Address | Instruction | Meaning |
|---|---|---|
| 017D:EF37 | call far 126E:0000 | Enter TSR installer main routine. |
| 017D:EF3C | push BP | Standard frame prologue. |
| 017D:EF3D | mov BP,SP | Establish stack frame. |
| 017D:EF3F | call far 1235:0000 | Build low-memory return/trampoline data. |
| 017D:EF44 | mov AX,3 | Prepare parameter value 3 (later used as return code for TSR terminate helper). |
| 017D:EF47 | push AX | Push argument. |
| 017D:EF48 | call far 109E:0000 | Call terminate-and-stay-resident helper. |

## 126E:0000 Main TSR Setup Routine

### Context and Resident Layout Calculation

| Address | Instruction | Meaning |
|---|---|---|
| 126E:0000 | mov DX,0x12C9 | Load TSR data segment base constant. |
| 126E:0003 | mov DS,DX | Switch DS to TSR data segment. |
| 126E:0005 | mov word ptr DS:[0x0032],ES | Save caller ES into TSR data. |
| 126E:0009 | xor BP,BP | Clear BP (scratch). |
| 126E:000B | mov AX,SP | Start size computation from current SP. |
| 126E:000D | add AX,0x0013 | Add fixed overhead. |
| 126E:0010 | mov CL,4 | Prepare shift count for paragraphs. |
| 126E:0012 | shr AX,CL | Convert bytes to paragraphs. |
| 126E:0014 | mov DX,SS | Load current SS. |
| 126E:0016 | add AX,DX | Paragraph base + stack segment gives absolute paragraph value. |
| 126E:0018 | mov word ptr DS:[0x000A],AX | Save computed paragraph value (field A). |
| 126E:001B | mov word ptr DS:[0x000C],AX | Save same value (field C). |
| 126E:001E | add AX,word ptr DS:[4] | Add additional resident size/offset from data. |
| 126E:0022 | mov word ptr DS:[0x000E],AX | Save extended boundary (field E). |
| 126E:0025 | mov word ptr DS:[0x0018],AX | Mirror extended boundary (field 18h). |
| 126E:0028 | mov word ptr DS:[0x001C],AX | Mirror extended boundary (field 1Ch). |
| 126E:002B | mov AX,word ptr ES:[2] | Read value from caller ES context (likely PSP/memory metadata). |
| 126E:002F | sub AX,0x1000 | Normalize/offset memory reference downward by 0x1000 paragraphs. |
| 126E:0032 | mov word ptr DS:[0x0020],AX | Store resulting memory boundary/reference. |

### Save Original Interrupt Vectors

| Address | Instruction | Meaning |
|---|---|---|
| 126E:0035 | mov DI,0x16BA | Destination in TSR data for vector backup table. |
| 126E:0038 | mov SI,0x01DD | Source list pointer in CS containing vector numbers. |
| 126E:003B | mov CX,0x0012 | Loop over 18 vectors. |
| 126E:003E | nop | Alignment/no-op. |
| 126E:003F | cld | Ensure forward string ops. |
| 126E:0040 | lods AL,byte ptr CS:[SI] | Read next vector number from list. |
| 126E:0042 | mov AH,0x35 | DOS Get Interrupt Vector function. |
| 126E:0044 | int 0x21 | Query current vector AL. |
| 126E:0046 | mov word ptr DS:[DI],BX | Save vector offset. |
| 126E:0048 | mov word ptr DS:[DI+2],ES | Save vector segment. |
| 126E:004B | add DI,4 | Advance to next backup slot. |
| 126E:004E | loop 0x0040 | Repeat for all vectors in list. |

### Install TSR Interrupt Handlers

| Address | Instruction | Meaning |
|---|---|---|
| 126E:0050 | push DS | Preserve DS. |
| 126E:0051 | push CS | Prepare DS=CS for handler pointers. |
| 126E:0052 | pop DS | DS now points to code segment where handlers live. |
| 126E:0053 | mov DX,0x00CE | Handler offset for INT 00h. |
| 126E:0056 | mov AX,0x2500 | DOS Set Interrupt Vector AH=25h, AL=00h. |
| 126E:0059 | int 0x21 | Install INT 00h handler. |
| 126E:005B | mov DX,0x00D5 | Handler offset for INT 23h. |
| 126E:005E | mov AX,0x2523 | Set vector INT 23h. |
| 126E:0061 | int 0x21 | Install INT 23h handler. |
| 126E:0063 | mov DX,0x009D | Handler offset for INT 24h. |
| 126E:0066 | mov AX,0x2524 | Set vector INT 24h. |
| 126E:0069 | int 0x21 | Install INT 24h handler. |
| 126E:006B | mov DX,0x00C6 | Handler offset for INT 3Fh. |
| 126E:006E | mov AX,0x253F | Set vector INT 3Fh. |
| 126E:0071 | int 0x21 | Install INT 3Fh handler. |
| 126E:0073 | pop DS | Restore original DS. |

### Build and Process First Context Block

| Address | Instruction | Meaning |
|---|---|---|
| 126E:0074 | mov AX,0x14BA | First context block base/handle value. |
| 126E:0077 | push DS | Arg part 1. |
| 126E:0078 | push AX | Arg part 2. |
| 126E:0079 | push DS | Arg part 3. |
| 126E:007A | push AX | Arg part 4. |
| 126E:007B | mov AX,0x0206 | Additional parameter value. |
| 126E:007E | push CS | Arg part 5. |
| 126E:007F | push AX | Arg part 6. |
| 126E:0080 | push CS | Arg part 7. |
| 126E:0081 | call near 0x0329 | Build parameter block structure. |
| 126E:0084 | push CS | Prepare call argument to selector helper. |
| 126E:0085 | call near 0x0399 | Select D7B1 profile and dispatch open/create pipeline. |

### Build and Process Second Context Block

| Address | Instruction | Meaning |
|---|---|---|
| 126E:0088 | mov AX,0x15BA | Second context block base/handle value. |
| 126E:008B | push DS | Arg part 1. |
| 126E:008C | push AX | Arg part 2. |
| 126E:008D | push DS | Arg part 3. |
| 126E:008E | push AX | Arg part 4. |
| 126E:008F | mov AX,0x0206 | Additional parameter value. |
| 126E:0092 | push CS | Arg part 5. |
| 126E:0093 | push AX | Arg part 6. |
| 126E:0094 | push CS | Arg part 7. |
| 126E:0095 | call near 0x0329 | Build second parameter block. |
| 126E:0098 | push CS | Prepare call argument to selector helper. |
| 126E:0099 | call near 0x039E | Select D7B2 profile and dispatch open/create pipeline. |
| 126E:009C | ret far | Return to caller stub at 017D. |

## 126E:0329 Parameter Block Builder

| Address | Instruction | Meaning |
|---|---|---|
| 126E:0329 | mov BX,SP | Access far-call arguments by stack. |
| 126E:032B | push DS | Preserve DS. |
| 126E:032C | les DI,word ptr SS:[BX+8] | ES:DI = destination block pointer. |
| 126E:0330 | lds SI,word ptr SS:[BX+4] | DS:SI = source template/string pointer. |
| 126E:0334 | cld | Forward string operations. |
| 126E:0335 | xor AX,AX | AX = 0 for zero-inits. |
| 126E:0337 | stos word ptr ES:[DI],AX | Field 0 = 0. |
| 126E:0338 | mov AX,0xD7B0 | Magic/type marker D7B0. |
| 126E:033B | stos word ptr ES:[DI],AX | Field type marker. |
| 126E:033C | mov AX,0x0080 | Flag/size field. |
| 126E:033F | stos word ptr ES:[DI],AX | Store field. |
| 126E:0340 | xor AX,AX | AX = 0 again. |
| 126E:0342 | stos word ptr ES:[DI],AX | Zero field. |
| 126E:0343 | stos word ptr ES:[DI],AX | Zero field. |
| 126E:0344 | stos word ptr ES:[DI],AX | Zero field. |
| 126E:0345 | lea AX,DI+0x74 | Compute pointer to embedded area. |
| 126E:0348 | stos word ptr ES:[DI],AX | Store offset pointer. |
| 126E:0349 | mov AX,ES | Segment for pointer. |
| 126E:034B | stos word ptr ES:[DI],AX | Store segment pointer. |
| 126E:034C | mov AX,0x043E | Function entry offset for later callback table. |
| 126E:034F | stos word ptr ES:[DI],AX | Store callback offset. |
| 126E:0350 | mov AX,CS | Callback segment. |
| 126E:0352 | stos word ptr ES:[DI],AX | Store callback segment. |
| 126E:0353 | xor AX,AX | Zero fill value. |
| 126E:0355 | mov CX,0x000E | Number of words to clear. |
| 126E:0358 | rep stos word ptr ES:[DI],AX | Clear tail fields. |
| 126E:035A | lods AL,byte ptr DS:[SI] | Read source length byte. |
| 126E:035B | cmp AL,0x4F | Compare to max allowed length. |
| 126E:035D | jbe short 0x0361 | Keep length if <= 0x4F. |
| 126E:0361 | mov CL,AL | Length into CL. |
| 126E:0363 | xor CH,CH | Zero high count byte. |
| 126E:0365 | rep movs byte ptr ES:[DI],byte ptr DS:[SI] | Copy payload bytes. |
| 126E:0367 | xor AL,AL | Null terminator value. |
| 126E:0369 | stos byte ptr ES:[DI],AL | Write null terminator. |
| 126E:036A | pop DS | Restore DS. |
| 126E:036B | ret far 8 | Return and discard arguments. |

## 126E:0399 and 126E:039E Selector Entrypoints

| Address | Instruction | Meaning |
|---|---|---|
| 126E:0399 | mov DX,0xD7B1 | Select profile/type D7B1. |
| 126E:039C | jmp short 0x03A6 | Enter common dispatcher. |
| 126E:039E | mov DX,0xD7B2 | Select profile/type D7B2. |
| 126E:03A1 | jmp short 0x03A6 | Enter common dispatcher. |

## 126E:03A6 Common Dispatcher

| Address | Instruction | Meaning |
|---|---|---|
| 126E:03A6 | mov BX,SP | Stack-based argument access. |
| 126E:03A8 | les DI,word ptr SS:[BX+4] | ES:DI = context block pointer. |
| 126E:03AC | mov AX,word ptr ES:[DI+2] | Load block type marker. |
| 126E:03B0 | cmp AX,0xD7B1 | Is profile D7B1? |
| 126E:03B3 | je short 0x03C7 | Branch if yes. |
| 126E:03B5 | cmp AX,0xD7B2 | Is profile D7B2? |
| 126E:03B8 | je short 0x03C7 | Branch if yes. |
| 126E:03BA | cmp AX,0xD7B0 | Is generic type D7B0? |
| 126E:03BD | je short 0x03CF | Branch if yes. |
| 126E:03CF | xor AX,AX | Reset status. |
| 126E:03D1 | mov word ptr ES:[DI+2],DX | Overwrite type marker with selected profile. |
| 126E:03D5 | mov word ptr ES:[DI+8],AX | Clear field. |
| 126E:03D9 | mov word ptr ES:[DI+0x0A],AX | Clear field. |
| 126E:03DD | mov BX,0x0010 | Callback slot offset/index. |
| 126E:03E0 | call near 0x042D | Call function pointer from block. |
| 126E:03E3 | je short 0x03EB | Branch on AX==0 result (success path). |
| 126E:03EB | ret far 4 | Return to caller, pop argument pointer. |

## 126E:042D Far Callback Invoker

| Address | Instruction | Meaning |
|---|---|---|
| 126E:042D | push ES | Preserve ES. |
| 126E:042E | push DI | Preserve DI. |
| 126E:042F | push ES | Push callback arg segment. |
| 126E:0430 | push DI | Push callback arg offset. |
| 126E:0431 | call far dword ptr ES:[BX+DI] | Invoke far callback from table entry. |
| 126E:0434 | or AX,AX | Test callback result in AX. |
| 126E:0436 | je short 0x043B | Branch if success (AX==0). |
| 126E:043B | pop DI | Restore DI. |
| 126E:043C | pop ES | Restore ES. |
| 126E:043D | ret near | Return to dispatcher. |

## 126E:043E File/Context Preparation Callback

| Address | Instruction | Meaning |
|---|---|---|
| 126E:043E | mov BX,SP | Stack-based arg access. |
| 126E:0440 | push DS | Preserve DS. |
| 126E:0441 | lds DI,word ptr SS:[BX+4] | DS:DI = context block pointer. |
| 126E:0445 | xor CX,CX | CX = 0. |
| 126E:0447 | mov word ptr DS:[DI],CX | Clear first status/count field. |
| 126E:0449 | mov AX,0x3D00 | Default operation Open Existing (AH=3D, AL=0). |
| 126E:044C | cmp word ptr DS:[DI+2],0xD7B1 | Check profile/type. |
| 126E:0451 | je short 0x0460 | If D7B1 keep default open mode. |
| 126E:0453 | mov AL,2 | Use read/write open mode if not D7B1. |
| 126E:0455 | inc word ptr DS:[DI] | Increment status/count field. |
| 126E:0457 | cmp word ptr DS:[DI+2],0xD7B3 | Check alternate type. |
| 126E:045C | je short 0x0460 | If D7B3 keep open path. |
| 126E:045E | mov AH,0x3C | Else switch to Create File function. |
| 126E:0460 | cmp byte ptr DS:[DI+0x30],0 | Test optional flag byte. |
| 126E:0464 | je short 0x046F | If zero, skip optional branch body. |
| 126E:046F | mov AX,0x051A | Store handler/function offset candidate. |
| 126E:0472 | xor CX,CX | CX = 0. |
| 126E:0474 | mov BX,CX | BX = 0. |
| 126E:0476 | cmp word ptr DS:[DI+2],0xD7B1 | Type check for D7B1. |
| 126E:047B | je short 0x04A6 | If D7B1 skip device info probe. |
| 126E:047D | mov BX,word ptr DS:[DI] | Load handle/index from field. |
| 126E:047F | mov AX,0x4400 | IOCTL get device info. |
| 126E:0482 | int 0x21 | Execute IOCTL. |
| 126E:0484 | mov AX,0x056F | Load alternate function offset. |
| 126E:0487 | mov CX,AX | Copy to CX. |
| 126E:0489 | mov BX,CS | BX = CS for function pointer segment pairing. |
| 126E:048B | test DL,0x80 | Check character-device bit from IOCTL result. |
| 126E:048E | jne short 0x04A1 | If device, branch to set D7B2 type. |
| 126E:04A1 | mov word ptr DS:[DI+2],0xD7B2 | Force type marker to D7B2. |
| 126E:04A6 | mov word ptr DS:[DI+0x14],AX | Store function offset A. |
| 126E:04A9 | mov word ptr DS:[DI+0x16],CS | Store function segment A. |
| 126E:04AC | mov word ptr DS:[DI+0x18],CX | Store function offset B. |
| 126E:04AF | mov word ptr DS:[DI+0x1A],BX | Store function segment B. |
| 126E:04B2 | mov word ptr DS:[DI+0x1C],0x058F | Store third function offset. |
| 126E:04B7 | mov word ptr DS:[DI+0x1E],CS | Store third function segment. |
| 126E:04BA | xor AX,AX | Return success status AX=0. |
| 126E:04BC | pop DS | Restore DS. |
| 126E:04BD | ret far 4 | Return to callback caller. |

## 1235:0000 Trampoline/Low-Memory Helper

| Address | Instruction | Meaning |
|---|---|---|
| 1235:0000 | push BP | Prologue. |
| 1235:0001 | mov BP,SP | Stack frame. |
| 1235:0003 | xor AX,AX | AX = 0 to access IVT/low memory through ES=0. |
| 1235:0005 | mov ES,AX | ES = 0000h. |
| 1235:0007 | mov BX,0x03CA | Target low-memory slot base. |
| 1235:000A | mov AX,word ptr SS:[BP+4] | Load far-call argument. |
| 1235:000D | mov word ptr ES:[BX],AX | Store argument at low-memory slot. |
| 1235:0010 | sub BX,2 | Move to previous word slot. |
| 1235:0013 | mov AX,word ptr SS:[BP+2] | Load return address word from stack. |
| 1235:0016 | add AX,9 | Bias return offset by 9 bytes. |
| 1235:0019 | mov word ptr ES:[BX],AX | Store adjusted return offset. |
| 1235:001C | mov AX,DS | AX = current DS. |
| 1235:001E | sub BX,2 | Move to previous word slot. |
| 1235:0021 | mov word ptr ES:[BX],AX | Store DS into low-memory slot. |
| 1235:0024 | pop BP | Epilogue. |
| 1235:0025 | ret far | Return to caller stub. |

## 109E:0000 Terminate-And-Stay-Resident Helper

| Address | Instruction | Meaning |
|---|---|---|
| 109E:0000 | push BP | Prologue. |
| 109E:0001 | mov BP,SP | Stack frame. |
| 109E:0003 | mov AX,word ptr DS:[0x0032] | Reload saved segment reference. |
| 109E:0006 | mov ES,AX | ES = saved segment. |
| 109E:0008 | mov DX,word ptr ES:[2] | Load top/end reference from saved context. |
| 109E:000D | sub DX,AX | Compute paragraphs to keep resident. |
| 109E:000F | mov AL,byte ptr SS:[BP+6] | Load return code argument (from caller push AX=3). |
| 109E:0012 | mov AH,0x31 | DOS Terminate and Stay Resident function. |
| 109E:0014 | int 0x21 | Commit TSR: keep DX paragraphs, return code AL. |

## Final Interpretation

The live CFG lines show a complete TSR install lifecycle:

1. Main setup computes resident memory footprint.
2. Existing vectors are backed up.
3. New vectors for critical handlers are installed.
4. Internal context blocks are built and wired with callback table entries.
5. File/device strategy is selected through INT 21h open/create and IOCTL probes.
6. Control returns to caller, low-memory trampoline metadata is written.
7. INT 21h AH=31h is executed to stay resident.

This is consistent with a small TSR whose primary role is interrupt interception and startup context preparation before the main game executable continues.
