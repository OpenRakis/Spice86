; Build: fasm idiom_jump_table_switch.asm ../idiom_jump_table_switch.bin
use16

start:
    mov byte [0x0600], 0x01
    mov bx, 0
    mov ax, bx
    shl bx, 1
    jmp word [table+bx]
table:
    dw target0
    dw target1
target0:
    mov byte [0x0601], 0xD0
    hlt
target1:
    mov byte [0x0601], 0xD1
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
