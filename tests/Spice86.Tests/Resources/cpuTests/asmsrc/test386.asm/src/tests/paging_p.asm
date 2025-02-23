; Page Directory/Table Entry (PDE, PTE)
;
; 31                                   12 11          6 5 4 3 2 1 0
; ╔══════════════════════════════════════╤═══════╤═══╤═╤═╤═╤═╤═╤═╤═╗
; ║                                      │       │   │ │ │P│P│U│R│ ║
; ║      PAGE FRAME ADDRESS 31..12       │ AVAIL │0 0│D│A│C│W│/│/│P║
; ║                                      │       │   │ │ │D│T│S│W│ ║
; ╚══════════════════════════════════════╧═══════╧═══╧═╧═╧═╧═╧═╧═╧═╝
; P: PRESENT, R/W: READ/WRITE, U/S: USER/SUPERVISOR,
; PWT: PAGE WRITE-THROUGH (486+), PCD: PAGE CACHE DISABLE (486+)
; A: ACCESSED, D: DIRTY (PTE only),
; AVAIL: AVAILABLE FOR SYSTEMS PROGRAMMER USE, 0: reserved
;
; Page Translation
;                                                               PAGE FRAME
;               ╔═══════════╦═══════════╦══════════╗         ╔═══════════════╗
;               ║    DIR    ║   PAGE    ║  OFFSET  ║         ║               ║
;               ╚═════╤═════╩═════╤═════╩═════╤════╝         ║               ║
;                     │           │           │              ║               ║
;       ┌──────/──────┘           /10         └──────/──────▶║    PHYSICAL   ║
;       │     10                  │                 12       ║    ADDRESS    ║
;       *4                        *4                         ║               ║
;       │   PAGE DIRECTORY        │      PAGE TABLE          ║               ║
;       │  ╔═══════════════╗      │   ╔═══════════════╗      ║               ║
;       │  ║               ║      │   ║               ║      ╚═══════════════╝
;       │  ║               ║      │   ╠═══════════════╣              ▲
;       │  ║               ║      └──▶║      PTE      ╟──────────────┘
;       │  ╠═══════════════╣          ╠═══════════════╣   [31:12]
;       └─▶║      PDE      ╟──┐       ║               ║
;          ╠═══════════════╣  │       ║               ║
;          ║               ║  │       ║               ║
;          ╚═══════════════╝  │       ╚═══════════════╝
;                  ▲          │               ▲
; ╔═══════╗ PDBR   │          └───────────────┘
; ║  CR3  ╟────────┘               [31:12]
; ╚═══════╝ [31:12]
;

;
; Input: EAX = entry index with bits 12-13 as the table index (table 0,1,2)
; Returns: FS:EBX pointer to table entry
;
loadTableEntryAddress:
	push edx
	mov  edx, eax
	lfs  ebx, [cs:ptrPDprot]
	and  edx, 0x3000
	add  ebx, edx
	and  eax, 0x3FF
	shl  eax, 2
	add  ebx, eax
	pop  edx
	ret

;
; Updates the flags of a PDE/PTE
;
; EAX = entry index
; EDX = new flags (bits 11-0)
;
; caller-saved
; uses FS
;
updPageFlagsP:
	call loadTableEntryAddress
	and  [fs:ebx], dword PTE_FRAME
	or   [fs:ebx], edx
	mov  eax, PAGE_DIR_ADDR
	mov  cr3, eax ; flush the page translation cache
	ret

;
; Given a bitmask, set the value of specific PTE flags
;
; EAX = entry index
; ECX = flags mask
; EDX = new flags value
;
; caller-saved
; uses FS
;
setPageFlagsP:
	call loadTableEntryAddress
	not  ecx
	and  [fs:ebx], ecx
	or   [fs:ebx], edx
	mov  eax, PAGE_DIR_ADDR
	mov  cr3, eax ; flush the page translation cache
	ret

;
; Returns a PTE in EAX
; EAX = linear address
; Uses FS
;
getPTE:
	push ebx
	push eax
	call getPDE
	and  eax, 0xFFFFF000
	mov  ebx, eax
	mov  ax, FLAT_SEG_PROT
	mov  fs, ax
	pop  eax
	shr  eax, 12
	and  eax, 0x3FF
	mov  eax, [fs:ebx + eax*4]
	pop  ebx
	ret

;
; Returns a PDE in EAX
; EAX = linear address
; Uses FS
;
getPDE:
	push ebx
	lfs  ebx, [cs:ptrPDprot]
	shr  eax, 22
	and  eax, 0x3FF
	mov  eax, [fs:ebx + eax*4]
	pop  ebx
	ret


