; Testing of 32-bit addressing using the LEA instruction.
; Every possible combination of ModRM and SIB is tested (6312 valid for LEA).
;
; I'm using a different approach than the arith-logic tests at POST EEh.
; Instead of manually writing every possible instruction to execute and letting
; the CPU write the results to the output port, I'll use a self modifying test
; routine. The computed EA will be immediately compared with the expected result.
;
; Is this really necessary? No.
; Was it fun to code at least? Somewhat.
; Would I do it again? Probably not.
; What is a reasonable alternative? Create a binary table with every possible
; valid combination of the LEA instruction followed by RET; this is a fairly
; easy, 1-time job that is less prone to bugs.
; For every entry in the table:
; 1. initialize every register with known values
; 2. call the LEA+RET code at the current table offset
; 3. print the value of every register to the output port
; At the end, manually compare the output with a reference.
; Don't depend on NASM to assemble the LEA instructions, it tends to use
; optimizations.


;
; This is the routine that will be updated, to be copied in system memory.
; A couple of loops in the driver will iterate through every combination of the
; ModRM and SIB bytes used in a LEA instruction. A CMP instruction will then be
; executed to do a comparison between the computed EA and the expected correct
; value. A final MOV r/m32,r32 will move the computed EA in ES:[0] for later use
; (debug).
;
addr32TestCode:
	pushad ; save current regs values
	mov [es:4], esp ; save ESP

	; fill in the values to be used for effective address computation
	mov eax, 0x001
	mov ebx, 0x002
	mov ecx, 0x004
	mov edx, 0x008
	mov esp, 0x020
	mov ebp, 0x040
	mov esi, 0x080
	mov edi, 0x100

	%define disp8value  0x80
	%define disp32value 0x80000000

	db 0x8D ; LEA, lenght=2-7
	.leaModRM:   db 0x00 ; ModRM byte
	.leaSIBDisp: db 0x90 ; SIB byte or disp8/disp32 or NOP
	.leaDisp:    db 0x90,0x90,0x90 ; disp8 or disp32, if SIB is used, else NOPs
	.leaLastByte:db 0x90 ; the last possible byte, if SIB and disp32 are used, else NOP

	;jmp .skipCMP ; DEBUG

	db 0x81 ; CMP (81/7), lenght=6
	.cmpModRM: db 0 ; ModRM byte, the register to compare is derived from LEA's ModRM
	.cmpImm32: dd 0 ; 32-bit immediate value for comparison

.skipCMP:
	db 0x26 ; ES prefix
	db 0x89 ; MOV, lenght=5
	.movModRM: db 0 ; ModRM byte, the register to move is the same as LEA's ModRM
	dd 0 ; disp32

	mov esp, [es:4] ; restore ESP
	je .exit
	call C_SEG_PROT32:error

.exit
	popad ; restore regs
	retf
.end:

%assign leaModRMOff    addr32TestCode.leaModRM   - addr32TestCode
%assign leaSIBDispOff  addr32TestCode.leaSIBDisp - addr32TestCode
%assign leaDispOff     addr32TestCode.leaDisp    - addr32TestCode
%assign leaLastByteOff addr32TestCode.leaLastByte- addr32TestCode
%assign cmpModRMOff    addr32TestCode.cmpModRM   - addr32TestCode
%assign cmpImm32Off    addr32TestCode.cmpImm32   - addr32TestCode
%assign movModRMOff    addr32TestCode.movModRM   - addr32TestCode


