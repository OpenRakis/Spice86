;
; Tests XCHG operation
; %1 dst operand
; %2 src operand
;
; Both dst and src will be overwritten.
;
%macro testXchg 2
	%assign dst_value 0xA5A5A5A5
	%assign src_value 0x5A5A5A5A
	mov %1,dst_value
	mov %2,src_value
	xchg %1,%2
	cmp %1,src_value
	jne error
	cmp %2,dst_value
	jne error
%endmacro
