; Build: fasm idiom_relaxation_base.asm ../idiom_relaxation_base.bin
use16

start:
    mov byte [0x0600], 0x01
    mov ax, word [0x2000]
    mov byte [0x0601], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
