; Build: fasm idiom_relaxation_base.asm ../idiom_relaxation_base.bin
; unknown-memory-is-RAM: an indirect far access whose segment the analysis cannot prove.
; At runtime the pointer targets low RAM, so forwarding the store is correct.
use16

start:
    mov byte [0x0600], 0x01
    les bx, [cs:far_pointer]
    mov byte [es:bx], 0x42
    mov al, [es:bx]
    mov byte [0x0601], al
    hlt
far_pointer: dw 0x0610, 0x0000

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
