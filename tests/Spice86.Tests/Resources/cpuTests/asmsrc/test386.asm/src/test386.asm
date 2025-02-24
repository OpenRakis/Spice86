;
;   test386.asm
;   Copyright (C) 2012-2015 Jeff Parsons <Jeff@pcjs.org>
;   Copyright (C) 2017-2021 Marco Bortolin <barotto@gmail.com>
;
;   This file is a derivative work of PCjs
;   https://www.pcjs.org/software/pcx86/test/cpu/80386/test386.asm
;
;   test386.asm is free software: you can redistribute it and/or modify it under
;   the terms of the GNU General Public License as published by the Free
;   Software Foundation, either version 3 of the License, or (at your option)
;   any later version.
;
;   test386.asm is distributed in the hope that it will be useful, but WITHOUT ANY
;   WARRANTY without even the implied warranty of MERCHANTABILITY or FITNESS
;   FOR A PARTICULAR PURPOSE.  See the GNU General Public License for more
;   details.
;
;   You should have received a copy of the GNU General Public License along with
;   test386.asm.  If not see <http://www.gnu.org/licenses/gpl.html>.
;
;   This program was originally developed for IBMulator
;   https://barotto.github.io/IBMulator
;
;   Overview
;   --------
;   This file is designed to run as a test ROM, loaded in place of the BIOS.
;   Its pourpose is to test the CPU, reporting its status to the POST port and
;   its computational results in ASCII form to various configurable ports.
;   A 80386 or later CPU is required. This ROM is designed to test an emulator
;   CPU and was never tested on real hardware.
;
;   It must be installed at physical address 0xf0000 and aliased at physical
;   address 0xffff0000.  The jump at resetVector should align with the CPU reset
;   address 0xfffffff0, which will transfer control to f000:0045.  From that
;   point on, all memory accesses should remain within the first 1MB.
;

;
; WARNING
;
;   A word of caution before you start developing.
;   NASM (2.11.08) generates [ebp + ebp] for [ebp*2] (i.e. no base register),
;   which are not the same thing: [ebp+ebp] references the SS segment, [ebp*2]
;   references the DS segment.
;   NASM developers think [ebp*2] and [ebp+ebp] are the same, but that is true
;   only assuming a flat memory model. Until the time NASM authors realize their
;   mistake (any assumption of a flat memory model should be optional), you can
;   disable this behaviour by writing: [nosplit ebp*2].
;
;	NASM Assembly            Translated               Assembled
;	mov eax,[ebp*2]          mov eax,[ebp+ebp*1+0x0]  8B442D00
;	mov eax,[nosplit ebp*2]  mov eax,[ebp*2+0x0]      8B046D00000000
;

%define COPYRIGHT 'test386.asm (C) 2012-2015 Jeff Parsons, (C) 2017-2021 Marco Bortolin '
%define RELEASE   '??/??/21'

	cpu 386
	section .text

	%include "configuration.asm"
	%include "x86_e.asm"
	%include "macros_m.asm"

	bits 16

;
; memory map:
;  00000-003FF real mode IDT
;  00400-004FF protected mode IDT
;  00500-0077F protected mode GDT
;  00800-00FFF protected mode LDT
;  01000-01FFF page directory
;  02000-02FFF page table 0
;  03000-03FFF page table 1
;  04000-04FFF TSS
;  10000-1FFFF stack
;  20000-9FFFF tests
;

TEST_BASE  equ 0x20000
%assign TEST_BASE1 TEST_BASE+0x00000
%assign TEST_BASE2 TEST_BASE+0x40000

;
;   Real mode segments
;
C_SEG_REAL   equ 0xf000
S_SEG_REAL   equ 0x1000
IDT_SEG_REAL equ 0x0040
GDT_SEG_REAL equ 0x0050
GDT_SEG_LIMIT equ 0x2FF
%assign D1_SEG_REAL TEST_BASE1 >> 4
%assign D2_SEG_REAL TEST_BASE2 >> 4

ESP_REAL    equ 0xffff


header:
	db COPYRIGHT

cpuTest:
	cli


; ==============================================================================
;	Real mode tests
; ==============================================================================

%include "real_m.asm"
;-------------------------------------------------------------------------------
	POST 0
;-------------------------------------------------------------------------------
;
;   Real mode initialisation
;
	initRealModeIDT
	mov ax, S_SEG_REAL
	mov ss, ax
	mov sp, ESP_REAL
	mov dx, D1_SEG_REAL
	mov ds, dx
	mov dx, D2_SEG_REAL
	mov es, dx


;-------------------------------------------------------------------------------
	POST 1
;-------------------------------------------------------------------------------
;
;   Conditional jumps
;
%include "tests/jcc_m.asm"
	testJcc 8
	testJcc 16

;
;   Loops
;
%include "tests/loop_m.asm"
	testLoop
	testLoopZ
	testLoopNZ


;-------------------------------------------------------------------------------
	POST 2
;-------------------------------------------------------------------------------
;
;   Quick tests of unsigned 32-bit multiplication and division
;   Thorough arithmetical and logical tests are done later
;
	mov    eax, 0x80000001
	imul   eax
	mov    eax, 0x44332211
	mov    ebx, eax
	mov    ecx, 0x88776655
	mul    ecx
	div    ecx
	cmp    eax, ebx
	jne    error


%include "tests/mov_m.asm"
;-------------------------------------------------------------------------------
	POST 3
;-------------------------------------------------------------------------------
;
;   Move segment registers in real mode
;
	testMovSegR_real ss
	testMovSegR_real ds
	testMovSegR_real es
	testMovSegR_real fs
	testMovSegR_real gs
	testMovSegR_real cs

	advTestSegReal


%include "tests/string_m.asm"
;-------------------------------------------------------------------------------
	POST 4
;-------------------------------------------------------------------------------
;
;   Test store, move, scan, and compare string data
;
	testStringOps b,0,a16
	testStringOps w,0,a16
	testStringOps d,0,a16
	testStringOps b,1,a16
	testStringOps w,1,a16
	testStringOps d,1,a16
	testStringReps b,0,a16
	testStringReps w,0,a16
	testStringReps d,0,a16
	testStringReps b,1,a16
	testStringReps w,1,a16
	testStringReps d,1,a16

	advTestSegReal


%include "tests/call_m.asm"
;-------------------------------------------------------------------------------
	POST 5
;-------------------------------------------------------------------------------
;
;   Calls in real mode
;
	mov    si, 0
	testCallNear sp
	testCallFar C_SEG_REAL

	advTestSegReal


%include "tests/load_ptr_m.asm"
;-------------------------------------------------------------------------------
	POST 6
;-------------------------------------------------------------------------------
;
;   Load full pointer in real mode
;
	mov    di, 0
	testLoadPtr ss
	testLoadPtr ds
	testLoadPtr es
	testLoadPtr fs
	testLoadPtr gs

	advTestSegReal

%if TEST_PMODE

; ==============================================================================
;	Protected mode tests
; ==============================================================================

;-------------------------------------------------------------------------------
	POST 8
