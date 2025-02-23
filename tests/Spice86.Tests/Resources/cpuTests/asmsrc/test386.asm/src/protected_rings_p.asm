;
; Tests the Current Privilege Level value
;
; %1 the value (0-3) to compare to; jumps to error if not equal.
;
%macro testCPL 1
	mov  ax, cs
	and  ax, 3
	cmp  ax, %1
	jne  error
%endmacro


; Switches from Ring 0 to Ring 3
;
; After calling this procedure consider all the registers and flags as trashed.
; Also, the stack will be different, so saving the CPU state there will be pointless.
;
switchToRing3:
	; In order to swich to user mode (ring 3) we need to execute an IRET with these
	; values on the stack:
	; - the instruction to continue execution at - the value of EIP.
	; - the code segment selector to change to.
	; - the value of the EFLAGS register to load.
	; - the stack pointer to load.
	; - the stack segment selector to change to.
	; We also need:
	; - a 32bit code descriptor in GDT with DPL 3
	; - a 32bit data descriptor in GDT with DPL 3 (for the new stack)
	; - to put the ring 0 stack in TSS.SS0 and TSS.ESP0
	testCPL 0 ; we must be in ring 0
	pop    edx ; read the return offset
	mov    ax, ds
	lds    ebx, [cs:ptrTSSprot]
	; save ring 0 data segments, they'll be restored with switchToRing0
	mov    [ebx+0x54], ax ; save DS
	mov    ax, es
	mov    [ebx+0x48], ax ; save ES
	mov    ax, fs
	mov    [ebx+0x58], ax ; save FS
	mov    ax, gs
	mov    [ebx+0x5C], ax ; save GS
	; set ring 0 SS:ESP
	mov    [ebx+4], esp
	mov    eax, ss
	mov    [ebx+8], eax
	cli                           ; disable ints during switching
	push dword SU_SEG_PROT32|3    ; push user stack with RPL=3
	push dword ESP_R3_PROT        ; push user mode esp
	pushfd                        ; push eflags
	or   dword [ss:esp+4], 0x200  ; reenable interrupts in ring 3 (can't use privileged sti)
	push dword CU_SEG_PROT32|3    ; push user code segment with RPL=3
	push dword edx                ; push return EIP
	iretd


; Switches from Ring 3 to Ring 0
;
; After calling this procedure consider all the registers and flags as trashed.
;
switchToRing0:
	testCPL 3 ; we must be in ring 3
	; In order to swich to kernel mode (ring 0) we'll use a Call Gate.
	; A placeholder for a Call Gate is already present in the GDT.
	pop  ecx ; read the return offset
	lfs  ebx, [cs:ptrGDTUprot]
	mov  eax, RING0_GATE
	mov  esi, C_SEG_PROT32
	mov  edi, .ring0
	mov  dx,  ACC_DPL_3 ; the DPL needs to be 3
	call initCallGate
	call RING0_GATE|3:0 ; the RPL needs to be 3, the offset will be ignored.
.ring0:
	add  esp, 16 ; remove from stack CS:EIP+SS:ESP pushed by the CALL to RING0_GATE
	; restore ring 0 data segments saved by switchToRing3
	lds  ebx, [cs:ptrTSSprot]
	mov  ax, [ebx+0x48] ; restore ES
	mov  es, ax
	mov  ax, [ebx+0x58] ; restore FS
	mov  fs, ax
	mov  ax, [ebx+0x5C] ; restore GS
	mov  gs, ax
	mov  ax, [ebx+0x54] ; restore DS
	mov  ds, ax
	; return to caller
	push ecx
	ret