;
; This is the testing driver. Two loops, 1 for ModRM and 1 for the current SIB
; (when present), will generate the missing parts of the LEA and CMP
; instructions of the testing routine.
;
; x86 addressing is a bit convoluted and has some special cases:
; - if Mod=00b and R/M=100b then SIB is present
; - if Mod=00b and R/M=100b and Base=101b then SIB+disp32 are present
; - if Mod=00b and R/M=101b then disp32 is present
; - if Mod=01b then disp8 is present
; - if Mod=01b and R/M=100b then SIB+disp8 are present
; - if Mod=10b then disp32 is present
; - if Mod=10b and R/M=100b then SIB+disp32 are present
;
; 7      6 5         3 2          0
; ╔═══╤═══╤═══╤═══╤═══╤═══╤═══╤═══╗
; ║  Mod  │    Reg    │    R/M    ║ ModRM
; ╚═══╧═══╧═══╧═══╧═══╧═══╧═══╧═══╝
;
; 7      6 5         3 2          0
; ╔═══╤═══╤═══╤═══╤═══╤═══╤═══╤═══╗
; ║ Scale │   Index   │   Base    ║ SIB
; ╚═══╧═══╧═══╧═══╧═══╧═══╧═══╧═══╝
;
testAddressing32:
	; dynamic code segment base = D1_SEG_PROT base
	updLDTDescBase DC_SEG_PROT32,TEST_BASE1

	; copy the test routine in RAM
	mov ecx, addr32TestCode.end - addr32TestCode
	mov ax, C_SEG_PROT32 ; source = C_SEG_PROT32:addr32TestCode
	mov ds, ax
	mov esi, addr32TestCode
	mov ax, D1_SEG_PROT  ; dest = D1_SEG_PROT:0
	mov es, ax
	mov edi, 0
	cld
	rep movsb

	mov ax, D1_SEG_PROT
	mov ds, ax ; DS = writeable code segment
	mov ax, D2_SEG_PROT
	mov es, ax ; ES = scratch pad

	; AL = LEA modrm byte
	; AH = LEA SIB byte
	; EAX16 = LEA SIB present?
	; BL = CMP modrm byte
	; BH = LEA last byte value
	; ECX15-0 = ModRM loop counter
	; ECX31-16 = SIB loop counter
	; DL = index in the CMP values table
	; DH = ModRM of the MOV
	; ESI = LEA 8/32-bit displacement
	; EDI = LEA displacement value offset

	xor eax, eax
	mov ecx, 0x010000C0 ; ModRM values C0-FF are not valid for LEA
.calcModRMValues:
	call addr32CalcLEAValues
	call addr32CalcCMPValues
	call addr32CalcMOVValues
	call addr32CopyValues
	; call addr32PrintStatus ; enable for DEBUG
	call DC_SEG_PROT32:0
	; call addr32PrintResult ; enable for DEBUG

	test eax, 0x10000 ; is SIB used?
	jnz .nextSIB
.nextModRM:
	inc al ; next LEA ModRM value
	jmp .loop
.nextSIB:
	inc ah ; next LEA SIB value
	a16 loop .calcModRMValues
	; if we end here then the SIB loop is over
	and  eax, 0x0ffff  ; disable SIB byte flag
	mov  cx, 0x100     ; reset SIB loop counter
	rol  ecx, 16       ; switch SIB loop cnt with ModRM loop cnt
	jmp .nextModRM
.loop:
	a16 loop .calcModRMValues
	; outer ModRM loop finished
	ret


addr32CalcLEAValues:
	; handle the SIB byte
	mov ebp, eax
	and ebp, 111b ; LEA R/M value
	cmp ebp, 100b ; SIB encoding?
	jne .noSIB
.SIB:
	mov  edi, leaDispOff    ; displacement, if present, is after SIB
	test eax, 0x10000       ; was prev iteration with SIB?
	jnz .disp
	or   eax, 0x10000       ; enable SIB byte flag
	rol  ecx, 16            ; switch ModRM loop with SIB loop
	jmp .disp
.noSIB:
	mov  edi, leaSIBDispOff ; displacement, if present, is after opcode

	; handle disp8/disp32
.disp:
	mov  esi, 0x90909090    ; init with NOPs
	mov  ebp, eax
	shr  ebp, 6
	and  ebp, 11b     ; Mod
	cmp  ebp, 01b     ; Mod=01, disp8
	je  .disp8
	cmp  ebp, 10b     ; Mod=10, disp32
	je  .disp32
	cmp  ebp, 11b     ; Mod=11 no displacement
	je  .lastByte
	mov  ebp, eax
	and  ebp, 111b    ; R/M
	cmp  ebp, 101b    ; Mod=00 and R/M=101, disp32
	je  .disp32
	cmp  ebp, 100b    ; Mod=00 and R/M=100, is SIB Base=101?
	jne .lastByte
	mov  ebp, eax
	shr  ebp, 8
	and  ebp, 111b    ; SIB Base value
	cmp  ebp, 101b    ; is Base=101?
	je  .disp32
	jmp .lastByte