;-------------------------------------------------------------------------------
;
;   GDT, LDT, PDT, and PT setup, enter protected mode
;
	jmp initGDT

ESP_R0_PROT equ 0x0000FFFF
ESP_R3_PROT equ 0x00007FFF


%include "protected_m.asm"


;;; support for ROM based GDT (currently unused)
romGDT:
romGDTEnd:
romGDTaddr:
	dw romGDTEnd - romGDT - 1 ; 16-bit limit
	dw romGDT, 0x000f         ; 32-bit base address
;;;

ptrGDTreal: ; pointer to the pmode GDT for real mode code
	dd 0             ; 32-bit offset
	dw GDT_SEG_REAL  ; 16-bit segment selector
ptrIDTreal: ; pointer to the pmode IDT for real mode code
	dd 0
	dw IDT_SEG_REAL

initGDT:
	; the first descriptor in the GDT is always a dud (the null selector)
	defGDTDesc NULL
	defGDTDesc C_SEG_PROT16,  0x000f0000,0x0000ffff,ACC_TYPE_CODE_R|ACC_PRESENT
	defGDTDesc C_SEG_PROT32,  0x000f0000,0x0000ffff,ACC_TYPE_CODE_R|ACC_PRESENT,EXT_32BIT
	defGDTDesc CU_SEG_PROT32, 0x000f0000,0x0000ffff,ACC_TYPE_CODE_R|ACC_PRESENT|ACC_DPL_3,EXT_32BIT
	defGDTDesc CC_SEG_PROT32, 0x000f0000,0x0000ffff,ACC_TYPE_CODE_R|ACC_TYPE_CONFORMING|ACC_PRESENT|EXT_32BIT
	defGDTDesc IDT_SEG_PROT,  0x00000400,0x000000ff,ACC_TYPE_DATA_W|ACC_PRESENT
	defGDTDesc IDTU_SEG_PROT, 0x00000400,0x000000ff,ACC_TYPE_DATA_W|ACC_PRESENT|ACC_DPL_3
	defGDTDesc GDT_DSEG_PROT, 0x00000500,0x000002ff,ACC_TYPE_DATA_W|ACC_PRESENT
	defGDTDesc GDTU_DSEG_PROT,0x00000500,0x000002ff,ACC_TYPE_DATA_W|ACC_PRESENT|ACC_DPL_3
	defGDTDesc LDT_SEG_PROT,  0x00000800,0x000007ff,ACC_TYPE_LDT|ACC_PRESENT
	defGDTDesc LDT_DSEG_PROT, 0x00000800,0x000007ff,ACC_TYPE_DATA_W|ACC_PRESENT
	defGDTDesc PG_SEG_PROT,   0x00001000,0x00002fff,ACC_TYPE_DATA_W|ACC_PRESENT
	defGDTDesc S_SEG_PROT32,  0x00010000,0x0008ffff,ACC_TYPE_DATA_W|ACC_PRESENT,EXT_32BIT
	defGDTDesc SU_SEG_PROT32, 0x00010000,0x0008ffff,ACC_TYPE_DATA_W|ACC_PRESENT|ACC_DPL_3,EXT_32BIT
	defGDTDesc TSS_PROT,      0x00004000,0x00000fff,ACC_TYPE_TSS|ACC_PRESENT|ACC_DPL_3
	defGDTDesc TSS_DSEG_PROT, 0x00004000,0x00000fff,ACC_TYPE_DATA_W|ACC_PRESENT
	defGDTDesc FLAT_SEG_PROT, 0x00000000,0xffffffff,ACC_TYPE_DATA_W|ACC_PRESENT
	defGDTDesc RING0_GATE ; placeholder for a call gate used to switch to ring 0

	jmp initIDT

ptrIDTprot: ; pointer to the IDT for pmode
	dd 0             ; 32-bit offset
	dw IDT_SEG_PROT  ; 16-bit segment selector
ptrIDTUprot: ; pointer to the IDT for pmode
	dd 0             ; 32-bit offset
	dw IDTU_SEG_PROT  ; 16-bit segment selector
ptrGDTprot: ; pointer to the GDT for pmode (kernel mode data segment)
	dd 0
	dw GDT_DSEG_PROT
ptrGDTUprot: ; pointer to the GDT for pmode (user mode data segment)
	dd 0
	dw GDTU_DSEG_PROT
ptrLDTprot: ; pointer to the LDT for pmode
	dd 0
	dw LDT_DSEG_PROT
ptrPDprot: ; pointer to the Page Directory for pmode
	dd 0
	dw PG_SEG_PROT
ptrPT0prot: ; pointer to Page Table 0
	dd 0x1000
	dw PG_SEG_PROT
ptrPT1prot: ; pointer to Page Table 1
	dd 0x2000
	dw PG_SEG_PROT
ptrSSprot: ; pointer to the stack for pmode
	dd ESP_R0_PROT
	dw S_SEG_PROT32
ptrTSSprot: ; pointer to the task state segment
	dd 0
	dw TSS_DSEG_PROT
addrProtIDT: ; address of pmode IDT to be used with lidt
	dw 0xFF              ; 16-bit limit
	dd IDT_SEG_REAL << 4 ; 32-bit base address
addrGDT: ; address of GDT to be used with lgdt
	dw GDT_SEG_LIMIT
	dd GDT_SEG_REAL << 4

; Initializes an interrupt gate in system memory in real mode
initIntGateReal:
	pushad
	initIntGate
	popad
	ret

initIDT:
	lds    ebx, [cs:ptrIDTreal]
	mov    esi, C_SEG_PROT32
	mov    edi, DefaultExcHandler
	mov    dx,  ACC_DPL_0
%assign vector 0
%rep    0x15
	mov    eax, vector
	call   initIntGateReal
%assign vector vector+1
%endrep

	jmp initPaging

initPaging:
;
; pages:
;  00000-00FFF   1  1000h   4K IDTs, GDT and LDT
;  01000-01FFF   1  1000h   4K page directory
;  02000-02FFF   1  1000h   4K page table 0
;  03000-03FFF   1  1000h   4K page table 1
;  04000-04FFF   1  1000h   4K task switch segments
;  05000-0FFFF  11  c000h  44K free
;  10000-1FFFF  16 10000h  64K stack
;  20000-9EFFF 127 7f000h 508K tests
;  9F000-9FFFF   1  1000h   4K used for page faults (PTE 9Fh)
;
PAGE_DIR_ADDR equ 0x1000
PAGE_TBL0_ADDR equ PAGE_DIR_ADDR+0x1000
PAGE_TBL1_ADDR equ PAGE_DIR_ADDR+0x2000

;   Now we want to build a page directory and a page table. We need two pages of
;   4K-aligned physical memory.  We use a hard-coded address, segment 0x100,
;   corresponding to physical address 0x1000.
;
	mov   esi, PAGE_DIR_ADDR
	mov   eax, esi
	shr   eax, 4
	mov   es,  eax
