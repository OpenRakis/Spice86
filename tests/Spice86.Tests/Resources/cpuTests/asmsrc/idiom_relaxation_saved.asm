; Build: fasm idiom_relaxation_saved.asm ../idiom_relaxation_saved.bin
; saved-slots: a method saves registers around a call and restores them after.
use16

start:
    mov byte [0x0600], 0x01
    mov bx, 0x11
    mov cx, 0x22
    call worker
    mov byte [0x0601], bl
    mov byte [0x0602], cl
    hlt
worker:
    push bx
    push cx
    mov bx, 0x99
    mov cx, 0x88
    call leaf
    pop cx
    pop bx
    ret
leaf:
    mov ax, 0x55
    ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
