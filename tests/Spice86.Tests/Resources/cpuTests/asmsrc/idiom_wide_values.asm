; Build: fasm idiom_wide_values.asm ../idiom_wide_values.bin
use16

start:
    mov byte [0x0400], 0x01
    mov byte [0x0410], 0x34
    mov byte [0x0411], 0x12
    mov ax, word [0x0410]
    mov byte [0x0401], ah
    mov word [0x0412], 0x5678
    mov word [0x0414], 0x1234
    mov bx, word [0x0412]
    mov byte [0x0402], bl
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