;
;   Build a page directory at ES:EDI (0100:0000) with only 1 valid PDE (the first one),
;   because we're not going to access any memory outside the first 1MB.
;
	cld
	mov   eax, PAGE_TBL0_ADDR | PTE_PRESENT | PTE_USER | PTE_WRITE
	xor   edi, edi
	stosd
	mov   ecx, 1024-1 ; ECX == number of (remaining) PDEs to write
	xor   eax, eax    ; fill remaining PDEs with 0
	rep   stosd
;
;   Build a page table at EDI with 256 (out of 1024) valid PTEs, mapping the first 1MB
;   as linear == physical.
;
	mov   eax, PTE_PRESENT | PTE_WRITE | PTE_USER
	mov   ecx, 256 ; ECX == number of PTEs to write
.initPT:
	stosd
	add   eax, 0x1000
	loop  .initPT
	mov   ecx, 1024-256 ; ECX == number of (remaining) PTEs to write
	xor   eax, eax
	rep   stosd

switchToProtMode:
	cli ; make sure interrupts are off now, since we've not initialized the IDT yet
	o32 lidt [cs:addrProtIDT]
	o32 lgdt [cs:addrGDT]
	mov    cr3, esi
	mov    eax, cr0
	or     eax, CR0_MSW_PE | CR0_PG
	mov    cr0, eax
	jmp    C_SEG_PROT32:toProt32 ; jump to flush the prefetch queue
toProt32:
	bits 32
	jmp    initLDT

%include "protected_p.asm"

initLDT:
	defLDTDesc D_SEG_PROT16,   TEST_BASE, 0x000fffff,ACC_TYPE_DATA_W|ACC_PRESENT
	defLDTDesc D_SEG_PROT32,   TEST_BASE, 0x000fffff,ACC_TYPE_DATA_W|ACC_PRESENT,EXT_32BIT
	defLDTDesc DU_SEG_PROT,    TEST_BASE, 0x000fffff,ACC_TYPE_DATA_W|ACC_PRESENT|ACC_DPL_3
	defLDTDesc D1_SEG_PROT,    TEST_BASE1,0x000fffff,ACC_TYPE_DATA_W|ACC_PRESENT
	defLDTDesc D2_SEG_PROT,    TEST_BASE2,0x000fffff,ACC_TYPE_DATA_W|ACC_PRESENT
	defLDTDesc DC_SEG_PROT32,  TEST_BASE1,0x000fffff,ACC_TYPE_CODE_R|ACC_PRESENT,EXT_32BIT
	defLDTDesc RO_SEG_PROT,    TEST_BASE, 0x000fffff,ACC_TYPE_DATA_R|ACC_PRESENT
	defLDTDesc ROU_SEG_PROT,   TEST_BASE, 0x000fffff,ACC_TYPE_DATA_R|ACC_PRESENT|ACC_DPL_3
	defLDTDesc DTEST_SEG_PROT, TEST_BASE, 0x000fffff,ACC_TYPE_DATA_W|ACC_PRESENT,EXT_32BIT
	defLDTDesc DPL1_SEG_PROT,  TEST_BASE, 0x000fffff,ACC_TYPE_DATA_W|ACC_PRESENT|ACC_DPL_1
	defLDTDesc NP_SEG_PROT,    TEST_BASE, 0x000fffff,ACC_TYPE_DATA_W
	defLDTDesc SYS_SEG_PROT,   TEST_BASE, 0x000fffff,ACC_PRESENT

	mov  ax, LDT_SEG_PROT
	lldt ax
	mov  ax, TSS_PROT
	ltr  ax
	jmp  protTests

%include "tss_p.asm"
%include "protected_rings_p.asm"

protTests:

%include "tests/stack_m.asm"

;-------------------------------------------------------------------------------
	POST 9
;-------------------------------------------------------------------------------
;
;   Stack functionality
;
;
;   For the next tests, with a 16-bit data segment in SS, we
;   expect all pushes/pops will occur at SP rather than ESP.
;
	mov    ax, D_SEG_PROT16
	mov    ds, ax
	mov    ss, ax
	mov    es, ax
	mov    fs, ax
	mov    gs, ax

	;
	; general purpose registers
	;
	testPushPopR ax,16
	testPushPopR bx,16
	testPushPopR cx,16
	testPushPopR dx,16
	testPushPopR sp,16
	testPushPopR bp,16
	testPushPopR si,16
	testPushPopR di,16

	testPushPopAll16 16
	testPushPopAll32 16

	;
	; segment registers
	;
	testPushPopSR cs,16
	testPushPopSR ds,16
	testPushPopSR ss,16
	testPushPopSR es,16
	testPushPopSR fs,16
	testPushPopSR gs,16

	;
	; memory
	;
	testPushPopM 16
	testPushImm 16

	;
	; flags
	;
	testPushPopF 16

;
;   Now use a 32-bit stack address size.
;   All pushes/pops will occur at ESP rather than SP.
;
	mov    ax,  D_SEG_PROT32
	mov    ss,  ax

	testPushPopR ax,32
	testPushPopR bx,32
	testPushPopR cx,32
	testPushPopR dx,32
	testPushPopR bp,32
	testPushPopR sp,32
	testPushPopR si,32
	testPushPopR di,32

	testPushPopAll16 32
	testPushPopAll32 32

	testPushPopSR cs,32
	testPushPopSR ds,32
	testPushPopSR ss,32
	testPushPopSR es,32
	testPushPopSR fs,32
	testPushPopSR gs,32

	testPushPopM 32
	testPushImm 32

	testPushPopF 32


	; the stack works
	; initialize it for the next tests
	loadProtModeStack

	advTestSegProt


;-------------------------------------------------------------------------------
	POST A
;-------------------------------------------------------------------------------
;
;   Test user mode (ring 3) switching
;
	call   clearTSS
	mov    ax, D_SEG_PROT32
	mov    ds, ax
	mov    es, ax
	mov    fs, ax
	mov    gs, ax
	call   switchToRing3
	; CS must be CU_SEG_PROT32|3 (CPL=3)
	mov    ax, cs
	cmp    ax, CU_SEG_PROT32|3
	jne    error
	; data segments must be NULL
	mov    ax, ds
	cmp    ax, 0
	jne    error
	mov    ax, es
	cmp    ax, 0
	jne    error
	mov    ax, fs
	cmp    ax, 0
	jne    error
	mov    ax, gs
	cmp    ax, 0
	jne    error
	; test some privileged ops
	protModeFaultTest EX_GP, 0, cli
	protModeFaultTest EX_GP, 0, hlt
	; switch back to ring 0
	call switchToRing0
	; CS must be C_SEG_PROT32|0 (CPL=0)
	mov    ax, cs
	cmp    ax, C_SEG_PROT32
	jne    error


;-------------------------------------------------------------------------------
	POST B
;-------------------------------------------------------------------------------
;
;   Test of moving segment registers in protected mode
;
	testMovSegR_prot ds
	testMovSegR_prot es
	testMovSegR_prot fs
	testMovSegR_prot gs
	testMovSegR_prot cs
	testMovSegR_prot ss

	loadProtModeStack
	advTestSegProt


;-------------------------------------------------------------------------------
	POST C
