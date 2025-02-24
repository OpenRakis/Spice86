; ENTER imm16, imm8

; OperandSize = 16
; ****************
;
; 01 AllocSize ← imm16;
; 02 NestingLevel ← imm8 MOD 32;
;
; 03 Push16(BP);
; 04 FrameTemp ← SP;
;
; 05 IF NestingLevel = 0 THEN
; 06 	GOTO CONTINUE;
; 07 FI;
;
; 08 IF (NestingLevel > 1) THEN
; 09 	FOR i ← 1 to (NestingLevel - 1) DO
; 10		IF StackSize = 32 THEN
; 11			EBP ← EBP - 2;
; 12			Push16([EBP]);
; 13		ELSE (* StackSize = 16 *)
; 14			BP ← BP - 2;
; 15			Push16([BP]);
; 16		FI;
; 17	OD;
; 18 FI;
;
; 19 Push16(FrameTemp);
;
; 20 CONTINUE:
; 21 BP ← FrameTemp;
; 22 SP ← SP − AllocSize;

; %1=AllocSize
; %2=NestingLevel
; %3=StackSize
%macro testENTER16 3
	%assign NestingLevel (%2 % 32)
	%assign stackWords 1 ; 03
	%if NestingLevel>0
		%if NestingLevel>1
			%assign stackWords stackWords+(NestingLevel-1) ; 12/15
		%endif
		%assign stackWords stackWords+1 ; 19
	%endif
	%assign stackBytes stackWords*2
	%if %3=16
		%assign val32 0x10000
		mov   ax, D_SEG_PROT16
	%else
		%assign val32 0
		mov   ax, D_SEG_PROT32
	%endif
	%assign ebpVal    val32|(0xfffe-(stackBytes-2))
	%assign ebpResult val32| 0xfffe
	%assign espResult val32|(0xfffe-%1-(stackBytes-2))

	mov   ss, ax
	mov   es, ax

	; clear stack memory
	mov   ecx, stackWords
	xor   edi, edi
	sub   di, stackBytes
	xor   ax, ax
	rep stosw

	mov   ebp, ebpVal
	%if NestingLevel>1
		mov   ecx, (NestingLevel-1)
		%%initbp:
		%if %3=16
		sub   bp, 2
		mov   [bp], cx
		%else
		sub   ebp, 2
		mov   [ebp], cx
		%endif
		loop %%initbp
		mov   ebp, ebpVal
	%endif

	mov   esp, 0x10000
	o16 enter %1, %2
	cmp   esp, espResult
	jne   error
	cmp   ebp, ebpResult
	jne   error
	cmp   [bp], word (ebpVal&0xffff)
	jne   error
	%if NestingLevel>0
		add   esp, %1
		mov   bx, sp
		cmp   [es:bx], bp
		jne   error
		%if NestingLevel>1
			mov   ecx, (NestingLevel-1)
			add   bx, 2+(NestingLevel-1)*2
			%%testNesting:
			sub   bx, 2
			cmp   [es:bx], cx
			jne   error
			loop %%testNesting
		%endif
	%endif
%endmacro


; OperandSize = 32
; ****************
;
; AllocSize ← imm16;
; NestingLevel ← imm8 MOD 32;
;
; Push32(EBP);
; FrameTemp ← ESP;
;
; IF NestingLevel = 0 THEN
; 	GOTO CONTINUE;
; FI;
;
; IF (NestingLevel > 1) THEN
; 	FOR i ← 1 to (NestingLevel - 1) DO
; 		IF StackSize = 32
; 			EBP ← EBP - 4;
; 			Push32([EBP]);
; 		ELSE (* StackSize = 16 *)
; 			BP ← BP - 4;
; 			Push32([BP]);
; 		FI;
; 	OD;
; FI;
;
; Push32(FrameTemp);
;
; CONTINUE:
; EBP ← FrameTemp;
; ESP ← ESP − AllocSize;
;
%macro testENTER32 3
	%assign NestingLevel (%2 % 32)
	%assign stackDoubles 1
	%if NestingLevel>0
		%if NestingLevel>1
			%assign stackDoubles stackDoubles+(NestingLevel-1)
		%endif
		%assign stackDoubles stackDoubles+1
	%endif
	%assign stackBytes stackDoubles*4
	%if %3=16
		%assign val32 0x10000
		mov   ax, D_SEG_PROT16
	%else
		%assign val32 0
		mov   ax, D_SEG_PROT32
	%endif
	%assign ebpVal    val32|(0xfffc-(stackBytes-4))
	%assign ebpResult val32| 0xfffc
	%assign espResult val32|(0xfffc-%1-(stackBytes-4))

	mov   ss, ax
	mov   es, ax

	; clear stack memory
	mov   ecx, stackDoubles
	xor   edi, edi
	sub   di, stackBytes
	xor   eax, eax
	rep stosd

	mov   ebp, ebpVal
	%if NestingLevel>1
		mov   ecx, (NestingLevel-1)
		%%initbp:
		%if %3=16
		sub   bp, 4
		mov   [bp], ecx
		%else
		sub   ebp, 4
		mov   [ebp], ecx
		%endif
		loop %%initbp
		mov   ebp, ebpVal
	%endif

	mov   esp, 0x10000
	o32 enter %1, %2
	cmp   esp, espResult
	jne   error
	cmp   ebp, ebpResult
	jne   error
	cmp   [bp], dword ebpVal
	jne   error
	%if NestingLevel>0
		add   esp, %1
		mov   ebx, esp
		cmp   [es:bx], ebp
		jne   error
		%if NestingLevel>1
			mov   ecx, (NestingLevel-1)
			add   ebx, 4+(NestingLevel-1)*4
			%%testNesting:
			sub   ebx, 4
			cmp   [es:bx], ecx
			jne   error
			loop %%testNesting
		%endif
	%endif
%endmacro
