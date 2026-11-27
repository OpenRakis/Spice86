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
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    mov word [ss:608h], cx
    hlt
target:
    mov cx, 3
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
