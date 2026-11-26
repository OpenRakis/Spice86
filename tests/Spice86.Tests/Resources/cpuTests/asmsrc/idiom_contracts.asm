; Build: fasm idiom_contracts.asm ../idiom_contracts.bin
use16

start:
    mov byte [0x0600], 0x01
    mov ax, 0
    mov ds, ax
    mov word [0x0200], handler
    mov word [0x0202], 0xF000
    mov byte [0x0601], 0xDD
    int 0x80
    mov byte [0x0602], al
    hlt
handler:
    mov al, 0x42
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
