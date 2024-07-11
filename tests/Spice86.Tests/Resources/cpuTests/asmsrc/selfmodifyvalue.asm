;00: 01 00 ff ff
; compile it with fasm
use16
start:
mov ax,0
mov ss,ax
mov sp,4
selmodifiedmov:
mov ax,0ffffh
push ax
mov word[cs:selmodifiedmov+1],0001h ; modify value to put in AX next time
; loop back to mov if AX is 0ffffh
cmp ax,word 0ffffh
je selmodifiedmov
;stack should be 0100ffff
hlt

; bios entry point at offset fff0
rb 65520-$
jmp start
rb 65535-$
db 0ffh
