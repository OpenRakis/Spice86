;
;   Tests Call near by displacement and register indirect
;   Stack must be initilized.
;   %1: stack pointer register
;   Uses: AX, EBX, Flags
;
%macro testCallNear 1
	%ifidni %1,sp
	%define spcmp ax
	%else
	%define spcmp eax
	%endif
	mov spcmp, %1

%%rel16:
	clc
	o16 call word %%nearfn16
	jnc error
	jmp %%rel32
%%nearfn16:
	sub spcmp, 2
	cmp %1, spcmp
	jne error
	add spcmp, 2
	stc
	o16 ret
	jmp error

%%rel32:
	clc
	o32 call dword %%nearfn32
	jnc error
	jmp %%rm16
%%nearfn32:
	sub spcmp, 4
	cmp %1, spcmp
	jne error
	add spcmp, 4
	stc
	o32 ret
	jmp error

%%rm16:
	clc
	mov bx, %%nearfn16
	o16 call bx
	jnc error
%%rm32:
	clc
	mov ebx, %%nearfn32
	o32 call ebx
	jnc error
%endmacro

;
;   Tests Call far by immediate and memory pointers
;   Stack must be initilized
;   %1: code segment
;   Uses: AX, Flags, DS:SI as scratch memory
;
%macro testCallFar 1
	mov ax, sp

	clc
	o16 call word %1:%%farfn16
	jnc error
	jmp %%o32
%%farfn16:
	sub ax, 4
	cmp sp, ax
	jne error
	add ax, 4
	stc
	o16 retf
	jmp error

%%o32:
	clc
	o32 call dword %1:%%farfn32
	jnc error
	jmp %%m1616
%%farfn32:
	sub ax, 8
	cmp sp, ax
	jne error
	add ax, 8
	stc
	o32 retf
	jmp error

%%m1616:
	clc
	mov [si], word %%farfn16
	mov [si+2], word %1
	o16 call far [si]
	jnc error
%%m1632:
	clc
	mov [si], dword %%farfn32
	mov [si+4], word %1
	o32 call far [si]
	jnc error
%%exit:
%endmacro
