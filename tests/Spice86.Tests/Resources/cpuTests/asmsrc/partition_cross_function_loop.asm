use16

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    mov cx, 2
    call function_a
    mov cx, 2
    call function_b
    hlt

function_a:
    dec cx
    jz function_a_done
    jmp function_b

function_a_done:
    ret

function_b:
    dec cx
    jz function_b_done
    jmp function_a

function_b_done:
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh