;00: 01 00 ff ff
; compile it with fasm
use16
start:
; setup stack
mov ax,0
mov ss,ax
mov sp,6
; load code 1
mov si,code1
loadcode:
; offset to code to load in si (source is ds:si)
mov ax,word 0f000h
mov ds,ax
; destination es:di
mov es,ax
mov di,loadarea
; size cx
mov cx, word 1000
rep movsw
jmp loadarea

rb 2048-$
code1:
mov ax,word 0001h
push ax
; load next
mov si,code2
jmp 0F000h:loadcode

rb 3072-$
code2:
mov bx,0001h
shl bx,1
push bx ;0002h
; load next
mov si,code3
jmp 0F000h:loadcode

rb 4096-$
code3:
mov cx,0001h
inc cx
inc cx
push cx
; final test
hlt

rb 5120-$
loadarea:

; bios entry point at offset fff0
rb 65520-$
jmp start
rb 65535-$
db 0ffh