.disp8:
	mov  esi, disp8value
	or   esi, 0x90909000
	jmp .lastByte
.disp32:
	mov  esi, disp32value
	jmp .lastByte

	; the last byte of LEA
.lastByte:
	mov  bh, 0x90
	test eax, 0x10000 ; SIB present?
	jz  .return
	mov  [es:0], esi  ; take last byte of displacement dword
	mov  bh, [es:3]

.return:
	ret


addr32CalcCMPValues:
	mov bl, al
	shr bl, 3
	and bl, 00000111b ; CMP R/M value = dest register of LEA
	or  bl, 00111000b ; CMP Opcode value = /7 (CMP r/m32,imm32 inst.)
	or  bl, 11000000b ; CMP Mod value = 11b (dest is register)

	xor edx, edx

	; calculate the cmp value address offset in the values tables
	; EBP = will be the base address in the values tables
	; EDX = will be the index in the values tables (then mult. by 4 bytes)

	; take cmp value from the ModRM table
	; cmp value at cs:[testModRMValues + al*4]
	mov dl, al
	and dl, 0x7 ; R/M
	mov dh, al
	shr dh, 6   ; Mod
	shl dh, 3   ; Mod*8
	add dl, dh  ; DL = Mod*8 + R/M
	mov ebp, addr32Values

	test eax, 0x10000 ; is SIB used?
	jz .return

	; take cmp value from the SIB tables
	; cmp value at cs:[testModRMValuesSIB00 + 1024*SIBtblIdx + ah*4]
	mov dl, ah
	mov ebp, eax
	shr ebp, 6
	and ebp, 11b  ; SIBtblIdx (SIB table index)
	shl ebp, 10   ; 256values * 4bytes
	add ebp, addr32ValuesSIB00 ; SIB tables base

.return:
	ret


addr32CalcMOVValues:
	mov dh, al
	and dh, 00111000b ; MOV Reg value = Reg of LEA's ModRM
	or  dh, 00000101b ; MOV R/M value = disp32
	;or  dh, 00000000b ; MOV Mod value = 00b (dest is memory)
	ret


addr32CopyValues:
	mov [leaModRMOff],    al
	mov [leaSIBDispOff],  ah
	mov [edi],            esi
	mov [leaLastByteOff], bh
	mov [cmpModRMOff],    bl
	mov [movModRMOff],    dh
	and edx, 0xFF
	mov edx, [cs:ebp + edx*4]
	mov [cmpImm32Off],    edx
	ret


addr32PrintStatus:
	call printEAX
	;xchg ecx, edx
	;call printEDX
	;xchg ecx, edx
	;call printEOL
	ret


addr32PrintResult:
	push eax
	mov  eax, [es:0]
	call printEAX
	call printEOL
	pop  eax
	ret


addr32Values:
	dd 0x00000001
	dd 0x00000004
	dd 0x00000008
	dd 0x00000002
	dd 0x0 ; SIB table 00
	dd 0x80000000
	dd 0x00000080
	dd 0x00000100
	dd 0xFFFFFF81
	dd 0xFFFFFF84
	dd 0xFFFFFF88
	dd 0xFFFFFF82
	dd 0x0 ; SIB table 01
	dd 0xFFFFFFC0
	dd 0x00000000
	dd 0x00000080
	dd 0x80000001
	dd 0x80000004
	dd 0x80000008
	dd 0x80000002
	dd 0x0 ; SIB table 10
	dd 0x80000040
	dd 0x80000080
	dd 0x80000100
