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
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
