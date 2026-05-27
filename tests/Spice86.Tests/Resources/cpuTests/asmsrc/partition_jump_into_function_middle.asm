use16

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    call routine
    call middle_entry_caller
    hlt

routine:
    mov ax, 1111h

routine_middle:
    mov bx, 2222h
    ret

middle_entry_caller:
    jmp routine_middle

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh