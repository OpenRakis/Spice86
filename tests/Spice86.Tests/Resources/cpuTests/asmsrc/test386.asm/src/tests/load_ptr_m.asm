;
;   Tests LSS,LDS,LES,LFS,LGS in 16 and 32 bit mode
;   %1 segment register name, one of ss,ds,es,fs,gs
;   [ed:di] memory address to use for the pointer
;   Uses: nothing
;

%macro testLoadPtr 1
	mov cx, %1
	mov dx, es

	mov [es:di], word 0x1234
	mov [es:di + 2], word 0xabcd
	l%1 bx, [es:di]
	mov ax, %1
	cmp ax, 0xabcd
	jne error
	cmp bx, 0x1234
	jne error

	mov es, dx

	mov [es:di], dword 0x12345678
	mov [es:di + 4], word 0xbcde
	l%1 ebx, [es:di]
	mov ax, %1
	cmp ax, 0xbcde
	jne error
	cmp ebx, 0x12345678
	jne error

	mov es, dx
	mov %1, cx
%endmacro
