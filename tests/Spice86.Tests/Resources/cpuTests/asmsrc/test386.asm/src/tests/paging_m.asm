TESTPAGE_LIN equ 0x0049F000 ; linear test address (PDE 1, PTE 9F, offset 0)
TESTPAGE_PDE equ TESTPAGE_LIN>>22 ; page directory entry
TESTPAGE_PTE equ 0x2000|((TESTPAGE_LIN>>12)&0x3FF) ; page table 1 entry

TESTPAGE_OFF equ (TESTPAGE_LIN&0xFFFFF)


;
; Updates the flags of a PTE
;
; %1 entry index
; %2 new flags (bits 11-0)
;
; Uses FS
;
%macro updPageFlags 2
	pushad
	pushf
	mov  eax, %1
	mov  edx, %2
	call updPageFlagsP
	popf
	popad
%endmacro


;
; Given a bitmask, set the value of specific PDE/PTE flags
;
; %1 entry index
; %2 flags mask
; %3 new flags value
;
; Uses FS
;
%macro setPageFlags 3
	pushad
	pushf
	mov  eax, %1
	mov  ecx, %2
	mov  edx, %3
	call setPageFlagsP
	popf
	popad
%endmacro

