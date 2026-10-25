use16
start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    xor dx, dx
    mov ax, 5
    cmp ax, 3
    jns pos1
    add dx, 1
pos1:
    mov ax, 2
    cmp ax, 3
    jns pos2
    add dx, 10h
pos2:
    mov ax, 8000h
    test ax, ax
    jns pos3
    add dx, 100h
pos3:
    mov ax, 1
    test ax, ax
    jns pos4
    add dx, 1000h
pos4:
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
