;
;   Test of loop, loopz, loopnz
;   Use: EAX, ECX, Flags
;

%macro testLoop 0

	mov ecx, 0x20000
	mov eax, 0
%%loop16:
	inc eax
	a16 loop %%loop16
	cmp eax, 0x10000
	jne error
	cmp ecx, 0x20000
	jne error

	mov ecx, 0x20000
	mov eax, 0
%%loop32:
	inc eax
	a32 loop %%loop32
	cmp eax, 0x20000
	jne error
	cmp ecx, 0
	jne error

%endmacro

%macro testLoopZ 0

	mov cx, 0xFFFF
	mov ax, 0
%%loop16a:
	inc ax
	cmp ah, 0
	a16 loopz %%loop16a
	cmp ax, 0x0100
	jne error
	cmp cx, 0xFEFF
	jne error

	mov cx, 0x00FF
	mov ax, 0
%%loop16b:
	inc ax
	cmp ah, 0
	a16 loopz %%loop16b
	cmp ax, 0x00FF
	jne error
	cmp cx, 0
	jne error

	mov ecx, 0x20000
	mov eax, 0
%%loop32:
	inc eax
	test eax, 0x10000
	a32 loopz %%loop32
	cmp eax, 0x10000
	jne error
	cmp ecx, 0x10000
	jne error

%endmacro

%macro testLoopNZ 0

	mov cx, 0xFFFF
	mov ax, 0
%%loop16a:
	inc ax
	test al, 0xFF
	a16 loopnz %%loop16a
	cmp ax, 0x0100
	jne error
	cmp cx, 0xFEFF
	jne error

	mov cx, 0x00FF
	mov ax, 0
%%loop16b:
	inc ax
	test al, 0xFF
	a16 loopnz %%loop16b
	cmp ax, 0x00FF
	jne error
	cmp cx, 0
	jne error

	mov ecx, 0x10000
	mov eax, 0
%%loop32:
	inc eax
	test eax, 0x0FFFF
	a32 loopnz %%loop32
	cmp eax, 0x10000
	jne error
	cmp ecx, 0
	jne error

%endmacro
