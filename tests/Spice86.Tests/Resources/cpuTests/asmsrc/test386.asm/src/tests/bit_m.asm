%macro testBitscan 1
	mov edx, 1
	shl edx, 31
	mov ecx, 31
%%loop32:
	o32 %1  ebx, edx
	shr edx, 1
	lahf
	cmp ebx, ecx
	jne error
	sahf
	loopne %%loop32 ; if CX>0 ZF must be 0
	cmp ecx, 0
	jne error ; CX must be 0

	mov dx, 1
	shl dx, 15
	mov cx, 15
%%loop16:
	o16 %1  bx, dx
	shr dx, 1
	lahf
	cmp bx, cx
	jne error
	sahf
	loopne %%loop16 ; if CX>0 ZF must be 0
	cmp cx, 0
	jne error ; CX must be 0
%endmacro


%macro testBittest16 1
	mov edx, 0x0000aaaa
	mov cx, 15
%%loop:
	o16 %1 dx, cx
	lahf ; save CF
	test cx, 1
	jz %%zero
%%one:
	sahf ; bit in CF must be 1
	jnb error
	jmp %%next
%%zero:
	sahf ; bit in CF must be 0
	jb error
%%next:
	dec cx
	jns %%loop
%endmacro

%macro testBittest32 1
	mov edx, 0xaaaaaaaa
	mov ecx, 31
%%loop:
	o32 %1 edx, ecx
	lahf ; save CF
	test ecx, 1
	jz %%zero
%%one:
	sahf ; bit in CF must be 1
	jnb error
	jmp %%next
%%zero:
	sahf ; bit in CF must be 0
	jb error
%%next:
	dec ecx
	jns %%loop
%endmacro

;
;   Executes a bit test operation and checks the resulting flags.
;
;   %1 ax: word operand
;   %2 imm8: bit index
;   %3 flags: value of flags before op execution
;   %4 flags: expected value of flags after op execution (cmp with PS_ARITH mask)
;
;   Uses: EAX, ECX, Flags
;
%macro testBittestFlags 4
	testBittestWFlags bt,  %1, %2, %3, %4
	testBittestWFlags btc, %1, %2, %3, %4
	testBittestWFlags btr, %1, %2, %3, %4
	testBittestWFlags bts, %1, %2, %3, %4

	testBittestDFlags bt,  %1, %2, %3, %4
	testBittestDFlags btc, %1, %2, %3, %4
	testBittestDFlags btr, %1, %2, %3, %4
	testBittestDFlags bts, %1, %2, %3, %4
%endmacro

%macro testBittestWFlags 5
	; bt ax, imm8
	mov ax, %4
	push ax
	popf
	mov ax, %2
	o16 %1 ax, %3
	pushf
	pop ax
	and ax, PS_ARITH
	cmp ax, %5
	jne error

	; bt ax, cx
	mov ax, %4
	push ax
	popf
	mov ax, %2
	mov cx, %3
	o16 %1 ax, cx
	pushf
	pop ax
	and ax, PS_ARITH
	cmp ax, %5
	jne error
%endmacro

%macro testBittestDFlags 5
	; bt eax, imm8
	mov ax, %4
	push ax
	popf
	mov eax, %2
	o32 %1 eax, %3
	pushf
	pop ax
	and ax, PS_ARITH
	cmp ax, %5
	jne error

	; bt eax, ecx
	mov ax, %4
	push ax
	popf
	mov eax, %2
	mov ecx, %3
	o32 %1 eax, ecx
	pushf
	pop ax
	and ax, PS_ARITH
	cmp ax, %5
	jne error
%endmacro