addr32ValuesSIB00:
	dd 0x00000002
	dd 0x00000005
	dd 0x00000009
	dd 0x00000003
	dd 0x00000021
	dd 0x80000001
	dd 0x00000081
	dd 0x00000101
	dd 0x00000005
	dd 0x00000008
	dd 0x0000000C
	dd 0x00000006
	dd 0x00000024
	dd 0x80000004
	dd 0x00000084
	dd 0x00000104
	dd 0x00000009
	dd 0x0000000C
	dd 0x00000010
	dd 0x0000000A
	dd 0x00000028
	dd 0x80000008
	dd 0x00000088
	dd 0x00000108
	dd 0x00000003
	dd 0x00000006
	dd 0x0000000A
	dd 0x00000004
	dd 0x00000022
	dd 0x80000002
	dd 0x00000082
	dd 0x00000102
	dd 0x00000001
	dd 0x00000004
	dd 0x00000008
	dd 0x00000002
	dd 0x00000020
	dd 0x80000000
	dd 0x00000080
	dd 0x00000100
	dd 0x00000041
	dd 0x00000044
	dd 0x00000048
	dd 0x00000042
	dd 0x00000060
	dd 0x80000040
	dd 0x000000C0
	dd 0x00000140
	dd 0x00000081
	dd 0x00000084
	dd 0x00000088
	dd 0x00000082
	dd 0x000000A0
	dd 0x80000080
	dd 0x00000100
	dd 0x00000180
	dd 0x00000101
	dd 0x00000104
	dd 0x00000108
	dd 0x00000102
	dd 0x00000120
	dd 0x80000100
	dd 0x00000180
	dd 0x00000200
	dd 0x00000003
	dd 0x00000006
	dd 0x0000000A
	dd 0x00000004
	dd 0x00000022
	dd 0x80000002
	dd 0x00000082
	dd 0x00000102
	dd 0x00000009
	dd 0x0000000C
	dd 0x00000010
	dd 0x0000000A
	dd 0x00000028
	dd 0x80000008
	dd 0x00000088
	dd 0x00000108
	dd 0x00000011
	dd 0x00000014
	dd 0x00000018
	dd 0x00000012
	dd 0x00000030
	dd 0x80000010
	dd 0x00000090
	dd 0x00000110
	dd 0x00000005
	dd 0x00000008
	dd 0x0000000C
	dd 0x00000006
	dd 0x00000024
	dd 0x80000004
	dd 0x00000084
	dd 0x00000104
	dd 0x00000001
	dd 0x00000004
	dd 0x00000008
	dd 0x00000002
	dd 0x00000020
	dd 0x80000000
	dd 0x00000080
	dd 0x00000100
	dd 0x00000081
	dd 0x00000084
	dd 0x00000088
	dd 0x00000082
	dd 0x000000A0
	dd 0x80000080
	dd 0x00000100
	dd 0x00000180
	dd 0x00000101
	dd 0x00000104
	dd 0x00000108
	dd 0x00000102
	dd 0x00000120
	dd 0x80000100
	dd 0x00000180
	dd 0x00000200
	dd 0x00000201
	dd 0x00000204
	dd 0x00000208
	dd 0x00000202
	dd 0x00000220
	dd 0x80000200
	dd 0x00000280
	dd 0x00000300
	dd 0x00000005
	dd 0x00000008
	dd 0x0000000C
	dd 0x00000006
	dd 0x00000024
	dd 0x80000004
	dd 0x00000084
	dd 0x00000104
	dd 0x00000011
	dd 0x00000014
	dd 0x00000018
	dd 0x00000012
	dd 0x00000030
	dd 0x80000010
	dd 0x00000090
	dd 0x00000110
	dd 0x00000021
	dd 0x00000024
	dd 0x00000028
	dd 0x00000022
	dd 0x00000040
	dd 0x80000020
	dd 0x000000A0
	dd 0x00000120
	dd 0x00000009
	dd 0x0000000C
	dd 0x00000010
	dd 0x0000000A
	dd 0x00000028
	dd 0x80000008
	dd 0x00000088
	dd 0x00000108
	dd 0x00000001
	dd 0x00000004
	dd 0x00000008
	dd 0x00000002
	dd 0x00000020
	dd 0x80000000
	dd 0x00000080
	dd 0x00000100
	dd 0x00000101
	dd 0x00000104
	dd 0x00000108
	dd 0x00000102
	dd 0x00000120
	dd 0x80000100
	dd 0x00000180
	dd 0x00000200
	dd 0x00000201
	dd 0x00000204
	dd 0x00000208
	dd 0x00000202
	dd 0x00000220
	dd 0x80000200
	dd 0x00000280
	dd 0x00000300
	dd 0x00000401
	dd 0x00000404
	dd 0x00000408
	dd 0x00000402
	dd 0x00000420
	dd 0x80000400
	dd 0x00000480
	dd 0x00000500
	dd 0x00000009
	dd 0x0000000C
	dd 0x00000010
	dd 0x0000000A
	dd 0x00000028
	dd 0x80000008
	dd 0x00000088
	dd 0x00000108
	dd 0x00000021
	dd 0x00000024
	dd 0x00000028
	dd 0x00000022
	dd 0x00000040
	dd 0x80000020
	dd 0x000000A0
	dd 0x00000120
	dd 0x00000041
	dd 0x00000044
	dd 0x00000048
	dd 0x00000042
	dd 0x00000060
	dd 0x80000040
	dd 0x000000C0
	dd 0x00000140
	dd 0x00000011
	dd 0x00000014
	dd 0x00000018
	dd 0x00000012
	dd 0x00000030
	dd 0x80000010
	dd 0x00000090
	dd 0x00000110
	dd 0x00000001
	dd 0x00000004
	dd 0x00000008
	dd 0x00000002
	dd 0x00000020
	dd 0x80000000
	dd 0x00000080
	dd 0x00000100
	dd 0x00000201
	dd 0x00000204
	dd 0x00000208
	dd 0x00000202
	dd 0x00000220
	dd 0x80000200
	dd 0x00000280
	dd 0x00000300
	dd 0x00000401
	dd 0x00000404
	dd 0x00000408
	dd 0x00000402
	dd 0x00000420
	dd 0x80000400
	dd 0x00000480
	dd 0x00000500
	dd 0x00000801
	dd 0x00000804
	dd 0x00000808
	dd 0x00000802
	dd 0x00000820
	dd 0x80000800
	dd 0x00000880
	dd 0x00000900
