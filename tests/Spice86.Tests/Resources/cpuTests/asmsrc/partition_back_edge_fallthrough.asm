use16
start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    xor dx, dx
    mov cx, 3
    jmp header
latch:
    dec cx
    jz done
header:
    add dx, 1
    jmp latch
done:
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    mov word [ss:60Ah], dx
    mov word [ss:608h], cx
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
