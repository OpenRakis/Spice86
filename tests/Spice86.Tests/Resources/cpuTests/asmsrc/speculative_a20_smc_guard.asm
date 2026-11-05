; compile with fasm
; A20-wrap self-modification inside a speculative run. The speculative code at
; FFFE:0014..0023 is the physical range FFFF4..100003: with the A20 gate closed its last
; bytes alias low RAM at 00000-00003. The speculative run's own store at FFFE:0014 writes
; 0000:0002, which is the instruction byte fetched at FFFE:0022. A speculative
; guard after the store must detect the byte change instead of running the stale decode.
; The selector byte at [0500h]: 0 jumps to the observed tail, 1 enters the smc run.
; Low RAM 0000-0003 is initialized by the program itself so the wrapped fetches see the
; intended bytes regardless of what the image holds past 0xFFFF.
use16

start:
    cli
    mov ax, 2000h
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov ds, ax
    mov cx, 0
    mov byte [0000h], 22h
    mov byte [0001h], 22h
    mov byte [0002h], 41h
    mov byte [0003h], 0F4h
    mov byte [0400h], 1
    jmp 0FFFEh:0000h

observed:
    mov byte [0401h], 0DDh
    hlt

    rb 0FFE0h-$
    mov al, [0500h]
    test al, al
    jnz smc_path
    jmp 0F000h:observed

    rb 0FFF0h-$
    jmp start

    rb 0FFF4h-$
smc_path:
    mov byte [0002h], 43h
    mov dx, 1111h
    mov bx, 1234h
    mov ax, 2222h
    inc cx
    hlt