addr32ValuesSIB01:
	dd 0xFFFFFF82
	dd 0xFFFFFF85
	dd 0xFFFFFF89
	dd 0xFFFFFF83
	dd 0xFFFFFFA1
	dd 0xFFFFFFC1
	dd 0x00000001
	dd 0x00000081
	dd 0xFFFFFF85
	dd 0xFFFFFF88
	dd 0xFFFFFF8C
	dd 0xFFFFFF86
	dd 0xFFFFFFA4
	dd 0xFFFFFFC4
	dd 0x00000004
	dd 0x00000084
	dd 0xFFFFFF89
	dd 0xFFFFFF8C
	dd 0xFFFFFF90
	dd 0xFFFFFF8A
	dd 0xFFFFFFA8
	dd 0xFFFFFFC8
	dd 0x00000008
	dd 0x00000088
	dd 0xFFFFFF83
	dd 0xFFFFFF86
	dd 0xFFFFFF8A
	dd 0xFFFFFF84
	dd 0xFFFFFFA2
	dd 0xFFFFFFC2
	dd 0x00000002
	dd 0x00000082
	dd 0xFFFFFF81
	dd 0xFFFFFF84
	dd 0xFFFFFF88
	dd 0xFFFFFF82
	dd 0xFFFFFFA0
	dd 0xFFFFFFC0
	dd 0x00000000
	dd 0x00000080
	dd 0xFFFFFFC1
	dd 0xFFFFFFC4
	dd 0xFFFFFFC8
	dd 0xFFFFFFC2
	dd 0xFFFFFFE0
	dd 0x00000000
	dd 0x00000040
	dd 0x000000C0
	dd 0x00000001
	dd 0x00000004
	dd 0x00000008
	dd 0x00000002
	dd 0x00000020
	dd 0x00000040
	dd 0x00000080
	dd 0x00000100
	dd 0x00000081
	dd 0x00000084
	dd 0x00000088
	dd 0x00000082
	dd 0x000000A0
	dd 0x000000C0
	dd 0x00000100
	dd 0x00000180
	dd 0xFFFFFF83
	dd 0xFFFFFF86
	dd 0xFFFFFF8A
	dd 0xFFFFFF84
	dd 0xFFFFFFA2
	dd 0xFFFFFFC2
	dd 0x00000002
	dd 0x00000082
	dd 0xFFFFFF89
	dd 0xFFFFFF8C
	dd 0xFFFFFF90
	dd 0xFFFFFF8A
	dd 0xFFFFFFA8
	dd 0xFFFFFFC8
	dd 0x00000008
	dd 0x00000088
	dd 0xFFFFFF91
	dd 0xFFFFFF94
	dd 0xFFFFFF98
	dd 0xFFFFFF92
	dd 0xFFFFFFB0
	dd 0xFFFFFFD0
	dd 0x00000010
	dd 0x00000090
	dd 0xFFFFFF85
	dd 0xFFFFFF88
	dd 0xFFFFFF8C
	dd 0xFFFFFF86
	dd 0xFFFFFFA4
	dd 0xFFFFFFC4
	dd 0x00000004
	dd 0x00000084
	dd 0xFFFFFF81
	dd 0xFFFFFF84
	dd 0xFFFFFF88
	dd 0xFFFFFF82
	dd 0xFFFFFFA0
	dd 0xFFFFFFC0
	dd 0x00000000
	dd 0x00000080
	dd 0x00000001
	dd 0x00000004
	dd 0x00000008
	dd 0x00000002
	dd 0x00000020
	dd 0x00000040
	dd 0x00000080
	dd 0x00000100
	dd 0x00000081
	dd 0x00000084
	dd 0x00000088
	dd 0x00000082
	dd 0x000000A0
	dd 0x000000C0
	dd 0x00000100
	dd 0x00000180
	dd 0x00000181
	dd 0x00000184
	dd 0x00000188
	dd 0x00000182
	dd 0x000001A0
	dd 0x000001C0
	dd 0x00000200
	dd 0x00000280
	dd 0xFFFFFF85
	dd 0xFFFFFF88
	dd 0xFFFFFF8C
	dd 0xFFFFFF86
	dd 0xFFFFFFA4
	dd 0xFFFFFFC4
	dd 0x00000004
	dd 0x00000084
	dd 0xFFFFFF91
	dd 0xFFFFFF94
	dd 0xFFFFFF98
	dd 0xFFFFFF92
	dd 0xFFFFFFB0
	dd 0xFFFFFFD0
	dd 0x00000010
	dd 0x00000090
	dd 0xFFFFFFA1
	dd 0xFFFFFFA4
	dd 0xFFFFFFA8
	dd 0xFFFFFFA2
	dd 0xFFFFFFC0
	dd 0xFFFFFFE0
	dd 0x00000020
	dd 0x000000A0
	dd 0xFFFFFF89
	dd 0xFFFFFF8C
	dd 0xFFFFFF90
	dd 0xFFFFFF8A
	dd 0xFFFFFFA8
	dd 0xFFFFFFC8
	dd 0x00000008
	dd 0x00000088
	dd 0xFFFFFF81
	dd 0xFFFFFF84
	dd 0xFFFFFF88
	dd 0xFFFFFF82
	dd 0xFFFFFFA0
	dd 0xFFFFFFC0
	dd 0x00000000
	dd 0x00000080
	dd 0x00000081
	dd 0x00000084
	dd 0x00000088
	dd 0x00000082
	dd 0x000000A0
	dd 0x000000C0
	dd 0x00000100
	dd 0x00000180
	dd 0x00000181
	dd 0x00000184
	dd 0x00000188
	dd 0x00000182
	dd 0x000001A0
	dd 0x000001C0
	dd 0x00000200
	dd 0x00000280
	dd 0x00000381
	dd 0x00000384
	dd 0x00000388
	dd 0x00000382
	dd 0x000003A0
	dd 0x000003C0
	dd 0x00000400
	dd 0x00000480
	dd 0xFFFFFF89
	dd 0xFFFFFF8C
	dd 0xFFFFFF90
	dd 0xFFFFFF8A
	dd 0xFFFFFFA8
	dd 0xFFFFFFC8
	dd 0x00000008
	dd 0x00000088
	dd 0xFFFFFFA1
	dd 0xFFFFFFA4
	dd 0xFFFFFFA8
	dd 0xFFFFFFA2
	dd 0xFFFFFFC0
	dd 0xFFFFFFE0
	dd 0x00000020
	dd 0x000000A0
	dd 0xFFFFFFC1
	dd 0xFFFFFFC4
	dd 0xFFFFFFC8
	dd 0xFFFFFFC2
	dd 0xFFFFFFE0
	dd 0x00000000
	dd 0x00000040
	dd 0x000000C0
	dd 0xFFFFFF91
	dd 0xFFFFFF94
	dd 0xFFFFFF98
	dd 0xFFFFFF92
	dd 0xFFFFFFB0
	dd 0xFFFFFFD0
	dd 0x00000010
	dd 0x00000090
	dd 0xFFFFFF81
	dd 0xFFFFFF84
	dd 0xFFFFFF88
	dd 0xFFFFFF82
	dd 0xFFFFFFA0
	dd 0xFFFFFFC0
	dd 0x00000000
	dd 0x00000080
	dd 0x00000181
	dd 0x00000184
	dd 0x00000188
	dd 0x00000182
	dd 0x000001A0
	dd 0x000001C0
	dd 0x00000200
	dd 0x00000280
	dd 0x00000381
	dd 0x00000384
	dd 0x00000388
	dd 0x00000382
	dd 0x000003A0
	dd 0x000003C0
	dd 0x00000400
	dd 0x00000480
	dd 0x00000781
	dd 0x00000784
	dd 0x00000788
	dd 0x00000782
	dd 0x000007A0
	dd 0x000007C0
	dd 0x00000800
	dd 0x00000880