;-------------------------------------------------------------------------------
;
;   Zero and sign-extension tests
;
	movsx  eax, byte [cs:signedByte] ; byte to a 32-bit register with sign-extension
	cmp    eax, 0xffffff80
	jne    error

	movsx  eax, word [cs:signedWord] ; word to a 32-bit register with sign-extension
	cmp    eax, 0xffff8080
	jne    error

	movzx  eax, byte [cs:signedByte] ; byte to a 32-bit register with zero-extension
	cmp    eax, 0x00000080
	jne    error

	movzx  eax, word [cs:signedWord] ; word to a 32-bit register with zero-extension
	cmp    eax, 0x00008080
	jne    error

	push   byte -128       ; NASM will not use opcode 0x6A ("PUSH imm8") unless we specify "byte"
	pop    ebx             ; verify EBX == 0xFFFFFF80
	cmp    ebx, 0xFFFFFF80
	jne    error

	and    ebx, 0xff       ; verify EBX == 0x00000080
	cmp    ebx, 0x00000080
	jne    error

	movsx  bx, bl          ; verify EBX == 0x0000FF80
	cmp    ebx, 0x0000FF80
	jne    error

	movsx  ebx, bx         ; verify EBX == 0xFFFFFF80
	cmp    ebx, 0xFFFFFF80
	jne    error

	movzx  bx,  bl         ; verify EBX == 0xFFFF0080
	cmp    ebx, 0xFFFF0080
	jne    error

	movzx  ebx, bl         ; verify EBX == 0x00000080
	cmp    ebx, 0x00000080
	jne    error

	not    ebx             ; verify EBX == 0xFFFFFF7F
	cmp    ebx,0xFFFFFF7F
	jne    error

	movsx  bx, bl          ; verify EBX == 0xFFFF007F
	cmp    ebx, 0xFFFF007F
	jne    error

	movsx  ebx, bl         ; verify EBX == 0x0000007F
	cmp    ebx, 0x0000007F
	jne    error

	not    ebx             ; verify EBX == 0xFFFFFF80
	cmp    ebx, 0xFFFFFF80
	jne    error

	movzx  ebx, bx         ; verify EBX == 0x0000FF80
	cmp    ebx, 0x0000FF80
	jne    error

	movzx  bx, bl          ; verify EBX == 0x00000080
	cmp    ebx,0x00000080
	jne    error

	movsx  bx, bl
	neg    bx
	neg    bx
	cmp    ebx, 0x0000FF80
	jne    error

	movsx  ebx, bx
	neg    ebx
	neg    ebx
	cmp    ebx, 0xFFFFFF80
	jne    error


	jmp postD
%include "tests/lea_m.asm"
%include "tests/lea_p.asm"
postD:
;-------------------------------------------------------------------------------
	POST D
;-------------------------------------------------------------------------------
;
;   16-bit addressing modes (LEA)
;
	mov ax, 0x0001
	mov bx, 0x0002
	mov cx, 0x0004
	mov dx, 0x0008
	mov si, 0x0010
	mov di, 0x0020
	testLEA16 [0x4000],0x4000
	testLEA16 [bx], 0x0002
	testLEA16 [si], 0x0010
	testLEA16 [di], 0x0020
	testLEA16 [bx + 0x40], 0x0042
	testLEA16 [si + 0x40], 0x0050
	testLEA16 [di + 0x40], 0x0060
	testLEA16 [bx + 0x4000], 0x4002
	testLEA16 [si + 0x4000], 0x4010
	testLEA16 [bx + si], 0x0012
	testLEA16 [bx + di], 0x0022
	testLEA16 [bx + 0x40 + si], 0x0052
	testLEA16 [bx + 0x40 + di], 0x0062
	testLEA16 [bx + 0x4000 + si], 0x4012
	testLEA16 [bx + 0x4000 + di], 0x4022


;-------------------------------------------------------------------------------
	POST E
;-------------------------------------------------------------------------------
;
;   32-bit addressing modes (LEA)
;
	call testAddressing32

	advTestSegProt


;-------------------------------------------------------------------------------
	POST F
;-------------------------------------------------------------------------------
;
;   Access memory using various addressing modes
;
	; store a known word at the scratch address
	mov    ebx, 0x11223344
	mov    [0x10000], ebx

	; now access that scratch address using various addressing modes
	mov    ecx, 0x10000
	cmp    [ecx], ebx
	jne    error

	add    ecx, 64
	cmp    [ecx-64], ebx
	jne    error

	sub    ecx, 64
	shr    ecx, 1
	cmp    [ecx+0x8000], ebx
	jne    error

	cmp    [ecx+ecx], ebx
	jne    error

	shr    ecx, 1
	cmp    [ecx+ecx*2+0x4000], ebx
	jne    error

	cmp    [ecx*4], ebx
	jne    error

	; test default segment SS
	mov    ebp, ecx
	cmp    [ebp+ecx*2+0x4000], ebx ; EBP is used as base so the default segment is SS
	je     error ; since SS != DS, this better be a mismatch

	mov    eax, esp ; save ESP
	mov    esp, ecx
	cmp    [esp+ecx*2+0x4000], ebx ; ESP is used as base so the default segment is SS
	je     error ; since SS != DS, this better be a mismatch
	mov    esp, eax ; restore ESP

	mov    ax, D1_SEG_PROT
	mov    dx, D2_SEG_PROT

	; store the known word in a different segment
	mov    ds, dx
	mov    [0x10000], ebx
	mov    ds, ax

	; test segment overrides
	mov    es, dx
	cmp    [es:ecx+ecx*2+0x4000], ebx
	jne    error
	mov    es, ax
	mov    fs, dx
	cmp    [fs:ecx+ecx*2+0x4000], ebx
	jne    error
	mov    fs, ax
	mov    gs, dx
	cmp    [gs:ecx+ecx*2+0x4000], ebx
	jne    error
	mov    gs, ax
	mov    ss, dx
	cmp    [ss:ecx+ecx*2+0x4000], ebx
	jne    error
	mov    dx, S_SEG_PROT32
	mov    ss, dx
	cmp    [ds:ebp+ecx*2+0x4000], ebx
	jne    error

	advTestSegProt


;-------------------------------------------------------------------------------
	POST 10
;-------------------------------------------------------------------------------
;
;   Store, move, scan, and compare string data in protected mode
;
	pushad
	pushfd
	testStringOps b,0,a32
	testStringOps w,0,a32
	testStringOps d,0,a32
	testStringOps b,1,a32
	testStringOps w,1,a32
	testStringOps d,1,a32
	testStringReps b,0,a32
	testStringReps w,0,a32
	testStringReps d,0,a32
	testStringReps b,1,a32
	testStringReps w,1,a32
	testStringReps d,1,a32
	popfd
	popad

	advTestSegProt


	jmp post11
%include "tests/paging_m.asm"
%include "tests/paging_p.asm"
post11:
;-------------------------------------------------------------------------------
	POST 11
