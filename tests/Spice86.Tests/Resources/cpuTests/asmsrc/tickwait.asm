; compile it with fasm
; A function that waits for a timer tick: waittick spins on the tick counter at 0:0000 until the
; IRQ 0 handler increments it, then stores 1 at 0:0001 and returns.
use16
start:
    mov ax, 0
    mov ss, ax
    mov sp, 0x100
    mov ds, ax
    ; int 8 handler: increments the tick counter
    mov word [8*4], inthandler
    mov word [8*4+2], cs
    mov byte [0], 0
    ; PIT counter 0, mode 3, ~1ms
    mov al, 00110110b
    out 43h, al
    mov al, 51h
    out 40h, al
    mov al, 22h
    out 40h, al
    ; unmask all IRQs
    mov al, 0
    out 21h, al
    sti
    call waittick
    cli
    hlt

waittick:
    mov al, [0]
waitloop:
    cmp [0], al
    je waitloop
    mov byte [1], 1
    ret

inthandler:
    inc byte [0]
    push ax
    mov al, 20h
    out 20h, al
    pop ax
    iret

rb 65520-$
    jmp start
rb 65535-$
    db 0ffh
