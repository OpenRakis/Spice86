use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    mov bp, sp
    mov word [ss:bp-2], 1111h
    push 2222h
    push word [ss:bp-2]
    call sum_arguments
    add sp, 4
    mov [0200h], ax
    mov [0202h], sp
    hlt
sum_arguments:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    add ax, [bp+6]
    pop bp
    ret
rb 0FFF0h-$
    jmp start
rb 0FFFFh-$
    db 0FFh
