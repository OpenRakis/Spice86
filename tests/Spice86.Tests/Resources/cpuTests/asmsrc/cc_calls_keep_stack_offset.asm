; compile it with fasm
; callee_p is called with one stack argument: the caller pushes 5678h and calls
; it. callee_p pushes 1234h as callee_q's argument and calls it; callee_q reads
; [bp+4] into ax, restores bp and cleans the argument with ret 2. Back in
; callee_p SP is again at its entry SP, so [ss:sp+2] is the caller's 5678h -
; the read only resolves when the call is known to advance SP by the callee's
; pop count. callee_p leaves ax from callee_q, dx its own argument and cleans
; the argument with ret 2. DS = SS = 2000h, SP = 1000h.
; Results go to offsets 40h-43h (physical 20040h-20043h).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

    push 5678h
    call callee_p
    mov [40h], ax
    mov [42h], dx

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

callee_p:
    push 1234h
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
    db 0FFh
