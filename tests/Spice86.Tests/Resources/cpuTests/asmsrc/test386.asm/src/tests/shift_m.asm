;
;   Executes a byte shift operation and checks the resulting flags.
;
;   %1 operation
;   %2 al: byte operand
;   %3 cl: shift count
;   %4 flags: value of flags before %1 execution
;   %5 flags: expected value of flags after %1 execution (cmp with PS_ARITH mask)
;
;   Uses: AX, CL, Flags
;
%macro testShiftBFlags 5
	mov ax, %4
	push ax
	popf
	mov ah, 0xff
	mov al, %2
	mov cl, %3
	%1 al, cl
	pushf
	pop ax
	and ax, PS_ARITH
	cmp ax, %5
	jne error
%endmacro

;
;   Executes a word shift operation and checks the resulting flags.
;
;   %1 operation
;   %2 ax: word operand
;   %3 cl: shift count
;   %4 flags: value of flags before %1 execution
;   %5 flags: expected value of flags after %1 execution (cmp with PS_ARITH mask)
;
;   Uses: AX, CL, Flags
;
%macro testShiftWFlags 5
	mov ax, %4
	push ax
	popf
	mov ax, %2
	mov cl, %3
	%1 ax, cl
	pushf
	pop ax
	and ax, PS_ARITH
	cmp ax, %5
	jne error
%endmacro
