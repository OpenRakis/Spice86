use16
start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    xor dx, dx
    mov ax, 1
    cmp ax, 1
    je next
next:
    add dx, 1
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    mov word [ss:60Ah], dx
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