;-------------------------------------------------------------------------------
;
;	Verify page faults and PTE bits
;
	setProtModeIntGate EX_PF, PageFaultHandler
	; set test data segment to DPL 3 R/W flat
	updLDTDesc DTEST_SEG_PROT, 0x0000000, 0x000fffff, ACC_TYPE_DATA_W|ACC_PRESENT|ACC_DPL_3, EXT_PAGE
	; setup page table 1
	cld
	les   ebx, [cs:ptrPDprot]
	mov   [es:ebx + 4], dword PAGE_TBL1_ADDR ; PDE 1 -> page table 1
	les   ebx, [cs:ptrPT1prot]
	mov   edi, ebx
	xor   eax, eax
	mov   ecx, 1024
	rep   stosd
	mov   [es:ebx + (TESTPAGE_PTE&0x3FF)*4], dword TESTPAGE_OFF

	mov   eax, pagingTestsEnd-pagingTests
	mov   esi, pagingTests
	mov   edx, 0
	mov   ecx, 3
	div   ecx
	mov   ecx, eax
.nextTest:
	movzx eax, byte [cs:esi + 0]
	movzx ebx, byte [cs:esi + 1]
	movzx edx, byte [cs:esi + 2]
	call  testPageFault
	add   esi, 3
	loop .nextTest

	setProtModeIntGate EX_PF, DefaultExcHandler

	; test if the Dirty bit is updated after a read
	updPageFlags TESTPAGE_PDE, PTE_SUPER_W
	updPageFlags TESTPAGE_PTE, PTE_SUPER_W
	mov   ax, DTEST_SEG_PROT
	mov   ds, ax
	mov   eax, [TESTPAGE_LIN]
	mov   [TESTPAGE_LIN], eax
	mov   eax, TESTPAGE_LIN
	call  getPTE
	test  eax, PTE_DIRTY
	jz    error

	mov ax, D1_SEG_PROT
	mov ds, ax


;-------------------------------------------------------------------------------
	POST 12
;-------------------------------------------------------------------------------
;
;   Verify other memory access faults
;
	; #GP(0) If the destination operand is in a non-writable segment.
	mov ax, RO_SEG_PROT ; write protect DS
	mov ds, ax
	protModeFaultTest EX_GP, 0, mov [0],eax
	; #GP(0) If a memory operand effective address is outside the CS, DS, ES, FS, or GS segment limit.
	; use byte granular DS
	updLDTDesc DTEST_SEG_PROT, 0x0000000, 0x0009ffff, ACC_TYPE_DATA_W|ACC_PRESENT
	mov ax, DTEST_SEG_PROT
	mov ds, ax
	protModeFaultTest EX_GP, 0, mov eax,[0x9fffd] ; check on read
	protModeFaultTest EX_GP, 0, mov [0x9fffd],eax ; check on write
	mov eax,[0x9fffc] ; this should be ok
	mov [0x9fffc],eax ; this should be ok
	protModeFaultTest EX_GP, 0, mov eax,[-1] ; check on read (test for overflows)
	protModeFaultTest EX_GP, 0, mov [-1],eax ; check on write (test for overflows)
	; use page granular DS
	updLDTDesc DTEST_SEG_PROT, 0x0000000, 0x0000009f, ACC_TYPE_DATA_W|ACC_PRESENT, EXT_PAGE
	mov ax, DTEST_SEG_PROT
	mov ds, ax
	protModeFaultTest EX_GP, 0, mov eax,[0x9fffd] ; check on read
	protModeFaultTest EX_GP, 0, mov [0x9fffd],eax ; check on write
	mov eax,[0x9fffc] ; this should be ok
	mov [0x9fffc],eax ; this should be ok
	; #SS(0) If a memory operand effective address is outside the SS segment limit.
	protModeFaultTest EX_SS, 0, mov eax,[ss:-1] ; check on read
	protModeFaultTest EX_SS, 0, mov [ss:-1],eax ; check on write
	; #UD If the LOCK prefix is used.
	protModeFaultTest EX_UD, 0, lock mov [0],eax

	mov ax, D1_SEG_PROT
	mov ds, ax


%include "tests/bit_m.asm"
;-------------------------------------------------------------------------------
	POST 13
;-------------------------------------------------------------------------------
;
;   Verify Bit Scan operations
;
	testBitscan bsf
	testBitscan bsr


;-------------------------------------------------------------------------------
	POST 14
;-------------------------------------------------------------------------------
;
;   Verify Bit Test operations
;
	testBittest16 bt
	testBittest16 btc
	cmp edx, 0x00005555
	jne error
	testBittest16 btr
	cmp edx, 0
	jne error
	testBittest16 bts
	cmp edx, 0x0000ffff
	jne error

	testBittest32 bt
	testBittest32 btc
	cmp edx, 0x55555555
	jne error
	testBittest32 btr
	cmp edx, 0
	jne error
	testBittest32 bts
	cmp edx, 0xffffffff
	jne error


%include "tests/setcc_m.asm"
;-------------------------------------------------------------------------------
	POST 15
;-------------------------------------------------------------------------------
;
;   Verify Byte set on condition (SETcc)
;
	testSetcc bl
	testSetcc byte [0x10000]

	advTestSegProt


;-------------------------------------------------------------------------------
	POST 16
;-------------------------------------------------------------------------------
;
;	Calls in protected mode
;
	mov si, 0
	testCallNear esp
	testCallFar C_SEG_PROT32

	advTestSegProt


;-------------------------------------------------------------------------------
	POST 17
;-------------------------------------------------------------------------------
;
;	Adjust RPL Field of Selector (ARPL)
;
	; test on register destination
	xor ax, ax       ; ZF = 0
	mov ax, 0xfff0
	mov bx, 0x0002
	arpl ax, bx      ; RPL ax < RPL bx
	jnz error        ; must be ZF = 1
	cmp ax, 0xfff2
	jne error
	; test on memory destination
	xor ax, ax       ; ZF = 0
	mov word [0x20000], 0xfff0
	arpl [0x20000], bx
	jnz error
	cmp word [0x20000], 0xfff2
	jne error
	%if BOCHS = 0
	; test unexpected memory write
	;
	; This test fails with Bochs, which does not write to memory (correctly),
	; but throws a #GP fault before that, during the reading of the memory
	; operand. Bochs checks that the destination segment is writeable before the
	; execution of ARPL.
	;
	; make DS read only
	updLDTDescAcc D1_SEG_PROT,ACC_TYPE_DATA_R|ACC_PRESENT
	mov ax, D1_SEG_PROT
	mov ds, ax
	xor eax, eax
	arpl [0x20000], bx ; value has not changed, arpl should not write to memory
	; make DS writeable again
	updLDTDescAcc D1_SEG_PROT,ACC_TYPE_DATA_W|ACC_PRESENT
	mov ax, D1_SEG_PROT
	mov ds, ax
	%endif
	; test with RPL dest > RPL src
	xor ax, ax       ; ZF = 0
	mov ax, 0xfff3
	arpl ax, bx
	jz error
	cmp ax, 0xfff3
	jne error

	advTestSegProt


;-------------------------------------------------------------------------------
	POST 18
