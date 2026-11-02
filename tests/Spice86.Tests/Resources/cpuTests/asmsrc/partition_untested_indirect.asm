use16
start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    mov ax, 1
    mov bx, target
    cmp ax, 0
    jne skip_jump
    jmp bx
skip_jump:
    cmp ax, 0
    jne skip_call
    call bx
skip_call:
    mov cx, 2
    hlt
target:
    mov cx, 3
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
