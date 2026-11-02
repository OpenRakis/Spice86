use16
start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    xor ax, ax
    mov ds, ax
    mov word [0], first_handler
    mov word [2], cs
    xor si, si
    xor bx, bx
divide:
    mov ax, 10
    xor dx, dx
    div bx
    hlt
first_handler:
    add si, 1
    mov word [0], second_handler
    push bp
    mov bp, sp
    mov word [bp+2], divide
    pop bp
    iret
second_handler:
    add si, 10h
    mov bx, 5
    push bp
    mov bp, sp
    mov word [bp+2], divide
    pop bp
    iret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
