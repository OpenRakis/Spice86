;
; Tests for VERR and VERW instructions
;
; DX = segment selector
; BL = expected ZF value on bit 6, other bits to 0
;
testVERRp:
	mov ah, bl
	not ah
	sahf
	verr dx
	jmp testVERresult
testVERWp:
	mov ah, bl
	not ah
	sahf
	verw dx
testVERresult:
	lahf
	and ah, 0x40
	cmp bl, ah
	jne error
	ret

;
; %1 = segment selector
; %2 = 0 or 1 (expected ZF result)
;
%macro testVERR 2
	mov dx, %1
	mov bl, ((%2 & 1) << 6)
	call testVERRp
%endmacro
%macro testVERW 2
	mov dx, %1
	mov bx, ((%2 & 1) << 6)
	call testVERWp
%endmacro
