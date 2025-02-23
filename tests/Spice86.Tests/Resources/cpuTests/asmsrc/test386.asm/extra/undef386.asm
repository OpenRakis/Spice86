;
;   undef386.asm
;   Copyright (C) 2019-2023 Marco Bortolin <barotto@gmail.com>
;
;   undef386.asm is free software: you can redistribute it and/or modify it under
;   the terms of the GNU General Public License as published by the Free
;   Software Foundation, either version 3 of the License, or (at your option)
;   any later version.
;
;   undef386.asm is distributed in the hope that it will be useful, but WITHOUT ANY
;   WARRANTY without even the implied warranty of MERCHANTABILITY or FITNESS
;   FOR A PARTICULAR PURPOSE.  See the GNU General Public License for more
;   details.
;
;   See <http://www.gnu.org/licenses/gpl.html>.
;
;
;   This program is a testbed for undefined behaviours or poor documentation.
;
;   WARNING: cannot run with HIMEM or any other memory manager loaded!
;
;   Assemble with:
;   nasm undef386.asm -fbin -o undef386.com -l undef386.lst
;
;
;   The following tests are currently defined:
;
;   Paging: tests if the CPU follows the 386/486 memory protection behaviour as
;           described in the programmer's manuals for those CPUs (wrong) or the
;           behaviour of Pentium and later processors (correct).
;           Possible results: 386/486 docs: wrong implementation
;                             586+ docs: correct implementation
;   32bit SMSW: executes 'SMSW eax' in both real (1st result) and protected (2nd result)
;               modes and checks the value of the upper 16 bits of the destination register.
;               Possible results: undefined: upper 16 bits are nonsense
;                                 CR0: upper 16 bits are equal to those of CR0
;   32bit SLDT: executes 'SLDT eax' and checks the value of the upper 16 bits.
;               Possible results: zero: the upper 16 bits are zeroed
;                                 untouched: the bits are not modified
;                                 undefined: the bits are modified with garbage
;   32bit STR: executes 'STR eax' and checks the value of the upper 16 bits.
;              Possible results: same as "32bit SLDT"
;   32bit MOV SR: executes 'MOV eax,ds' and checks the value of the upper 16 bits.
;                 Possible results: same as "32bit SLDT"
;   32bit PUSH SR: executes 'PUSH ds' and checks the value of the upper 16 bits.
;                 Possible results: same as "32bit SLDT"
;   undef. RAM: reads a byte from a memory location that doesn't exist.
;               Possible results: a byte in hexadecimal format.

;   These are the results for 80386SX and 80486DX CPUs:
;    Paging: 586+ docs
;    32bit SMSW: CR0 CR0
;    32bit SLDT: zero
;    32bit STR: zero
;    32bit MOV SR: zero
;    32bit PUSH SR: untouched
;    undef. RAM: 0xFF
;

cpu 386
org 100h

section .data

addrIDTreal:
	dw 0x3FF ; 16-bit limit of real-mode IDT
	dd 0     ; 32-bit base address of real-mode IDT
addrIDT:
	dw 0x1F*8  ; 16-bit limit
	dd 0       ; 32-bit base address
addrGDT:
	dw 0xFF
	dd 0
ptrReal: ; pointer to real mode code
	dw toReal  ; 16-bit offset
	dw 0       ; 16-bit segment
addrPageDir:
	dd 0 ; 32-bit phy address
offPageDir:
	dd 0 ; 32-bit segment offset
resultPaging:
	dw strError
resultSMSWProt:
	dw strUndefined
resultSMSWReal:
	dw strUndefined
resultSLDT:
	dw strError
resultSTR:
	dw strError
resultMOVSR:
	dw strError
resultPUSHSR:
	dw strError
resultUndefRAM:
	db 0x00
flags:
	dw 0

strError:     db "error$"
strPaging:    db "Paging: $"
strSMSW:      db "32bit SMSW: $"
strSLDT:      db "32bit SLDT: $"
strSTR:       db "32bit STR: $"
strMOVSR:     db "32bit MOV SR: $"
strPUSHSR:    db "32bit PUSH SR: $"
strUndefRAM:  db "undef. RAM: 0x$"
strPaging386: db "386/486 docs$"
strPaging586: db "586+ docs$"
strCR0:       db "CR0 $"
strUndefined: db "undefined $"
strUntouched: db "untouched $"
strZero:      db "zero $"
strNewline:   db $0A,$0D,"$"


align 8
IDT: resb 0x1F * 8
GDT: resb 0x100
TSS: resb 0x100

