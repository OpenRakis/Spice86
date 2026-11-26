; compile with fasm
; E4: the program waits long enough for the IRQ 12 the machine raises before the
; run, then halts. The variant flag at 0x0000:0x0500, when nonzero, first registers
; user_routine through INT 33h AX=000Ch; the routine stores 0CAFEh at 2000h:0080h.
; With the flag clear nothing calls it and the word stays 0. DS = SS = 2000h.
use16
result = 0x0080

start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    cmp byte [es:0500h], 0
    je no_routine
    mov ax, cs
    mov es, ax
    mov dx, user_routine
    mov cx, 1
    mov ax, 000Ch
    int 33h
no_routine:
    sti
    mov cx, 0400h
spin:
    loop spin
    cli
    hlt

user_routine:
    push ds
    push ax
    mov ax, 2000h
    mov ds, ax
    mov word [result], 0CAFEh
    pop ax
    pop ds
    retf

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
