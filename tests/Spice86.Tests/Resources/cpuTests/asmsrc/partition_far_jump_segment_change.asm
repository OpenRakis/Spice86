; compile it with fasm
; Far jumps into F100h, an alias of the same image (F100:0000 = file offset 1000h). The segment-B
; code is reached only through far jumps, so it stays in the generated method of its F000 caller and
; every such transfer must set CS. DS = SS = 2000h. In each section a word is written with a CS:
; override, read back with a CS: override, and read again through ES loaded with F100h: the CS-to-CS
; read-back gives the written value even when CS is wrong, while the ES read shows where the write
; really went. The scratch words sit in padding so a write with CS = F000 lands in unused bytes.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor bx, bx

; a constant jmp far F000 -> F100
    jmp 0F100h:seg_a - 1000h
ret_a:

; a jmp far through a memory pointer -> F100
    jmp dword [cs:jump_ptr]
ret_b:

; a constant jmp far -> F100 whose first CS read is a near call to a routine that pushes it
    jmp 0F100h:seg_c - 1000h
ret_c:
    hlt

jump_ptr:
    dw seg_b - 1000h, 0F100h

rb 1000h-$

; segment B, reached only through the far jumps above
seg_a:
    mov word [cs:8000h], 0AAAAh
    mov ax, [cs:8000h]
    mov [bx+00h], ax
    mov ax, 0F100h
    mov es, ax
    mov ax, [es:8000h]
    mov [bx+02h], ax
    push cs
    pop ax
    mov [bx+04h], ax
    jmp 0F000h:ret_a

seg_b:
    mov word [cs:8002h], 0BBBBh
    mov ax, [cs:8002h]
    mov [bx+06h], ax
    mov ax, 0F100h
    mov es, ax
    mov ax, [es:8002h]
    mov [bx+08h], ax
    push cs
    pop ax
    mov [bx+0Ah], ax
    jmp 0F000h:ret_b

seg_c:
    call near segc_routine
    jmp 0F000h:ret_c

segc_routine:
    push cs
    pop ax
    mov [bx+0Ch], ax
    retn

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
