; compile it with fasm
; SS = DS = 1000h, SP = 0100h. A callee g does mov bp,sp and then writes a frame
; slot through bp ([bp-2] lands below the pushed return), so its BP web's only
; read is a frame-slot access the frame plan replaces, and g's BP output is read
; only by its typed return. Dropping mov bp,sp makes the return read stale BP:
; the caller stores BP after the call and a caller BP of 0 exposes the drop.
; Catches: a dropped frame web whose value a typed return serves.
use16

start:
    mov ax, 1000h
    mov ds, ax
    mov ss, ax
    mov sp, 0100h
    xor bp, bp
    call g
    mov [600h], bp
    mov [602h], sp
    hlt

g:
    mov bp, sp
    mov word [bp-2], 5
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
