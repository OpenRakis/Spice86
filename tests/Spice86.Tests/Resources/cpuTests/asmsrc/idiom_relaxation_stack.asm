; Build: fasm idiom_relaxation_stack.asm ../idiom_relaxation_stack.bin
; abandoned-stack-slots: nested typed stack calls like cc_calls_keep_stack_offset.
; SS:SP are set up because typed call helpers assume a real stack; results go through ES=0.
use16

start:
    mov ax, 0x2000
    mov ss, ax
    mov sp, 0x1000
    mov byte [es:0x0600], 0x01
    push 0x5678
    call callee_p
    mov byte [es:0x0601], al
    mov byte [es:0x0602], dl
    hlt
callee_p:
    push 0x1234
    call callee_q
    mov bx, sp
    mov dx, [ss:bx+2]
    ret 2
callee_q:
    push bp
    mov bp, sp
    mov ax, [bp+4]
    pop bp
    ret 2

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
