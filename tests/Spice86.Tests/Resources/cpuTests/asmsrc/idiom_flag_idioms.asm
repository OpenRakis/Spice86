; Build: fasm idiom_flag_idioms.asm ../idiom_flag_idioms.bin
use16

start:
    mov byte [0x0400], 0x01
    stc
    jc carry_set
    mov byte [0x0401], 0x00
    jmp next
carry_set:
    mov byte [0x0401], 0x01
next:
    mov al, 5
    test al, al
    setnz byte [0x0402]
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
