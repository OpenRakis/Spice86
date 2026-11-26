; Build: fasm idiom_dead_store.asm ../idiom_dead_store.bin
; a store overwritten before any read is eliminated. Stores go to scratch in the DS=2000h
; segment (the shape elimination applies to); the result bytes are written through ES=0.
use16

start:
    mov ax, 0x2000
    mov ds, ax
    mov byte [es:0x0600], 0x01
    mov word [0x20], 0x1111
    mov word [0x20], 0x2222
    mov ax, word [0x20]
    mov byte [es:0x0601], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
