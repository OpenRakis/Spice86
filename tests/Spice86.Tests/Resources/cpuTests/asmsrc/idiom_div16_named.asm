; Build: fasm idiom_div16_named.asm ../idiom_div16_named.bin
use16

start:
    mov byte [0x0600], 0x01
    mov ax, 0x0000
    mov dx, 0x0002
    mov cx, 0x0003
    div cx
    mov byte [0x0601], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