;
; Combined Page Directory and Page Table Protection:
;
; +-----------------+-----------------+----------------+
; |  Page Directory |    Page Table   |    Combined    |
; | Privilege  Type | Privilege  Type | Privilege  Type|
; |-----------------+-----------------+----------------|
; | User       R    | User       R    | User       R   |
; | User       R    | User       RW   | User       R   |
; | User       RW   | User       R    | User       R   |
; | User       RW   | User       RW   | User       RW  |
; | User       R    | Supervisor R    | Supervisor RW  |*
; | User       R    | Supervisor RW   | Supervisor RW  |*
; | User       RW   | Supervisor R    | Supervisor RW  |*
; | User       RW   | Supervisor RW   | Supervisor RW  |*
; | Supervisor R    | User       R    | Supervisor RW  |*
; | Supervisor R    | User       RW   | Supervisor RW  |*
; | Supervisor RW   | User       R    | Supervisor RW  |*
; | Supervisor RW   | User       RW   | Supervisor RW  |*
; | Supervisor R    | Supervisor R    | Supervisor RW  |
; | Supervisor R    | Supervisor RW   | Supervisor RW  |
; | Supervisor RW   | Supervisor R    | Supervisor RW  |
; | Supervisor RW   | Supervisor RW   | Supervisor RW  |
; +-----------------+-----------------+----------------+
;
; * Programmer's reference manuals for 386DX and i486 have different results for
;   these cases.
;   In particular, the following manuals are wrong:
;   - 386DX Microprocessor Programmer's Reference Manual 1990
;   - i486 Processor Programmer's Reference Manual 1990
;
;
; #PF error code pushed on the stack (386, 486, Pentium):
;
; 31                3     2     1     0
; +-----+-...-+-----+-----+-----+-----+
; |     Reserved    | U/S | W/R |  P  |
; +-----+-...-+-----+-----+-----+-----+
;
; P: When set, the fault was caused by a protection violation.
;    When not set, it was caused by a non-present page.
; W/R: When set, write access caused the fault; otherwise read access.
; U/S: When set, the fault occurred in user mode; otherwise in supervisor mode.
;
; The CR2 register contains the 32-bit linear address that caused the fault.
;

PF_PROT   equ 001b ; page protection error
PF_WRITE  equ 010b ; write access error
PF_USER   equ 100b ; fault occurred in user mode

; convenience equs
PF_READ       equ 0
PF_NOTP       equ 0
PF_NOFAULT    equ 0x80
PTE_SUPER_R   equ PTE_PRESENT
PTE_SUPER_W   equ PTE_PRESENT|PTE_WRITE
PTE_USER_R    equ PTE_PRESENT|PTE_USER
PTE_USER_W    equ PTE_PRESENT|PTE_USER|PTE_WRITE


pagingTests:
; not present faults
db	PTE_PRESENT|         PTE_WRITE,                       PTE_WRITE,  PF_NOTP|PF_READ
db	PTE_PRESENT|         PTE_WRITE,                       PTE_WRITE,  PF_NOTP|PF_WRITE
db	PTE_PRESENT|         PTE_WRITE,                       PTE_WRITE,  PF_NOTP|PF_READ |PF_USER
db	PTE_PRESENT|         PTE_WRITE,                       PTE_WRITE,  PF_NOTP|PF_WRITE|PF_USER

db	PTE_PRESENT|PTE_USER|PTE_WRITE,              PTE_USER|PTE_WRITE,  PF_NOTP|PF_READ |PF_USER
db	PTE_PRESENT|PTE_USER,                        PTE_USER,            PF_NOTP|PF_WRITE|PF_USER
db	                     PTE_WRITE,  PTE_PRESENT|         PTE_WRITE,  PF_NOTP|PF_READ
db	                     PTE_WRITE,  PTE_PRESENT|         PTE_WRITE,  PF_NOTP|PF_WRITE
db	                     PTE_WRITE,  PTE_PRESENT|PTE_USER|PTE_WRITE,  PF_NOTP|PF_READ |PF_USER
db	                     PTE_WRITE,  PTE_PRESENT|PTE_USER|PTE_WRITE,  PF_NOTP|PF_WRITE|PF_USER

