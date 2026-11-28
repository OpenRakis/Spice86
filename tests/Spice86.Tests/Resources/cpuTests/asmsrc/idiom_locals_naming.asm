; Build: fasm idiom_locals_naming.asm ../idiom_locals_naming.bin
use16

start:
    mov byte [0x0600], 0x01
    mov si, 0
loop_top:
    mov byte [si+0x0610], 0xAA
    inc si
    cmp si, 4
    jb loop_top
    mov ax, si
    mov byte [0x0601], al
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
