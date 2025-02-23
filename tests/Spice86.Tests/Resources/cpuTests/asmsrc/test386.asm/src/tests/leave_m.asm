;
; IF StackAddressSize = 32
; THEN
;  ESP ← EBP
; ELSE
;  SP ← BP
; FI
;
; IF OperandSize = 32
;  EBP ← Pop32()
; ELSE
;  BP ← Pop16()
; FI
;
; %1 operand size (o16,o32)
; %2 stack size (16,32)
%macro testLEAVE 2
	%if %2 = 16
		mov  ax, D_SEG_PROT16
		%ifidni %1,o16
			%assign espResult 0x00000006
			%assign ebpResult 0x00015678
		%else
			%assign espResult 0x00000008
			%assign ebpResult 0x12345678
		%endif
	%else
		mov  ax, D_SEG_PROT32
		%ifidni %1,o16
			%assign espResult 0x00010006
			%assign ebpResult 0x00015678
		%else
			%assign espResult 0x00010008
			%assign ebpResult 0x12345678
		%endif
	%endif
	mov  ss, ax
	mov  esp, 0x10008
	push dword 0x12345678
	mov  ebp, esp
	mov  esp, 0
	%1 leave
	cmp  esp, espResult
	jne  error
	cmp  ebp, ebpResult
	jne  error
%endmacro

