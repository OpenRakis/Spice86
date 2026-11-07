; compile it with fasm
use16
start:
    mov ax, 0
    mov ss, ax
    mov sp, 0x100
    mov ds, ax
    mov dx, 0
    ; int 8 handler: sets dx to 1
    mov word [8*4], inthandler
    mov word [8*4+2], cs
    ; PIT counter 0, mode 3
    mov al, 00110110b
    out 43h, al
    mov al, 51h
    out 40h, al
    mov al, 22h
    out 40h, al
    ; unmask IRQ0
    mov al, 0
    out 21h, al
    ; the program reached the wait
    mov [2], 0xAA
    ; wait for the timer tick: the pending interrupt must resume execution after the HLT
    cli
    mov dx, 0
    sti
    hlt
    mov [0], dx          ; only the wait-mode run reaches here: the handler set dx to 1
    cli
    hlt

inthandler:
    push ax
    mov dx, 1
    mov al, 20h
    out 20h, al
    pop ax
    iret

rb 65520-$
    jmp start
rb 65535-$
    db 0ffh
