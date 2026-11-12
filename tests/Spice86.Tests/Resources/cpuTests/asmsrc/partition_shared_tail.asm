use16

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    call function_a
    call function_b
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    mov word [ss:604h], ax
    mov word [ss:606h], bx
    mov word [ss:608h], cx
    hlt

function_a:
    mov ax, 1111h
    jmp shared_tail

function_b:
    mov bx, 2222h
    jmp shared_tail

shared_tail:
    mov cx, 3333h
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh