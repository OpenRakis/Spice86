use16

start:
    mov ax, 1000h
    mov ss, ax
    mov sp, 0100h
    xor dx, dx

    ; Direct calls make each landing its own function entry.
    call landing_a
    call landing_b

    ; The same indirect CALL instruction sees two target partitions, each
    ; tail-jumping to a different entry while its return frame is open.
    mov bp, trampoline_a
    call dispatcher
    mov bp, trampoline_b
    call dispatcher

    mov bx, jump_target
    jmp bx

dispatcher:
    call bp
    ret

trampoline_a:
    jmp landing_a

trampoline_b:
    jmp landing_b

landing_a:
    mov ax, 1111h
    inc dx
    ret

landing_b:
    add dx, 10h
    ret

jump_target:
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    mov word [ss:604h], ax
    mov word [ss:606h], bx
    mov word [ss:608h], dx
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
