use16

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    call function_a
    call function_b
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