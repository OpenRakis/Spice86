; compile it with fasm
; An IRQ0 handler is installed in the IVT but the timer never fires before the program halts
; (no PIT setup), so discovery never sees the handler bytes. The generated code must keep the
; conservative check effect: the BX constant held across the loop must not fold past the loop-head
; event check (the check may deliver a handler that clobbers BX). The oracle still sees 42h in the
; result region either way; the difference is golden-only. DS = SS = 2000h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:8*4], unseen_handler
    mov [es:8*4+2], cs
    mov ax, ds
    mov es, ax
    sti
; BX carries a constant across every loop-head event check; each iteration stores it.
    mov bx, 42h
    mov cx, 4
    mov di, 40h
fill_loop:
    mov [di], bx
    add di, 2
    loop fill_loop
; one more store after the loop with BX still live across the loop's last check
    mov [di], bx
    hlt

; never reached: no PIT tick is ever delivered before the hlt
unseen_handler:
    mov dx, 5AA5h
    iret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
