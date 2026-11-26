; Build: fasm idiom_stack_save_restore.asm ../idiom_stack_save_restore.bin
use16

start:
    mov byte [0x0400], 0x01
    mov ax, 0x1111
    mov bx, 0x2222
    mov cx, 0x3333
    push ax
    push bx
    push cx
    mov ax, 0xDD
    mov byte [0x0401], al
    pop cx
    pop bx
    pop ax
    mov byte [0x0402], ah
    pop dx
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
