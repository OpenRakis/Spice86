use16
start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    mov ax, 1
    cmp ax, 0
    jne skip_cpuid
    cpuid
skip_cpuid:
    mov cx, 2
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
