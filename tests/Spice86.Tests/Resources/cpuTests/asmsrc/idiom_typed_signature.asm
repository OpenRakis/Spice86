; Build: fasm idiom_typed_signature.asm ../idiom_typed_signature.bin
use16

start:
    mov byte [0x0600], 0x01
    mov cx, 5
    call compute
    mov byte [0x0601], al
    mov byte [0x0602], dl
    hlt
compute:
    mov ax, cx
    add ax, 0x30
    mov dx, cx
    add dx, 0x40
    ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
