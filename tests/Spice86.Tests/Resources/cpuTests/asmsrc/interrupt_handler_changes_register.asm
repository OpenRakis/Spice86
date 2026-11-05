; compile with fasm
; A timer handler that returns a value in AX: it acknowledges the PIC, writes AX, calls a function
; that does not touch AX and returns with iret. The main code waits for that value.
; DS = SS = 2000h. The result word is written at offset 0080h (physical 20080h).
use16
result = 0x0080

start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:8*4], handler
    mov [es:8*4+2], cs
    mov ax, ds
    mov es, ax
    cld
; PIT counter 0, mode 3, about 1 ms; all IRQs unmasked
    mov al, 00110110b
    out 43h, al
    mov al, 51h
    out 40h, al
    mov al, 22h
    out 40h, al
    mov al, 0
    out 21h, al
    jmp main

handler:
    mov al, 20h
    out 20h, al
    mov ax, 5
    call keep_ax
    iret
keep_ax:
    nop
    ret

main:
    xor ax, ax
    sti
wait_value:
    cmp ax, 5
    jne wait_value
    mov [result], ax
    cli
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
