;
; Tests byte set on condition
; %1 destination operand of setcc op
; Needs the stack
; Uses: AX, Flags
;
; Opcodes tested:
; opcode mnemonic condition
; OF90   SETO     OF=1
; OF91   SETNO    OF=O
; OF92   SETC     CF=1
; OF93   SETNC    CF=O
; OF94   SETZ     ZF=1
; OF95   SETNZ    ZF=O
; OF96   SETBE    CF=1 || ZF=1
; OF97   SETA     CF=O && ZF=O
; OF98   SETS     SF=1
; OF99   SETNS    SF=O
; OF9A   SETP     PF=1
; OF9B   SETNP    PF=0
; OF9C   SETL     SF!=OF
; OF9D   SETNL    SF=OF
; OF9E   SETLE    ZF=1 || SF!=OF
; OF9F   SETNLE   ZF=O && SF=OF
;
%macro SETcc 3
	%1 %2
	pushf
	cmp %2, %3
	jne error
	popf
%endmacro

%macro testSetcc 1
	mov    ah, PS_CF|PS_ZF|PS_SF|PS_PF
	sahf
	SETcc  setc, %1,1  ; OF92   SETC     CF=1
	SETcc  setnc,%1,0  ; OF93   SETNC    CF=O
	SETcc  setz, %1,1  ; OF94   SETZ     ZF=1
	SETcc  setnz,%1,0  ; OF95   SETNZ    ZF=O
	SETcc  sets, %1,1  ; OF98   SETS     SF=1
	SETcc  setns,%1,0  ; OF99   SETNS    SF=O
	SETcc  setp, %1,1  ; OF9A   SETP     PF=1
	SETcc  setnp,%1,0  ; OF9B   SETNP    PF=0
	SETcc  setbe,%1,1  ; OF96   SETBE    CF=1 || ZF=1
	SETcc  seta, %1,0  ; OF97   SETA     CF=O && ZF=O

	mov    ax, 0
	sahf
	SETcc  setc, %1,0  ; OF92   SETC     CF=1
	SETcc  setnc,%1,1  ; OF93   SETNC    CF=O
	SETcc  setz, %1,0  ; OF94   SETZ     ZF=1
	SETcc  setnz,%1,1  ; OF95   SETNZ    ZF=O
	SETcc  sets, %1,0  ; OF98   SETS     SF=1
	SETcc  setns,%1,1  ; OF99   SETNS    SF=O
	SETcc  setp, %1,0  ; OF9A   SETP     PF=1
	SETcc  setnp,%1,1  ; OF9B   SETNP    PF=0
	SETcc  setbe,%1,0  ; OF96   SETBE    CF=1 || ZF=1
	SETcc  seta, %1,1  ; OF97   SETA     CF=O && ZF=O

	mov   al, 1000000b
	shl   al, 1          ; OF = high-order bit of AL <> (CF), ZF=0,SF=1,OF=1
	SETcc seto,   %1, 1  ; OF90   SETO     OF=1
	SETcc setno,  %1, 0  ; OF91   SETNO    OF=O
	SETcc setl,   %1, 0  ; OF9C   SETL     SF!=OF
	SETcc setnl,  %1, 1  ; OF9D   SETNL    SF=OF
	SETcc setle,  %1, 0  ; OF9E   SETLE    ZF=1 || SF!=OF
	SETcc setnle, %1, 1  ; OF9F   SETNLE   ZF=O && SF=OF

	mov ah, PS_ZF
	sahf                 ; ZF=1,SF=0,OF=1
	SETcc setl,   %1, 1  ; OF9C   SETL     SF!=OF
	SETcc setnl,  %1, 0  ; OF9D   SETNL    SF=OF
	SETcc setle,  %1, 1  ; OF9E   SETLE    ZF=1 || SF!=OF
	SETcc setnle, %1, 0  ; OF9F   SETNLE   ZF=O && SF=OF
%endmacro
