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
join:
    loop next_case
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    mov word [ss:60Ah], dx
    mov word [ss:608h], cx
    mov word [ss:606h], bx
    hlt
table:
    dw case_zero, case_one, join

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
