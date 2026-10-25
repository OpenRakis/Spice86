use16
start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    xor dx, dx
    mov cx, 2
far_again:
    mov bx, cx
    dec bx
    shl bx, 2
    call dword [cs:far_table+bx]
    loop far_again
    mov cx, 2
jump_again:
    mov bx, cx
    dec bx
    shl bx, 2
    jmp dword [cs:jump_table+bx]
back:
    loop jump_again
    hlt
far_a:
    add dx, 1
    retf
far_b:
    add dx, 10h
    retf
jump_a:
    add dx, 100h
    jmp back
jump_b:
    add dx, 1000h
    jmp back
far_table:
    dw far_a, 0F000h, far_b, 0F000h
jump_table:
    dw jump_a, 0F000h, jump_b, 0F000h

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