; protection faults
; user-user combinations
db	PTE_USER_R,   PTE_USER_R,   PF_NOFAULT|PF_READ |PF_USER
db	PTE_USER_R,   PTE_USER_R,   PF_PROT   |PF_WRITE|PF_USER
db	PTE_USER_R,   PTE_USER_W,   PF_NOFAULT|PF_READ |PF_USER
db	PTE_USER_R,   PTE_USER_W,   PF_PROT   |PF_WRITE|PF_USER
db	PTE_USER_W,   PTE_USER_R,   PF_NOFAULT|PF_READ |PF_USER
db	PTE_USER_W,   PTE_USER_R,   PF_PROT   |PF_WRITE|PF_USER
db	PTE_USER_W,   PTE_USER_W,   PF_NOFAULT|PF_READ |PF_USER
db	PTE_USER_W,   PTE_USER_W,   PF_NOFAULT|PF_WRITE|PF_USER
db	PTE_USER_R,   PTE_USER_R,   PF_NOFAULT|PF_READ
db	PTE_USER_R,   PTE_USER_R,   PF_NOFAULT|PF_WRITE
db	PTE_USER_R,   PTE_USER_W,   PF_NOFAULT|PF_READ
db	PTE_USER_R,   PTE_USER_W,   PF_NOFAULT|PF_WRITE
db	PTE_USER_W,   PTE_USER_R,   PF_NOFAULT|PF_READ
db	PTE_USER_W,   PTE_USER_R,   PF_NOFAULT|PF_WRITE
db	PTE_USER_W,   PTE_USER_W,   PF_NOFAULT|PF_READ
db	PTE_USER_W,   PTE_USER_W,   PF_NOFAULT|PF_WRITE

; super-super combinations
db	PTE_SUPER_R,  PTE_SUPER_R,  PF_PROT|PF_READ |PF_USER
db	PTE_SUPER_R,  PTE_SUPER_R,  PF_PROT|PF_WRITE|PF_USER
db	PTE_SUPER_R,  PTE_SUPER_W,  PF_PROT|PF_READ |PF_USER
db	PTE_SUPER_R,  PTE_SUPER_W,  PF_PROT|PF_WRITE|PF_USER
db	PTE_SUPER_W,  PTE_SUPER_R,  PF_PROT|PF_READ |PF_USER
db	PTE_SUPER_W,  PTE_SUPER_R,  PF_PROT|PF_WRITE|PF_USER
db	PTE_SUPER_W,  PTE_SUPER_W,  PF_PROT|PF_READ |PF_USER
db	PTE_SUPER_W,  PTE_SUPER_W,  PF_PROT|PF_WRITE|PF_USER
db	PTE_SUPER_R,  PTE_SUPER_R,  PF_NOFAULT|PF_READ
db	PTE_SUPER_R,  PTE_SUPER_R,  PF_NOFAULT|PF_WRITE
db	PTE_SUPER_R,  PTE_SUPER_W,  PF_NOFAULT|PF_READ
db	PTE_SUPER_R,  PTE_SUPER_W,  PF_NOFAULT|PF_WRITE
db	PTE_SUPER_W,  PTE_SUPER_R,  PF_NOFAULT|PF_READ
db	PTE_SUPER_W,  PTE_SUPER_R,  PF_NOFAULT|PF_WRITE
db	PTE_SUPER_W,  PTE_SUPER_W,  PF_NOFAULT|PF_READ
db	PTE_SUPER_W,  PTE_SUPER_W,  PF_NOFAULT|PF_WRITE

; user-super combinations with supervisor access, always no fault
db	PTE_USER_R,   PTE_SUPER_R,  PF_NOFAULT|PF_READ
db	PTE_USER_R,   PTE_SUPER_R,  PF_NOFAULT|PF_WRITE
db	PTE_USER_R,   PTE_SUPER_W,  PF_NOFAULT|PF_READ
db	PTE_USER_R,   PTE_SUPER_W,  PF_NOFAULT|PF_WRITE
db	PTE_USER_W,   PTE_SUPER_R,  PF_NOFAULT|PF_READ
db	PTE_USER_W,   PTE_SUPER_R,  PF_NOFAULT|PF_WRITE
db	PTE_USER_W,   PTE_SUPER_W,  PF_NOFAULT|PF_READ
db	PTE_USER_W,   PTE_SUPER_W,  PF_NOFAULT|PF_WRITE
db	PTE_SUPER_R,  PTE_USER_R,   PF_NOFAULT|PF_READ
db	PTE_SUPER_R,  PTE_USER_R,   PF_NOFAULT|PF_WRITE
db	PTE_SUPER_R,  PTE_USER_W,   PF_NOFAULT|PF_READ
db	PTE_SUPER_R,  PTE_USER_W,   PF_NOFAULT|PF_WRITE
db	PTE_SUPER_W,  PTE_USER_R,   PF_NOFAULT|PF_READ
db	PTE_SUPER_W,  PTE_USER_R,   PF_NOFAULT|PF_WRITE
db	PTE_SUPER_W,  PTE_USER_W,   PF_NOFAULT|PF_READ
db	PTE_SUPER_W,  PTE_USER_W,   PF_NOFAULT|PF_WRITE

