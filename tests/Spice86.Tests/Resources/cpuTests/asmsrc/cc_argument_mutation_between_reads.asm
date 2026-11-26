use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    push 1111h
    call read_argument_twice
    add sp, 2
    mov [0200h], ax
    mov [0204h], sp
    hlt
read_argument_twice:
    push bp
    mov bp, sp
    mov dx, [bp+4]
    call overwrite_argument
    mov ax, [bp+4]
    mov [0202h], ax
    add ax, dx
    pop bp
    ret
overwrite_argument:
    mov bx, sp
    mov word [ss:bx+6], 9999h
    ret
rb 0FFF0h-$
    jmp start
rb 0FFFFh-$
    db 0FFh
