; Build: fasm idiom_memory_accessors.asm ../idiom_memory_accessors.bin
use16

start:
    mov byte [0x0600], 0x01
    mov bp, sp
    mov word [bp-4], 0x1234
    mov ax, word [bp-4]
    mov byte [0x0601], al
    mov bx, 0x0610
    mov word [bx], 0x5678
    mov cx, word [bx]
    mov byte [0x0602], cl
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
