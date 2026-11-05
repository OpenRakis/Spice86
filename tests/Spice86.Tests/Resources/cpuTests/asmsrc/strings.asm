;00: XX XX 34 12

use16
start:
mov cx,0f000h
mov ds,cx
mov es,cx
mov si,01000h
mov di,02001h

cmpsb              ; (1) flags=0x97 (SAPC)
pushf
ret

rb 46h-$
cmpsb              ; (1) flags=0x82 (S)
pushf
ret

rb 082h-$
cmpsb              ; (1) flags=0x812 (OA)
pushf
ret

rb 097h-$
cmpsb              ; (1) flags=0x46 (ZP)
pushf
ret

rb 0812h-$
cmpsw              ; (2)
pushf
ret                ; jump to f000:0883

rb 0883h-$
mov ah,10h
std
lodsb              ; (3)
jmp ax           ; jump to f000:10c2

rb 01000h-$
db 001h,0ffh,0ffh,080h
dw 00002h
db 0c2h

rb 010c2h-$
lodsw              ; (4)
jmp ax

rb 01300h-$
mov word[di],ax
mov es,dx
scasw              ; (8)
jz stor

; Unobserved fallthrough of the jz above runs into the rb zero-padding below.
; A hlt here bounds speculative decoding so the explorer stops at a terminator
; instead of decoding the padding as a long run of bogus instructions.
hlt

rb 01350h-$
stor:
mov  al,80h
std
stosb              ; (9)
jmp word [di]

rb 02001h-$
db 002h,0ffh,001h,001h
dw 08001h

rb 08013h-$
mov ax,0d000h
stosw              ; (10)
jmp word [di+2]

rb 080c2h-$
mov ds,cx
movsw              ; (6)
mov ds,dx
jmp word [di+2]

rb 080ffh-$
mov di,2002h
mov es,cx
cld
scasb              ; (7)
lahf
jmp ax

rb 0c200h-$
mov dx,01000h
mov es,dx
mov di,0ffffh
movsb              ; (5) 
mov ds,dx
mov byte[di],0c2h
jmp word[di]

rb 0d000h-$
mov dx,0
mov ds,dx
mov word[2],01234h
jmp df_sections

; direction flag at entry. DS addresses the code image, ES segment 0 so
; the markers land in the memory dump; each section stores the post-op DI.
rb 0e000h-$
df_sections:
mov ax,cs
mov ds,ax
xor ax,ax
mov es,ax
; a callee called after cld from every caller folds the step
cld
mov si,df_src
mov di,20h
call df_copy_clear
mov [es:4],di
cld
mov si,df_src
mov di,24h
call df_copy_clear
mov [es:6],di
; a callee called once after std keeps the ? : step (the callers disagree)
cld
mov si,df_src
mov di,28h
call df_copy_mixed
mov [es:8],di
std
mov si,df_src
mov di,2ch
call df_copy_mixed
mov [es:0ah],di
; a callee that sets DF on one return path: the caller's next string op does not fold
cld
call df_sets_path
mov si,df_src
mov di,30h
movsw
mov [es:0ch],di
; a string op in a timer interrupt handler: DF at entry is unknown
mov word [es:8*4],df_irq0_handler
mov word [es:8*4+2],cs
mov byte [es:300h],0
mov al,00110110b
out 43h,al
mov al,51h
out 40h,al
mov al,22h
out 40h,al
mov al,0feh
out 21h,al
std
sti
wait_irq:
cmp byte [es:300h],0
je wait_irq
cli
mov al,0ffh
out 21h,al
; a block entered by a dynamic jump: DF at entry is unknown
mov ax,df_unknown_entry
std
mov si,df_src
mov di,38h
jmp ax

rb 0e100h-$
df_copy_clear:
movsw
ret
df_copy_mixed:
movsw
ret
df_sets_path:
test al,1
jz df_sets_path_set
ret
df_sets_path_set:
std
ret

rb 0e200h-$
df_unknown_entry:
movsw
mov [es:0eh],di
hlt

rb 0e300h-$
df_irq0_handler:
mov si,df_src
mov di,44h
movsw
mov [es:0],di
mov byte [es:300h],1
push ax
mov al,20h
out 20h,al
pop ax
iret

df_src:
dw 0abcdh,0beefh

rb 65520-$
jmp start

rb 65535-$
db 0ffh
