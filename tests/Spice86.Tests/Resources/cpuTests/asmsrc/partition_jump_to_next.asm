use16
start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    xor dx, dx
    mov ax, 1
    cmp ax, 1
    je next
next:
    add dx, 1
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