section .bss
PD:

section .text

	%include "../src/x86_e.asm"
	%include "../src/macros_m.asm"

bits 16

	;db 0xF1

	; save caller's flags and clear
	pushf
	pop  ax
	mov  [flags], ax
	push 0
	popf

; Test 32-bit SMSW in real mode
SMSWTestReal:
	mov  ecx, cr0
	mov  eax, 0xffffffff
	smsw eax
	cmp  ecx, eax
	jne  initGDT
	mov  word [resultSMSWReal], strCR0


%assign GDTSelDesc 0
%macro defGDTDesc 1-3 0,0
	%assign %1 GDTSelDesc
	mov  eax, %1
	mov  esi, cs
	shl  esi, 4      ; base equal to real mode cs
	mov  edi, 0xffff ; real mode compatible limit
	mov  dx,  %2|%3
	mov  ebx, GDT
	initDescriptor
	%assign GDTSelDesc GDTSelDesc+8
%endmacro
%macro getCS 0
	mov   ax, cs
	movzx eax, ax
	shl   eax, 4
%endmacro

initGDT:
	defGDTDesc NULL
	defGDTDesc CS_SEG16,      ACC_TYPE_CODE_R|ACC_PRESENT
	defGDTDesc CS_SEG32,      ACC_TYPE_CODE_R|ACC_PRESENT,EXT_32BIT
	defGDTDesc CS_SEG32_DPL3, ACC_TYPE_CODE_R|ACC_PRESENT|ACC_DPL_3,EXT_32BIT
	defGDTDesc DS_SEG16,      ACC_TYPE_DATA_W|ACC_PRESENT
	defGDTDesc DS_SEG32,      ACC_TYPE_DATA_W|ACC_PRESENT,EXT_32BIT
	defGDTDesc DS_SEG32_DPL3, ACC_TYPE_DATA_W|ACC_PRESENT|ACC_DPL_3,EXT_32BIT
	defGDTDesc TSS_SEG,       ACC_TYPE_TSS|ACC_PRESENT|ACC_DPL_3
	defGDTDesc RING0_GATE ; placeholder for a call gate used to switch to ring 0

	%assign FLAT_SEG GDTSelDesc
	mov  eax, FLAT_SEG
	mov  esi, 0
	mov  edi, 0xfffff
	mov  dx,  ACC_TYPE_DATA_R|ACC_PRESENT|EXT_PAGE ; DOSBox has a bug where EXT_PAGE is not necessary
	mov  ebx, GDT
	initDescriptor

	; EAX = cs.base
	xor   eax, eax
	mov   ax, cs
	mov   [cs:ptrReal + 2], ax ; save cs to return to real mode
	shl   eax, 4

	; update GDT's base address as cs.base+GDT
	mov   edx, GDT
	add   edx, eax
	mov   [cs:addrGDT + 2], edx

	; update TSS_SEG's base value as cs.base+TSS
	mov   ebx, GDT
	add   ebx, TSS_SEG
	add   eax, TSS
	mov   word [ebx + 2], ax ; BASE 15-0
	shr   eax, 16
	mov   byte [ebx + 4], al ; BASE 23-16
	mov   byte [ebx + 7], ah ; BASE 31-24

	jmp initIDT

initIntGateReal:
	pushad
	initIntGate
	popad
	ret

initIDT:
	mov   ebx, IDT
	mov   esi, CS_SEG32
	mov   edi, DefaultExcHandler
	mov   dx,  ACC_DPL_0
%assign vector 0
%rep 0x1F
	mov   eax, vector
	call  initIntGateReal
%assign vector vector+1
%endrep
	getCS
	add   eax, IDT
	mov   [cs:addrIDT + 2], eax


initPaging:
;
;   Build a page directory at ES:EDI with only 1 valid PDE (the first one)
;
	; find the first available 4k page aligned data area in the cs segment
	getCS
	mov   ebx, eax
	add   ebx, PD
	add   ebx, 0xfff
	and   ebx, ~0xfff  ; page dir phy address
	mov   [cs:addrPageDir], ebx

	mov   edi, ebx
	sub   edi, eax ; page dir offset
	mov   [cs:offPageDir], edi

	cld
	mov   eax, ebx
	add   eax, 0x1000 ; page table phy address
	or    eax, PTE_PRESENT | PTE_USER | PTE_WRITE
	stosd
	mov   ecx, 1024-1
	xor   eax, eax    ; fill remaining PDEs with 0
	rep   stosd
;
;   Build a page table at ES:EDI with 256 (out of 1024) valid PTEs, mapping the first 1MB
;   as linear == physical (identity mapping)
;
	mov   edi, [cs:offPageDir]
	add   edi, 0x1000 ; page table offset
	mov   eax, PTE_PRESENT | PTE_WRITE | PTE_USER
	mov   ecx, 256
.initPT:
	stosd
	add   eax, 0x1000
	loop .initPT
	mov   ecx, 1024-256 ; remaining PTEs to write with 0
	xor   eax, eax
	rep   stosd

switchToProtMode:
	cli ; turn off interrupts
	o32 lidt [cs:addrIDT]
	o32 lgdt [cs:addrGDT]
	mov   eax, cr0
	or    eax, CR0_MSW_PE
	mov   cr0, eax
	jmp   CS_SEG32:toProt32

bits 32
%include "../src/protected_p.asm"

toProt32:
	mov   ax, DS_SEG32
	mov   ds, ax
	mov   es, ax
	mov   ss, ax
	mov   esp, 0xFFFE


; Test 32-bit SMSW in protected mode
SMSWProtTest:
	mov  ecx, cr0
	mov  eax, 0x7fffffff
	smsw eax
	cmp  ecx, eax
	jne  pagingTest
	mov  word [resultSMSWProt], strCR0

	mov   ax, TSS_SEG
	ltr   ax



%macro testUpper16Bits 2+
	mov eax, 0xdeadbeef
	%2
	testUpper16BitsResult %1
%endmacro

%macro testUpper16BitsStack 1
	push dword 0xdeadbeef
	add esp, 4
	o32 push ds
	pop eax
	testUpper16BitsResult %1
%endmacro

%macro testUpper16BitsResult 1
; We need to distinguish zeroing, undefined value, and untouched bits
	shr eax, 16
	cmp ax, 0xdead
	je %%untouched
	cmp ax, 0
	je %%zeroed
	mov word [%1], strUndefined
	jmp %%exit
%%untouched:
	mov word [%1], strUntouched
	jmp %%exit
%%zeroed:
	mov word [%1], strZero
	jmp %%exit
%%exit:
%endmacro

; Test 32-bit SLDT
; * Current Intel documentation:
;  When the destination operand is a 32-bit register, the 16-bit segment selector
; is copied into the low-order 16 bits of the register. The high-order 16 bits
; of the register are cleared for the Pentium 4, Intel Xeon, and P6 family processors.
; They are undefined for Pentium, Intel486, and Intel386 processors.
; * 80386 and 80486 programmer's manuals:
;  The operand-size attribute has no effect on the operation of the instruction.
SLDTTest:
	testUpper16Bits resultSLDT, sldt eax


; Test 32-bit STR
; * Current Intel documentation:
;  When the destination operand is a 32-bit register, the 16-bit segment selector
; is copied into the lower 16 bits of the register and the upper 16 bits of the
; register are cleared.
; * 80386 and 80486 programmer's manuals:
;  The operand-size attribute has no effect on this instruction.
STRTest:
	testUpper16Bits resultSTR, str eax


; Test 32-bit MOV SR
; Upper 16 bits are zeroed for Pentium Pro and later, undefined for Pentium and earlier.
MOVSRTest:
	testUpper16Bits resultMOVSR, mov eax,ds


; Test 32-bit PUSH SR
; Either a zero-extended value is pushed on the stack or the segment selector is written
; on the stack using a 16-bit move.
PUSHSRTest:
	testUpper16BitsStack resultPUSHSR


; Read a byte from not present RAM to see what's there
undefRAMTest:
	mov   ax, FLAT_SEG
	mov   gs, ax
	mov   al, [gs:0xffeffff0]
	mov   [resultUndefRAM], al

;
; Test memory protection behaviour with Paging, see if programmer's
; reference manuals for the 80386 and 80486 processors are wrong.
; Keep this test as the last one.
;
pagingTest:
	; enable paging
	mov   eax, [cs:addrPageDir]
	mov   cr3, eax
	mov   eax, cr0
	or    eax, CR0_PG
	mov   cr0, eax
switchToRing3:
	mov   ebx, TSS
	; set ring 0 SS:ESP
	mov   [ebx+4], esp
	mov   eax, ss
	mov   [ebx+8], eax
	push  dword DS_SEG32_DPL3|3    ; push user stack with RPL=3
	push  dword 0xfffe             ; push user mode esp
	pushfd                         ; push eflags
	push  dword CS_SEG32_DPL3|3    ; push user code segment with RPL=3
	push  dword ring3              ; push return EIP
	iretd
