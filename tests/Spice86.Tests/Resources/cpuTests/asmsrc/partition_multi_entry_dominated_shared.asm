use16

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    mov al, 0
    call root_a
    mov al, 1
    call root_a
    mov al, 0
    call root_b
    mov al, 1
    call root_b
    hlt

root_a:
    cmp al, 0
    je shared_entry_a
    jmp shared_entry_b

root_b:
    cmp al, 0
    je shared_entry_a
    jmp shared_entry_b

shared_entry_a:
    mov bx, 0A111h
    jmp shared_join

shared_entry_b:
    mov bx, 0B222h
    jmp shared_join

shared_join:
    mov cx, 0C333h
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh