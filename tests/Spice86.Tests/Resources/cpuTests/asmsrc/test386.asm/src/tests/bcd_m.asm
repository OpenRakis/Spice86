;
;   Executes and prints the results of a BCD operation.
;   No checks are performed.
;
;	%1 operation
;   %2 eax: the operand
;   %3 flags: value of flags before %1 execution
;   %4 flags mask: print only the resulting flags that are defined by this mask
;
;   Uses: EAX, Flags
;
%define quot "
%macro testBCD 4
	mov esi, %%name
	call printStr
	mov eax, %2
	call printEAX ; print the operand
	mov eax, %3   ; print only the flags that have been set
	push %3
	popf
	call printPS2 ; print the Processor Status (flags) before execution

	mov eax, %2
	push %3
	popf
	%1
	pushfd

	jmp %%printres

%%name:
	db quot %+ %1 %+ quot,' ',0

%%printres:
	call  printEAX ; print the result
	mov eax, %4
	popf
	call  printPS2 ; print the Processor Status (flags) after execution
	call  printEOL
%endmacro


;
;   Executes a BCD operation and checks the resulting flags.
;
;	%1 operation
;   %2 ax: the operand
;   %3 flags: value of flags before %1 execution
;   %4 flags: expected value of flags after %1 execution (cmp with PS_ARITH mask)
;
;   Uses: AX, Flags
;
%macro testBCDflags 4
	mov ax, %3
	push ax
	popf
	mov ax, %2
	%1
	pushf
	pop ax
	and ax, PS_ARITH
	cmp ax, %4
	jne error
%endmacro
