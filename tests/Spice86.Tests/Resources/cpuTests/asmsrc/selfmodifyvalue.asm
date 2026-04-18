;00: 0a 00 ff ff
; compile it with fasm
use16
start:
mov ax,0
mov ss,ax
mov sp,4
mov bx,10
 ; single-successor predecessor: CanHaveMoreSuccessors becomes false after linking, ensuring DetermineToExecute handles stale successors without relying on the linker
selmodifiedmovjmp:
jmp selmodifiedmov
selmodifiedmov:
mov ax,0ffffh
push ax
mov word[cs:selmodifiedmov+1],bx ; modify value to put in AX next time
dec bx
; loop back to mov if AX is not 0
cmp ax,0
jne selmodifiedmovjmp
;stack should be 0a00ffff
hlt

; bios entry point at offset fff0
rb 65520-$
jmp start
rb 65535-$
db 0ffh
