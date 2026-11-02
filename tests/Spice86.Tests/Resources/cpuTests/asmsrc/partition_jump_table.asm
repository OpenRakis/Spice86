use16

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    xor dx, dx
    mov cx, 3

next_case:
    mov bx, cx
    dec bx
    shl bx, 1
    jmp word [cs:table+bx]

case_zero:
    add dx, 1
    jmp join

case_one:
    add dx, 10h
    jmp join

case_two:
    add dx, 100h

join:
    loop next_case
    hlt

table:
    dw case_zero, case_one, case_two

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