;-------------------------------------------------------------------------------
;
;	Check Array Index Against Bounds (BOUND)
;
	setProtModeIntGate EX_BR, BoundExcHandler
	xor eax, eax
	mov ebx, 0x10100
	mov word [0x20000], 0x0010
	mov word [0x20002], 0x0102
	o16 bound bx, [0x20000]
	cmp eax, BOUND_HANDLER_SIG
	je error
	mov word [0x20002], 0x00FF
	o16 bound bx, [0x20000]
	cmp eax, BOUND_HANDLER_SIG
	jne error
	xor eax, eax
	mov dword [0x20004], 0x10010
	mov dword [0x20008], 0x10102
	o32 bound ebx, [0x20004]
	cmp eax, BOUND_HANDLER_SIG
	je error
	mov dword [0x20008], 0x100FF
	o32 bound ebx, [0x20004]
	cmp eax, BOUND_HANDLER_SIG
	jne error
	setProtModeIntGate EX_BR, DefaultExcHandler

	advTestSegProt
	jmp post19

BoundExcHandler:
	mov word [0x20002], 0x0100
	mov dword [0x20008], 0x10100
	BOUND_HANDLER_SIG equ 0x626f756e
	mov eax, BOUND_HANDLER_SIG
	iretd


%include "tests/xchg_m.asm"
post19:
;-------------------------------------------------------------------------------
	POST 19
;-------------------------------------------------------------------------------
;
;   Exchange Register/Memory with Register (XCHG)
;
	testXchg ax,cx ; 66 91
	testXchg ax,dx ; 66 92
	testXchg ax,bx ; 66 93
	mov bp,sp
	testXchg ax,sp ; 66 94
	mov sp,bp
	testXchg ax,bp ; 66 95
	testXchg ax,si ; 66 96
	testXchg ax,di ; 66 97

	testXchg eax,ecx ; 91
	testXchg eax,edx ; 92
	testXchg eax,ebx ; 93
	mov ebp,esp
	testXchg eax,esp ; 94
	mov esp,ebp
	testXchg eax,ebp ; 95
	testXchg eax,esi ; 96
	testXchg eax,edi ; 97

	testXchg bl,cl         ; 86 D9
	testXchg byte [0],cl   ; 86 0D 00000000
	testXchg bx,cx         ; 66 87 D9
	testXchg word [0],cx   ; 66 87 0D 00000000
	testXchg ebx,ecx       ; 87 D9
	testXchg dword [0],ecx ; 87 0D 00000000

	advTestSegProt


%include "tests/enter_m.asm"
post1A:
;-------------------------------------------------------------------------------
	POST 1A
;-------------------------------------------------------------------------------
;
;   Make Stack Frame for Procedure Parameters (ENTER)
;
	testENTER16 1, 0,16
	testENTER16 2, 1,16
	testENTER16 3, 4,16
	testENTER16 4,36,32
	testENTER32 5, 0,32
	testENTER32 6, 1,32
	testENTER32 7, 4,32
	testENTER32 8,36,16
	; The ENTER instruction causes a page fault whenever a write using the final
	; value of the stack pointer (within the current stack segment) would do so.
	jmp .pageFaultTest
.pageFaultProc:
	call  switchToRing3
	mov   ax, DU_SEG_PROT|3
	mov   ss, ax
	mov   esp, 0x1004
.pageFaultEIP:
	enter 1,0
	jmp   error
.pageFaultTest:
	loadProtModeStack
	updPageFlags 0x1000|(TEST_BASE>>12), PTE_SUPER_W
	protModeFaultTestEx EX_PF, PF_PROT|PF_WRITE|PF_USER, 3, .pageFaultEIP, call .pageFaultProc
	testCPL 0
	mov   ax, D1_SEG_PROT
	mov   ds, ax
	mov   ax, D2_SEG_PROT
	mov   es, ax


%include "tests/leave_m.asm"
post1B:
;-------------------------------------------------------------------------------
	POST 1B
;-------------------------------------------------------------------------------
;
;   High Level Procedure Exit (LEAVE)
;
	testLEAVE o16,16
	testLEAVE o16,32
	testLEAVE o32,16
	testLEAVE o32,32

	loadProtModeStack
	jmp post1C


%include "tests/ver_p.asm"
post1C:
;-------------------------------------------------------------------------------
	POST 1C
;-------------------------------------------------------------------------------
;
;   Verify a Segment for Reading or Writing (VERR/VERW)
;
	; null segment
	testVERR NULL,0
	testVERW NULL,0
	; out of bounds descriptor
	testVERR GDT_SEG_LIMIT+1,0
	testVERW GDT_SEG_LIMIT+1,0
	; system segment
	testVERR LDT_SEG_PROT,0
	testVERW LDT_SEG_PROT,0

	; CPL 0
	; code segment
	testVERR C_SEG_PROT32,1
	testVERW C_SEG_PROT32,0
	; data segment
	testVERR S_SEG_PROT32,1
	testVERW S_SEG_PROT32,1

	; CPL 3
	call  switchToRing3
	; non-conforming code segment
	testVERR C_SEG_PROT32,0  ; DPL0 code segment
	testVERW C_SEG_PROT32,0
	testVERR CU_SEG_PROT32,1 ; DPL3 code segment
	testVERW CU_SEG_PROT32,0
	; conforming code segment
	testVERR CC_SEG_PROT32,1
	testVERW CC_SEG_PROT32,0
	; data segment
	testVERR S_SEG_PROT32,0  ; DPL0 stack segment
	testVERW S_SEG_PROT32,0
	testVERR SU_SEG_PROT32,1 ; DPL3 stack segment
	testVERW SU_SEG_PROT32,1
	testVERR ROU_SEG_PROT,1  ; DPL3 read-only data segment
	testVERW ROU_SEG_PROT,0
	call  switchToRing0


%include "print_init.asm"

	jmp undefTests

%include "print_p.asm"

undefTests:
;-------------------------------------------------------------------------------
	POST E0
;-------------------------------------------------------------------------------
;
;   Undefined behaviours and bugs
;   Results have been validated against 386SX hardware.
;
	mov al, 0
	cmp al, TEST_UNDEF
	je arithLogicTests

	mov al, CPU_FAMILY
	cmp al, 3
	je bcd386FlagsTest
	call printUnkCPU
	jmp error

	%include "tests/bcd_m.asm"

