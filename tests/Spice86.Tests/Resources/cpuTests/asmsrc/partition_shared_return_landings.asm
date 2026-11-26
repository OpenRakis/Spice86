use16

start:
    mov ax, 0
    mov ds, ax
    mov ss, ax
    mov sp, 0100h
    xor dx, dx
    call landing_a
    call landing_b

    mov bp, trampoline_a
    call prefixed_call
    mov bp, trampoline_b
    call unprefixed_call

    mov word [ss:600h], sp
    mov word [ss:602h], dx
    hlt

prefixed_call:
    ; A segment prefix on the register CALL is ignored by the CPU, but creates
    ; a second CALL instruction whose last byte is the same as unprefixed_call.
    db 02Eh
unprefixed_call:
    call bp
shared_return:
    ret

trampoline_a:
    jmp landing_a
trampoline_b:
    jmp landing_b
landing_a:
    inc dx
    ret
landing_b:
    add dx, 10h
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