ring3:
	mov   ax, DS_SEG32_DPL3
	mov   ds, ax
	mov   es, ax
	mov   gs, ax
	mov   fs, ax


	; mark test PTE as superuser only
	mov   ebx, [offPageDir]
	add   ebx, 0x1000 ; page table base offset

	mov   eax, [addrPageDir]
	add   eax, 0x2000
	shr   eax, 12
	and   eax, 0x3ff ; PTE index

	and   [ebx + eax*4], dword ~PTE_USER

	mov   word [resultPaging], strPaging586
	add   ebx, 0x1000
	mov   eax, [ebx] ; access test page

switchToRing0:
	mov   ebx, GDT
	mov   eax, RING0_GATE
	mov   esi, CS_SEG32
	mov   edi, ring0
	mov   dx,  ACC_DPL_3 ; the DPL needs to be 3
	call  initCallGate
	call  RING0_GATE|3:0 ; the RPL needs to be 3, the offset will be ignored.
ring0:
	mov   ax, DS_SEG32
	mov   ds, ax
	mov   word [resultPaging], strPaging386


	jmp   returnToRealMode


;
; Default exception handler and exit procedure
;
DefaultExcHandler:
returnToRealMode:
	; 1. Disable interrupts.
	cli

	; 2. Clear the PG bit in the CR0 register. Move 0H into the CR3 register to flush the TLB.
	mov   eax, cr0
	and   eax, 0xFFFFFFFF & (~CR0_PG)
	mov   cr0, eax
	xor   eax, eax
	mov   cr3, eax

	; 3. Transfer program control to a readable segment that has a limit of 64 KBytes (FFFFH).
	jmp   CS_SEG16:.to16bits
.to16bits:
bits 16

	; 4. Load segment registers SS, DS, ES, FS, and GS with a selector for a
	; descriptor containing the following values, which are appropriate for real-address mode:
	; Limit = 64 KBytes (0FFFFH), Byte granular (G = 0), Expand up (E = 0), Writable (W = 1),
	; Present (P = 1), Base = any value
	mov   ax, DS_SEG16
	mov   ss, ax
	mov   ds, ax
	mov   es, ax
	mov   fs, ax
	mov   gs, ax

	; 5. Execute an LIDT instruction to point to a real-address mode interrupt
	; table that is within the 1-MByte real-address mode address range.
	o32 lidt [addrIDTreal] ; DS_SEG16 has base = cs

	; 6. Clear the PE flag in the CR0 register to switch to real-address mode.
	mov   eax, cr0
	and   eax, ~CR0_MSW_PE
	mov   cr0, eax

	; 7. Execute a far JMP instruction to jump to a real-address mode program.
	jmp far [ptrReal]
toReal:

	; 8. Load the SS, DS, ES, FS, and GS registers as needed by the real-address mode code.
	mov   ax, cs
	mov   ss, ax
	mov   ds, ax
	mov   es, ax
	mov   fs, ax
	mov   gs, ax
	mov   sp, 0xfffe

	; restore caller's flags
	mov   ax, [flags]
	push  ax
	popf

	; 9. Execute the STI instruction
	sti

	jmp printResults


%macro printString 1
	mov   dx, %1
	mov   ah, 0x9
	int   0x21
%endmacro
%macro printChar 0
	mov   ah, 0x2
	int   0x21
%endmacro

;
;   printHex(EDX == value, CL == number of hex digits)
;
printHex:
	shl    cl, 2  ; CL == number of bits (4 times the number of hex digits)
	jz     .done
.loop:
	sub    cl, 4
	push   edx
	shr    edx, cl
	and    dl, 0x0f
	add    dl, '0'
	cmp    dl, '9'
	jbe    .digit
	add    dl, 'A'-'0'-10
.digit:
	printChar
	pop    edx
	test   cl, cl
	jnz    .loop
.done:
	ret

printResults:
	printString strPaging
	printString [resultPaging]
	printString strNewline

	printString strSMSW
	printString [resultSMSWReal]
	printString [resultSMSWProt]
	printString strNewline

	printString strSLDT
	printString [resultSLDT]
	printString strNewline

	printString strSTR
	printString [resultSTR]
	printString strNewline

	printString strMOVSR
	printString [resultMOVSR]
	printString strNewline

	printString strPUSHSR
	printString [resultPUSHSR]
	printString strNewline

	printString strUndefRAM
	xor edx, edx
	mov dl, byte [resultUndefRAM]
	mov cl, 2
	call printHex
	printString strNewline

exit:
	mov   ax, 0x4c00
	int   0x21