addr32ValuesSIB10:
	dd 0x80000002
	dd 0x80000005
	dd 0x80000009
	dd 0x80000003
	dd 0x80000021
	dd 0x80000041
	dd 0x80000081
	dd 0x80000101
	dd 0x80000005
	dd 0x80000008
	dd 0x8000000C
	dd 0x80000006
	dd 0x80000024
	dd 0x80000044
	dd 0x80000084
	dd 0x80000104
	dd 0x80000009
	dd 0x8000000C
	dd 0x80000010
	dd 0x8000000A
	dd 0x80000028
	dd 0x80000048
	dd 0x80000088
	dd 0x80000108
	dd 0x80000003
	dd 0x80000006
	dd 0x8000000A
	dd 0x80000004
	dd 0x80000022
	dd 0x80000042
	dd 0x80000082
	dd 0x80000102
	dd 0x80000001
	dd 0x80000004
	dd 0x80000008
	dd 0x80000002
	dd 0x80000020
	dd 0x80000040
	dd 0x80000080
	dd 0x80000100
	dd 0x80000041
	dd 0x80000044
	dd 0x80000048
	dd 0x80000042
	dd 0x80000060
	dd 0x80000080
	dd 0x800000C0
	dd 0x80000140
	dd 0x80000081
	dd 0x80000084
	dd 0x80000088
	dd 0x80000082
	dd 0x800000A0
	dd 0x800000C0
	dd 0x80000100
	dd 0x80000180
	dd 0x80000101
	dd 0x80000104
	dd 0x80000108
	dd 0x80000102
	dd 0x80000120
	dd 0x80000140
	dd 0x80000180
	dd 0x80000200
	dd 0x80000003
	dd 0x80000006
	dd 0x8000000A
	dd 0x80000004
	dd 0x80000022
	dd 0x80000042
	dd 0x80000082
	dd 0x80000102
	dd 0x80000009
	dd 0x8000000C
	dd 0x80000010
	dd 0x8000000A
	dd 0x80000028
	dd 0x80000048
	dd 0x80000088
	dd 0x80000108
	dd 0x80000011
	dd 0x80000014
	dd 0x80000018
	dd 0x80000012
	dd 0x80000030
	dd 0x80000050
	dd 0x80000090
	dd 0x80000110
	dd 0x80000005
	dd 0x80000008
	dd 0x8000000C
	dd 0x80000006
	dd 0x80000024
	dd 0x80000044
	dd 0x80000084
	dd 0x80000104
	dd 0x80000001
	dd 0x80000004
	dd 0x80000008
	dd 0x80000002
	dd 0x80000020
	dd 0x80000040
	dd 0x80000080
	dd 0x80000100
	dd 0x80000081
	dd 0x80000084
	dd 0x80000088
	dd 0x80000082
	dd 0x800000A0
	dd 0x800000C0
	dd 0x80000100
	dd 0x80000180
	dd 0x80000101
	dd 0x80000104
	dd 0x80000108
	dd 0x80000102
	dd 0x80000120
	dd 0x80000140
	dd 0x80000180
	dd 0x80000200
	dd 0x80000201
	dd 0x80000204
	dd 0x80000208
	dd 0x80000202
	dd 0x80000220
	dd 0x80000240
	dd 0x80000280
	dd 0x80000300
	dd 0x80000005
	dd 0x80000008
	dd 0x8000000C
	dd 0x80000006
	dd 0x80000024
	dd 0x80000044
	dd 0x80000084
	dd 0x80000104
	dd 0x80000011
	dd 0x80000014
	dd 0x80000018
	dd 0x80000012
	dd 0x80000030
	dd 0x80000050
	dd 0x80000090
	dd 0x80000110
	dd 0x80000021
	dd 0x80000024
	dd 0x80000028
	dd 0x80000022
	dd 0x80000040
	dd 0x80000060
	dd 0x800000A0
	dd 0x80000120
	dd 0x80000009
	dd 0x8000000C
	dd 0x80000010
	dd 0x8000000A
	dd 0x80000028
	dd 0x80000048
	dd 0x80000088
	dd 0x80000108
	dd 0x80000001
	dd 0x80000004
	dd 0x80000008
	dd 0x80000002
	dd 0x80000020
	dd 0x80000040
	dd 0x80000080
	dd 0x80000100
	dd 0x80000101
	dd 0x80000104
	dd 0x80000108
	dd 0x80000102
	dd 0x80000120
	dd 0x80000140
	dd 0x80000180
	dd 0x80000200
	dd 0x80000201
	dd 0x80000204
	dd 0x80000208
	dd 0x80000202
	dd 0x80000220
	dd 0x80000240
	dd 0x80000280
	dd 0x80000300
	dd 0x80000401
	dd 0x80000404
	dd 0x80000408
	dd 0x80000402
	dd 0x80000420
	dd 0x80000440
	dd 0x80000480
	dd 0x80000500
	dd 0x80000009
	dd 0x8000000C
	dd 0x80000010
	dd 0x8000000A
	dd 0x80000028
	dd 0x80000048
	dd 0x80000088
	dd 0x80000108
	dd 0x80000021
	dd 0x80000024
	dd 0x80000028
	dd 0x80000022
	dd 0x80000040
	dd 0x80000060
	dd 0x800000A0
	dd 0x80000120
	dd 0x80000041
	dd 0x80000044
	dd 0x80000048
	dd 0x80000042
	dd 0x80000060
	dd 0x80000080
	dd 0x800000C0
	dd 0x80000140
	dd 0x80000011
	dd 0x80000014
	dd 0x80000018
	dd 0x80000012
	dd 0x80000030
	dd 0x80000050
	dd 0x80000090
	dd 0x80000110
	dd 0x80000001
	dd 0x80000004
	dd 0x80000008
	dd 0x80000002
	dd 0x80000020
	dd 0x80000040
	dd 0x80000080
	dd 0x80000100
	dd 0x80000201
	dd 0x80000204
	dd 0x80000208
	dd 0x80000202
	dd 0x80000220
	dd 0x80000240
	dd 0x80000280
	dd 0x80000300
	dd 0x80000401
	dd 0x80000404
	dd 0x80000408
	dd 0x80000402
	dd 0x80000420
	dd 0x80000440
	dd 0x80000480
	dd 0x80000500
	dd 0x80000801
	dd 0x80000804
	dd 0x80000808
	dd 0x80000802
	dd 0x80000820
	dd 0x80000840
	dd 0x80000880
	dd 0x80000900
