; Build: fasm idiom_fault_dispatch.asm ../idiom_fault_dispatch.bin
use16

start:
    mov byte [0x0400], 0x01
    mov ax, 0
    mov ds, ax
    mov word [0x0000], fault_handler
    mov word [0x0002], 0xF000
    xor dx, dx
    xor cx, cx
    mov ax, 5
    div cx
    mov byte [0x0401], 0xFF
    hlt
fault_handler:
    mov byte [0x0401], 0x42
    add sp, 0
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