; user-super combinations with user access
db	PTE_USER_R,   PTE_SUPER_R,  PF_PROT|PF_READ |PF_USER
db	PTE_USER_R,   PTE_SUPER_R,  PF_PROT|PF_WRITE|PF_USER
db	PTE_USER_R,   PTE_SUPER_W,  PF_PROT|PF_READ |PF_USER
db	PTE_USER_R,   PTE_SUPER_W,  PF_PROT|PF_WRITE|PF_USER
db	PTE_USER_W,   PTE_SUPER_R,  PF_PROT|PF_READ |PF_USER
db	PTE_USER_W,   PTE_SUPER_R,  PF_PROT|PF_WRITE|PF_USER
db	PTE_USER_W,   PTE_SUPER_W,  PF_PROT|PF_READ |PF_USER
db	PTE_USER_W,   PTE_SUPER_W,  PF_PROT|PF_WRITE|PF_USER
db	PTE_SUPER_R,  PTE_USER_R,   PF_PROT|PF_READ |PF_USER
db	PTE_SUPER_R,  PTE_USER_R,   PF_PROT|PF_WRITE|PF_USER
db	PTE_SUPER_R,  PTE_USER_W,   PF_PROT|PF_READ |PF_USER
db	PTE_SUPER_R,  PTE_USER_W,   PF_PROT|PF_WRITE|PF_USER
db	PTE_SUPER_W,  PTE_USER_R,   PF_PROT|PF_READ |PF_USER
db	PTE_SUPER_W,  PTE_USER_R,   PF_PROT|PF_WRITE|PF_USER
db	PTE_SUPER_W,  PTE_USER_W,   PF_PROT|PF_READ |PF_USER
db	PTE_SUPER_W,  PTE_USER_W,   PF_PROT|PF_WRITE|PF_USER
pagingTestsEnd:

pageEntryStr:           ; UWP
	db  "  SUPER R, ",0 ; 000
	db  "P SUPER R, ",0 ; 001
	db  "  SUPER W, ",0 ; 010
	db  "P SUPER W, ",0 ; 011
	db  "  USER  R, ",0 ; 100
	db  "P USER  R, ",0 ; 101
	db  "  USER  W, ",0 ; 110
	db  "P USER  W, ",0 ; 111
noFaultStr:
	db  "no fault ",0
strPF:
	db  "#PF ",0

;
; Tests if the CPU throws a page fault under the specified conditions.
;
; EAX = PDE flags to use
; EBX = PTE flags to use
; EDX = expected error code value
;
;
testPageFault:
	%if DEBUG
	pushad
	mov  cl, 12
	mul  cl
	mov  esi, pageEntryStr
	add  esi, eax
	call printStr
	mov  eax, ebx
	mul  cl
	mov  esi, pageEntryStr
	add  esi, eax
	call printStr
	test edx, PF_NOFAULT
	jnz  .printNoFault
	jmp  .printErrCode
.printNoFault:
	mov  esi, noFaultStr
	call printStr
.printErrCode:
	and  edx, 7
	mov  eax, edx
	mul  cl
	mov  esi, pageEntryStr
	add  esi, eax
	call printStr
	popad
	%endif

	; update PDE
	pushad
	mov  edx, eax ; new flags
	mov  eax, TESTPAGE_PDE
	call updPageFlagsP
	popad
	; update PTE
	pushad
	mov  edx, ebx ; new flags
	mov  eax, TESTPAGE_PTE
	call updPageFlagsP
	popad

	; reset CR2 to test its value in the page faults handler
	xor   eax, eax
	mov   cr2, eax

	; if the fault should happen in user mode switch to ring3
	test  edx, PF_USER
	jz   .start_test
	; before switching to user mode I need to save ESI,ECX,EDX for ring0 and EDX for ring3
	push  esi  ; save esi for ring0
	push  ecx  ; save ecx for ring0
	push  edx  ; save edx for ring0
	xchg  eax, esp
	mov   esp, ESP_R3_PROT
	push  edx  ; save edx for ring3
	xchg  eax, esp
	call  switchToRing3
	sub   esp, 4
	pop   edx  ; restore edx for ring3

.start_test:
	mov   ax, DTEST_SEG_PROT
	mov   ds, ax
	; switch to the appropriate test
	test  edx, PF_NOFAULT
	jnz  .no_fault
	test  edx, PF_WRITE
	jnz  .write_fault
