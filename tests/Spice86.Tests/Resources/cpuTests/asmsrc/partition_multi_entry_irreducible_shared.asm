use16

start:
    xor ax, ax
    mov ds, ax
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    mov dx, 0
    mov cx, 0
    call root_a
    mov cx, 1
    call root_b

    ; Record cycle_bridge as a function without entering the cycle.
    mov cx, 2
    call cycle_bridge
    mov bx, 0
    mov dx, 3
    call cycle_driver

    ; All four transfers reach one IRET handler. The push/ret adds return-target evidence.
    mov word [0200h], shared_target
    mov word [0202h], cs
    mov si, 0
    int 80h
    pushf
    push cs
    call return_caller
    pushf
    push cs
    call shared_target
    pushf
    push cs
    call jump_caller
    hlt

root_a:
    jmp shared_entry_a

root_b:
    jmp shared_entry_b

shared_entry_a:
    inc bx
    cmp cx, 0
    je shared_entry_b
    cmp dx, 0
    je shared_done
    dec dx
    jmp cycle_bridge

shared_entry_b:
    cmp cx, 1
    je shared_entry_a
    cmp dx, 0
    je shared_done
    dec dx
    jmp cycle_bridge

shared_done:
    ret

cycle_bridge:
    cmp dx, 3
    je shared_entry_a
    jmp shared_entry_b

cycle_driver:
    jmp cycle_bridge

return_caller:
    push shared_target
    ret

jump_caller:
    jmp shared_target

shared_target:
    inc si
    iret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
