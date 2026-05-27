use16

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    mov cx, 0
    call root_a
    mov cx, 1
    call root_b
    hlt

root_a:
    jmp shared_entry_a

root_b:
    jmp shared_entry_b

shared_entry_a:
    cmp cx, 0
    je shared_entry_b
    ret

shared_entry_b:
    cmp cx, 1
    je shared_entry_a
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh