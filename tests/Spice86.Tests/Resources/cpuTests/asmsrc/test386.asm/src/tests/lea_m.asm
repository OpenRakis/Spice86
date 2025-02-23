;
;   Execs LEA op with 16-bit addressing and compares the result with given value
;   %1 address to calculate
;   %2 value to compare
;   Uses: flags.
;
%macro testLEA16 2
	push ax
	a16 lea ax, %1
	cmp ax, %2
	jne error
	pop ax
%endmacro

;
;   Execs LEA op with 32-bit addressing and compares the result with given value
;   %1 address to calculate
;   %2 value to compare
;   Uses: flags.
;
%macro testLEA32 2
	push eax
	a32 lea eax, %1
	cmp eax, %2
	jne error
	pop eax
%endmacro
