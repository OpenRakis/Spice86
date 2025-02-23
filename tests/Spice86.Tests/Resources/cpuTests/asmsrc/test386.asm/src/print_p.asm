strEAX:    db  "EAX=",0
strEDX:    db  "EDX=",0
strPS:     db  "PS=",0
strDE:     db  "#DE ",0 ; when this is displayed, it indicates a Divide Error exception
achSize    db  "BWD"
strUnkCPU: db  "Unsupported CPU family: ",0

;
;   printUnkCPU()
;
;   Uses: ESI, EAX, CL
;
printUnkCPU:
	mov esi, strUnkCPU
	call printStr
	mov eax, CPU_FAMILY
	mov cl, 1
	call printVal
	ret

;
;   printEOL()
;
;   Uses: None
;
printEOL:
	push    eax
;	mov     al,0x0d
;	call    printChar
	mov     al,0x0a
	call    printChar
	pop     eax
	ret

;
;   printChar(AL)
;
;   Uses: None
;
printChar:
	pushfd
	push   edx
	push   ecx
	push   eax
	mov    ah, al      ; save AL
	%if COM_PORT
	mov    dx, [cs:COMLSRports+(COM_PORT-1)*2]   ; EDX == COM LSR (Line Status Register)
.loop:
	in     al, dx
	test   al, 0x20    ; THR (Transmitter Holding Register) empty?
	jz     .loop       ; no
	mov    al, ah      ; restore AL
	mov    dx, [cs:COMTHRports+(COM_PORT-1)*2]   ; EDX -> COM2 THR (Transmitter Holding Register)
	out    dx, al
	jmp    $+2
	%endif
	%if LPT_PORT
	mov    dx, [cs:LPTports+(LPT_PORT-1)*2]
	inc    dx          ; STATUS register
.loop2:
	in     al, dx      ; wait !BUSY
	test   al, 0x80
	jz     .loop2      ; wait until it's 1
	dec    dx          ; DATA register
	mov    al, ah      ; restore AL
	out    dx, al      ; send the DATA, then STROBE
	jmp    $+2
	add    dx, 2       ; CONTROL register
	in     al, dx      ; read the current config
	or     al, 1       ; enable strobing bit
	out    dx, al
	and    al, 0xfe    ; disable strobing bit
	%if LPT_STROBING
	mov    ecx, LPT_STROBING
.loop3:
	loop   .loop3
	%endif
	out    dx, al
	mov    al, ah      ; restore AL
	%endif
	%if OUT_PORT
	out    OUT_PORT, al
	%endif
	pop    eax
	pop    ecx
	pop    edx
	popfd
	ret

;
;   printStr(ESI -> zero-terminated string)
;
;   Uses: ESI, Flags
;
printStr:
	push    eax
.loop:
	cs lodsb
	test    al, al
	jz      .done
	call    printChar
	jmp     .loop
.done:
	pop     eax
	ret

;
;   printVal(EAX == value, CL == number of hex digits)
;
;   Uses: EAX, ECX, Flags
;
printVal:
	shl    cl, 2  ; CL == number of bits (4 times the number of hex digits)
	jz     .done
.loop:
	sub    cl, 4
	push   eax
	shr    eax, cl
	and    al, 0x0f
	add    al, '0'
	cmp    al, '9'
	jbe    .digit
	add    al, 'A'-'0'-10
.digit:
	call   printChar
	pop    eax
	test   cl, cl
	jnz    .loop
.done:
	mov    al, ' '
	call   printChar
	ret

;
;   printOp(ESI -> instruction sequence)
;
;   Rewinds ESI to the start of the mnemonic preceding the instruction sequence and prints the mnemonic
;
;   Uses: None
;
printOp:
	pushfd
	pushad
.findSize:
	dec    esi
	mov    al, [cs:esi-1]
	cmp    al, 32
	jae    .findSize
	call   printStr
	movzx  eax, al
	mov    al, [cs:achSize+eax]
	call   printChar
	mov    al, ' '
	call   printChar
	popad
	popfd
	ret

;
;   printEAX()
;
;   Uses: None
;
printEAX:
	pushfd
	pushad
	mov     esi, strEAX
	call    printStr
	mov     cl, 8
	call    printVal
	popad
	popfd
	ret

;
;   printEDX()
;
;   Uses: None
;
printEDX:
	pushfd
	pushad
	mov    esi, strEDX
	call   printStr
	mov    cl, 8
	mov    eax, edx
	call   printVal
	popad
	popfd
	ret

;
;   printPS(ESI -> instruction sequence)
;
;   Uses: None
;
printPS:
	pushfd
	pushad
	pushfd
	pop    edx
.findType:
	dec    esi
	mov    al, [cs:esi-1]
	cmp    al, 32
	jae    .findType
	movzx  eax, byte [cs:esi-2]
	and    edx, [cs:typeMasks+eax*4]
	mov    esi, strPS
	call   printStr
	mov    cl, 4
	mov    eax, edx
	call   printVal
	popad
	popfd
	ret

;
;	printPS2
;	EAX -> mask
;
;	Uses: None
;
printPS2:
	pushfd
	pushad
	pushfd
	pop edx
	and edx, eax
	mov esi, strPS
	call printStr
	mov  cl, 4
	mov  eax, edx
	call printVal
	popad
	popfd
	ret
