; Build: fasm idiom_div16_named.asm ../idiom_div16_named.bin
use16

start:
    mov byte [0x0400], 0x01
    mov ax, 0x0002
    mov dx, 0x0001
    mov cx, 0x0010
    div cx
    mov byte [0x0401], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
