; compile with fasm
; 32-bit offset memory accesses on the 386 real-mode MMU: DS:10000h crosses the 64 KB
; segment limit, so both the read and the store must raise #GP, while the in-range
; control accesses run normally. DS = SS = 2000h. Results at 40h-45h (physical 20040h):
; [40h] counts the faults, [42h] keeps AX, [44h] keeps the sentinel word at DS:0.
use16

start:
    cli
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:0Dh*4], gp_handler
    mov [es:0Dh*4+2], cs
    mov word [0], 1234h
    mov ax, 5678h
fault_read:
    mov ax, [dword 10000h]
after_read:
    mov [42h], ax
fault_write:
    mov [dword 10000h], ax
after_write:
    mov ax, [dword 0]
    mov [44h], ax
    hlt

gp_handler:
    inc word [40h]
    push bp
    mov bp, sp
    add word [ss:bp+2], 6
    pop bp
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
