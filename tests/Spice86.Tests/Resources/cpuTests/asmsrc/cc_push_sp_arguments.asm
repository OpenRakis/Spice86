use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 0FFFEh
    push sp
    push sp
    call sum_arguments
    add sp, 4
    mov [0200h], ax
    mov [0204h], sp
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
