use16
start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    xor dx, dx
    mov eax, 1
    cmp eax, 80000000h
    jl below1
    add dx, 1
below1:
    mov eax, 80000000h
    cmp eax, 80000000h
    jle below2
    add dx, 10h
below2:
    xor ax, ax
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    mov word [ss:60Ah], dx
    mov word [ss:604h], ax
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
