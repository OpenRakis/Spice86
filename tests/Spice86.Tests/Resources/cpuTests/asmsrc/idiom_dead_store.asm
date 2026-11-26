; Build: fasm idiom_dead_store.asm ../idiom_dead_store.bin
use16

start:
    mov byte [0x0400], 0x01
    mov word [0x0410], 0x1111
    mov word [0x0410], 0x2222
    mov ax, word [0x0410]
    mov byte [0x0401], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