.read_fault:
	mov   eax, [TESTPAGE_LIN]
	cmp   eax, PF_HANDLER_SIG  ; the page fault handler should have put its signature in memory
	jne   error
	jmp  .continue
.write_fault:
	mov   [TESTPAGE_LIN], dword 0xdeadbeef
	cmp   eax, PF_HANDLER_SIG  ; the page fault handler should have put its signature in EAX
	jne   error
	cmp   [TESTPAGE_OFF], dword 0xdeadbeef
	jne   error
	jmp  .continue
.no_fault:
	test  edx, PF_WRITE
	jnz  .write_nofault
.read_nofault:
	mov   eax, [TESTPAGE_LIN]
	jmp  .continue
.write_nofault:
	mov   [TESTPAGE_LIN], dword 0xdeadbeef
	jmp  .continue

.continue:

	; if the fault happened in user mode switch back to ring0
	test  edx, PF_USER
	jz   .verify_bits
	call  switchToRing0
	pop   edx ; restore edx
	pop   ecx ; restore ecx
	pop   esi ; restore esi

.verify_bits:
	; verify Accessed and Dirty bits
	mov   eax, TESTPAGE_LIN
	call  getPTE
	xchg  eax, ebx
	mov   eax, TESTPAGE_LIN
	call  getPDE
	; EAX = PDE, EBX = PTE
	; both PDE's and PTE's Accessed bits should be set
	test  eax, PTE_ACCESSED
	jz    error
	test  ebx, PTE_ACCESSED
	jz    error
	; if write operation then PTE's Dirty bit should be set otherwise 0
	test  edx, PF_WRITE
	jz   .read
	test  ebx, PTE_DIRTY
	jz    error
	jmp  .exit
.read:
	test  ebx, PTE_DIRTY
	jnz   error

.exit:
 	; reset memory location used for testing
	mov [TESTPAGE_OFF], dword 0

	%if DEBUG
	call printEOL
	%endif
	ret

;
; Page Fault handler
;
PF_HANDLER_SIG equ 0x50465046
PageFaultHandler:
	%if DEBUG
	push esi
	mov  esi, strPF
	call printStr
	pop  esi
	%endif

	; if expected error code has PF_NOFAULT bit, then this fault is an error
	test  edx, PF_NOFAULT
	jnz   error
	; compare the expected error code in EDX with the one pushed on the stack
	pop   ebx
	cmp   edx, ebx
	jne   error
	; this handler is expected to run in ring 0
	testCPL 0
	; check CR2 register, it must contain the linear address TESTPAGE_LIN
	mov   eax, cr2
	cmp   eax, TESTPAGE_LIN
	jne   error
	; check the PDE flags
	call  getPDE
	test  eax, PTE_ACCESSED ; PDE's A bit should be 0
	jnz   error
	; check the PTE flags
	mov   eax, TESTPAGE_LIN
	call  getPTE
	test  eax, PTE_ACCESSED|PTE_DIRTY ; PTE's A and D bits should be 0
	jnz   error
	; update PDE and PTE, put handler's result in memory or register
	test  edx, PF_PROT
	jz   .not_present
	test  edx, PF_USER
	jnz  .user
	jmp   error ; protection errors in supervisor mode can't happen
.not_present:
	; mark both PDE and PTE as present
	setPageFlags  TESTPAGE_PDE, PTE_PRESENT_BIT, PTE_PRESENT
	setPageFlags  TESTPAGE_PTE, PTE_PRESENT_BIT, PTE_PRESENT
	test  edx, PF_USER
	jnz  .user
	jmp  .check_rw
.user:
	; mark both PDE and PTE for user access
	setPageFlags  TESTPAGE_PDE, PTE_USER_BIT, PTE_USER
	setPageFlags  TESTPAGE_PTE, PTE_USER_BIT, PTE_USER
.check_rw:
	test  edx, PF_WRITE
	jnz  .write
.read:
	; put the handler's signature in memory (use page table 0)
	mov   [TESTPAGE_OFF], dword PF_HANDLER_SIG
	xor   eax, eax
	jmp  .exit
.write:
	; put the handler's signature in EAX
	setPageFlags  TESTPAGE_PDE, PTE_WRITE_BIT, PTE_WRITE ; mark the PTE for write
	setPageFlags  TESTPAGE_PTE, PTE_WRITE_BIT, PTE_WRITE ; mark the PTE for write
	mov   eax, PF_HANDLER_SIG
.exit:
	iretd