bcd386FlagsTest:
	PS_CAO  equ PS_CF|PS_AF|PS_OF
	PS_PZSO equ PS_PF|PS_ZF|PS_SF|PS_OF

	; AAA
	; undefined flags: PF, ZF, SF, OF
	testBCDflags   aaa, 0x0000, 0,           PS_PF|PS_ZF
	testBCDflags   aaa, 0x0001, PS_PZSO,     0
	testBCDflags   aaa, 0x007A, 0,           PS_CF|PS_AF|PS_SF|PS_OF
	testBCDflags   aaa, 0x007B, PS_AF,       PS_CF|PS_PF|PS_AF|PS_SF|PS_OF
	; AAD
	; undefined flags: CF, AF, OF
	testBCDflags   aad, 0x0001, PS_CAO,      0
	testBCDflags   aad, 0x0D8E, 0,           PS_CAO
	testBCDflags   aad, 0x0106, 0,           PS_AF
	testBCDflags   aad, 0x01F7, 0,           PS_CF|PS_AF
	; AAM
	; undefined flags: CF, AF, OF
	testBCDflags   aam, 0x0000, 0,           PS_ZF|PS_PF
	testBCDflags   aam, 0x0000, PS_CAO,      PS_ZF|PS_PF
	; AAS
	; undefined flags: PF, ZF, SF, OF
	testBCDflags   aas, 0x0000, PS_SF|PS_OF, PS_PF|PS_ZF
	testBCDflags   aas, 0x0000, PS_AF,       PS_CF|PS_PF|PS_AF|PS_SF
	testBCDflags   aas, 0x0001, PS_PZSO,     0
	testBCDflags   aas, 0x0680, PS_AF,       PS_CF|PS_AF|PS_OF
	; DAA
	; undefined flags: OF
	testBCDflags   daa, 0x001A, PS_AF|PS_OF, PS_AF
	testBCDflags   daa, 0x001A, PS_CF,       PS_CF|PS_AF|PS_SF|PS_OF
	; DAS
	; undefined flags: OF
	testBCDflags   das, 0x0080, PS_OF,       PS_SF
	testBCDflags   das, 0x0080, PS_AF,       PS_AF|PS_OF

shifts386FlagsTest:
	%include "tests/shift_m.asm"

	; SHR al,cl - SHR ax,cl
	; undefined flags:
	;  CF when cl>7 (byte) or cl>15 (word):
	;    if byte operand and cl=8 or cl=16 or cl=24 then CF=MSB(operand)
	;    if word operand and cl=16 then CF=MSB(operand)
	;  OF when cl>1: set according to result
	;  AF when cl>0: always 1
	; shift count is modulo 32 so if cl=32 then result is equal to cl=0
	testShiftBFlags   shr, 0x81,   1,  0,     PS_CF|PS_AF|PS_OF
	testShiftBFlags   shr, 0x82,   2,  0,     PS_CF|PS_AF
	testShiftBFlags   shr, 0x80,   8,  0,     PS_CF|PS_PF|PS_AF|PS_ZF
	testShiftBFlags   shr, 0x00,   8,  PS_CF, PS_PF|PS_AF|PS_ZF
	testShiftBFlags   shr, 0x80,   16, 0,     PS_CF|PS_PF|PS_AF|PS_ZF
	testShiftBFlags   shr, 0x00,   16, PS_CF, PS_PF|PS_AF|PS_ZF
	testShiftBFlags   shr, 0x80,   24, 0,     PS_CF|PS_PF|PS_AF|PS_ZF
	testShiftBFlags   shr, 0x00,   24, PS_CF, PS_PF|PS_AF|PS_ZF
	testShiftBFlags   shr, 0x80,   32, 0,     0
	testShiftWFlags   shr, 0x8000, 16, 0,     PS_CF|PS_PF|PS_AF|PS_ZF
	testShiftWFlags   shr, 0x0000, 16, PS_CF, PS_PF|PS_AF|PS_ZF
	testShiftWFlags   shr, 0x8000, 32, 0,     0

	; SHL al,cl - SHL ax,cl
	; undefined flags:
	;  CF when cl>7 (byte) or cl>15 (word):
	;    if byte operand and cl=8 or cl=16 or cl=24 then CF=LSB(operand)
	;    if word operand and cl=16 then CF=LSB(operand)
	;  OF when cl>1: set according to result
	;  AF when cl>0: always 1
	; shift count is modulo 32 so if cl=32 then result is equal to cl=0
	testShiftBFlags   shl, 0x81, 1,  0,     PS_CF|PS_AF|PS_OF
	testShiftBFlags   shl, 0x41, 2,  0,     PS_CF|PS_AF|PS_OF
	testShiftBFlags   shl, 0x01, 8,  0,     PS_CF|PS_PF|PS_AF|PS_ZF|PS_OF
	testShiftBFlags   shl, 0x00, 8,  PS_CF, PS_PF|PS_AF|PS_ZF
	testShiftBFlags   shl, 0x01, 16, 0,     PS_CF|PS_PF|PS_AF|PS_ZF|PS_OF
	testShiftBFlags   shl, 0x00, 16, PS_CF, PS_PF|PS_AF|PS_ZF
	testShiftBFlags   shl, 0x01, 24, 0,     PS_CF|PS_PF|PS_AF|PS_ZF|PS_OF
	testShiftBFlags   shl, 0x00, 24, PS_CF, PS_PF|PS_AF|PS_ZF
	testShiftBFlags   shl, 0x01, 32, 0,     0
	testShiftWFlags   shl, 0x01, 16, 0,     PS_CF|PS_PF|PS_AF|PS_ZF|PS_OF
	testShiftWFlags   shl, 0x00, 16, PS_CF, PS_PF|PS_AF|PS_ZF
	testShiftWFlags   shl, 0x01, 32, 0,     0

bt386FlagsTest:
	; BT, BTC, BTR, BTS
	; undefined flags:
	;  OF: same as RCR with CF=0
	testBittestFlags   0x01, 0, 0,     PS_CF
	testBittestFlags   0x01, 0, PS_CF, PS_CF
	testBittestFlags   0x01, 1, 0,     PS_OF
	testBittestFlags   0x01, 1, PS_CF, PS_OF
	testBittestFlags   0x01, 2, 0,     PS_OF
	testBittestFlags   0x01, 2, PS_CF, PS_OF
	testBittestFlags   0x01, 3, 0,     0
	testBittestFlags   0x01, 3, PS_CF, 0

rotate386FlagsTest:
	; RCR
	; CF and OF are set with byte and count=9 or word and count=17
	testShiftBFlags   rcr, 0,    9, 0,           0
	testShiftBFlags   rcr, 0,    9, PS_CF|PS_OF, PS_CF
	testShiftBFlags   rcr, 0x40, 9, 0,           PS_OF
	testShiftBFlags   rcr, 0x40, 9, PS_CF|PS_OF, PS_CF|PS_OF
	testShiftWFlags   rcr, 0,      17, 0,           0
	testShiftWFlags   rcr, 0,      17, PS_CF|PS_OF, PS_CF
	testShiftWFlags   rcr, 0x4000, 17, 0,           PS_OF
	testShiftWFlags   rcr, 0x4000, 17, PS_CF|PS_OF, PS_CF|PS_OF
	; RCL
	; CF and OF are set with byte and count=9 or word and count=17
	testShiftBFlags   rcl, 0,    9, 0,           0
	testShiftBFlags   rcl, 0,    9, PS_CF|PS_OF, PS_CF|PS_OF
	testShiftBFlags   rcl, 0x80, 9, 0,           PS_OF
	testShiftBFlags   rcl, 0x80, 9, PS_CF|PS_OF, PS_CF
	testShiftWFlags   rcl, 0,      17, 0,           0
	testShiftWFlags   rcl, 0,      17, PS_CF|PS_OF, PS_CF|PS_OF
	testShiftWFlags   rcl, 0x8000, 17, 0,           PS_OF
	testShiftWFlags   rcl, 0x8000, 17, PS_CF|PS_OF, PS_CF

	jmp arithLogicTests

