; compile with fasm
; A chained timer handler must restore AX before jumping to the old handler.
; DS = SS = 2000h. The result word is written at offset 0080h (physical 20080h).
use16
result = 0x0080
ticks = 0x0082
scratch = 0x0084

start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:8*4], old_handler
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

old_handler:
    push ax
    mov al, 20h
    out 20h, al
    inc word [ticks]
    pop ax
    iret

new_handler:
    push ax
    mov ax, 0BADh
    add [scratch], ax
    pop ax
    jmp old_handler

main:
    sti
wait_initial_ticks:
    cmp word [ticks], 3
    jb wait_initial_ticks
; Enter the same handler once as a function, with an iret frame.
    pushf
    push cs
    call old_handler
    cli
    xor ax, ax
    mov es, ax
    mov word [es:8*4], new_handler
    mov [es:8*4+2], cs
    mov ax, ds
    mov es, ax
    sti
    mov ax, 1234h
    mov cx, [ticks]
    add cx, 3
wait_chained_ticks:
    cmp word [ticks], cx
    jb wait_chained_ticks
    mov [result], ax
    cli
    hlt

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