arithLogicTests:
;-------------------------------------------------------------------------------
	POST EE
;-------------------------------------------------------------------------------
;
;   Now run a series of unverified tests for arithmetical and logical opcodes
;   Manually verify by comparing the tests output with a reference file
;
	jmp bcdTests

bcdTests:
	testBCD   daa, 0x12340503, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x12340506, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x12340507, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x12340559, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x12340560, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x1234059f, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x123405a0, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x12340503, 0,             PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x12340506, 0,             PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x12340503, PS_CF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x12340506, PS_CF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x12340503, PS_CF | PS_AF, PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   daa, 0x12340506, PS_CF | PS_AF, PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x12340503, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x12340506, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x12340507, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x12340559, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x12340560, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x1234059f, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x123405a0, PS_AF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x12340503, 0,             PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x12340506, 0,             PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x12340503, PS_CF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x12340506, PS_CF,         PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x12340503, PS_CF | PS_AF, PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   das, 0x12340506, PS_CF | PS_AF, PS_CF | PS_PF | PS_ZF | PS_SF | PS_AF
	testBCD   aaa, 0x12340205, PS_AF,         PS_CF | PS_AF
	testBCD   aaa, 0x12340306, PS_AF,         PS_CF | PS_AF
	testBCD   aaa, 0x1234040a, PS_AF,         PS_CF | PS_AF
	testBCD   aaa, 0x123405fa, PS_AF,         PS_CF | PS_AF
	testBCD   aaa, 0x12340205, 0,             PS_CF | PS_AF
	testBCD   aaa, 0x12340306, 0,             PS_CF | PS_AF
	testBCD   aaa, 0x1234040a, 0,             PS_CF | PS_AF
	testBCD   aaa, 0x123405fa, 0,             PS_CF | PS_AF
	testBCD   aas, 0x12340205, PS_AF,         PS_CF | PS_AF
	testBCD   aas, 0x12340306, PS_AF,         PS_CF | PS_AF
	testBCD   aas, 0x1234040a, PS_AF,         PS_CF | PS_AF
	testBCD   aas, 0x123405fa, PS_AF,         PS_CF | PS_AF
	testBCD   aas, 0x12340205, 0,             PS_CF | PS_AF
	testBCD   aas, 0x12340306, 0,             PS_CF | PS_AF
	testBCD   aas, 0x1234040a, 0,             PS_CF | PS_AF
	testBCD   aas, 0x123405fa, 0,             PS_CF | PS_AF
	testBCD   aam, 0x12340547, PS_AF,         PS_PF | PS_ZF | PS_SF
	testBCD   aad, 0x12340407, PS_AF,         PS_PF | PS_ZF | PS_SF


	setProtModeIntGate EX_DE, DivExcHandler
	cld
	; The tests for arithmetical and logical opcodes are defined as a sequence
	; of procedures labeled as "tableOps" (see tests/arith-logic_d.asm)
	mov    esi, tableOps   ; ESI -> tableOps entry

testOps:
	movzx  ecx, byte [cs:esi]           ; ECX == length of instruction sequence
	test   ecx, ecx                     ; (must use JZ since there's no long version of JECXZ)
	jz     near testDone                ; zero means we've reached the end of the table
	movzx  ebx, byte [cs:esi+1]         ; EBX == TYPE
	shl    ebx, 6                       ; EBX == TYPE * 64
	movzx  edx, byte [cs:esi+2]         ; EDX == SIZE
	shl    edx, 4                       ; EDX == SIZE * 16
	lea    ebx, [cs:typeValues+ebx+edx] ; EBX -> values for type
	add    esi, 3                       ; ESI -> instruction mnemonic
.skip:
	cs lodsb
	test   al,al
	jnz    .skip
	push   ecx
	mov    ecx, [cs:ebx]    ; ECX == count of values for dst
	mov    eax, [cs:ebx+4]  ; EAX -> values for dst
	mov    ebp, [cs:ebx+8]  ; EBP == count of values for src
	mov    edi, [cs:ebx+12] ; EDI -> values for src
	xchg   ebx, eax         ; EBX -> values for dst
	sub    eax, eax         ; set all ARITH flags to known values prior to tests
testDst:
	push   ebp
	push   edi
	pushfd
testSrc:
	mov   eax, [cs:ebx]    ; EAX == dst
	mov   edx, [cs:edi]    ; EDX == src
	popfd
	call  printOp
	call  printEAX
	call  printEDX
	call  printPS
	call  esi       ; execute the instruction sequence
	call  printEAX
	call  printEDX
	call  printPS
	call  printEOL
	pushfd
	add   edi,4    ; EDI -> next src
	dec   ebp      ; decrement src count
	jnz   testSrc
	popfd
	pop   edi         ; ESI -> restored values for src
	pop   ebp         ; EBP == restored count of values for src
	lea   ebx,[ebx+4] ; EBX -> next dst (without modifying flags)
	loop  testDst

	pop  ecx
	add  esi, ecx     ; ESI -> next tableOps entry
	jmp  testOps

testDone:
	jmp postFF

DivExcHandler:
	push esi
	mov  esi,strDE
	call printStr
	pop  esi
;
;   It's rather annoying that the 80386 treats #DE as a fault rather than a trap, leaving CS:EIP pointing to the
;   faulting instruction instead of the RET we conveniently placed after it.  So, instead of trying to calculate where
;   that RET is, we simply set EIP on the stack to point to our own RET.
;
	mov  dword [esp], DivExcHandlerRet
	iretd
DivExcHandlerRet:
	ret

%include "tests/arith-logic_d.asm"

%endif

postFF:
;-------------------------------------------------------------------------------
	POST FF
;-------------------------------------------------------------------------------
;
;   Testing finished. STOP.
;
	cli
	hlt
	jmp postFF

;
; Default exception handler and error routine
;
DefaultExcHandler:
error:
	; CLI and HLT are privileged instructions, don't use them in ring3
	mov ax, cs
	; when in real mode, the jnz will be decoded together with test as
	; "test eax,0xfe750007" (66A9070075FE)
	test ax, 7     ; 66 A9 07 00
.ring3: jnz .ring3 ; 75 FE
	cli
	hlt
	jmp error


LPTports:
	dw   0x3BC
	dw   0x378
	dw   0x278
COMTHRports:
	dw   0x3F8
	dw   0x2F8
COMLSRports:
	dw   0x3FD
	dw   0x2FD
signedWord:
	db   0x80
signedByte:
	db   0x80

;
;   Fill the remaining space with NOPs until we get to target offset 0xFFF0.
;
	times 0xfff0-($-$$) nop


bits 16

resetVector:
	jmp   C_SEG_REAL:cpuTest ; 0000FFF0

release:
	db    RELEASE,0       ; 0000FFF5  release date
	db    0xFC            ; 0000FFFE  FC (Model ID byte)
	db    0x00            ; 0000FFFF  00 (checksum byte, unused)
