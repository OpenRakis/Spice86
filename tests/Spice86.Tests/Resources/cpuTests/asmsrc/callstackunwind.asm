; Build: fasm callstackunwind.asm ../callstackunwind.bin
;
; The observed path returns leaf -> recovery -> inner_return. The unobserved
; path discards leaf's immediate return and returns directly to inner_return.
; The generated call waiting for inner_return must consume that unwind even
; while it is dispatching the recovery override.
use16

start:
    mov ax, 0
    mov ds, ax
    mov ss, ax
    mov sp, 0100h
    call outer

interpreter_return:
    mov byte [0010h], 0AAh
    hlt

rb 0100h-$

outer:
    call first

inner_return:
    mov byte [0011h], 0BBh
    ret

rb 0200h-$

first:
    pop bx
    push bx
    mov ax, recovery
    push ax
    ret

rb 0300h-$

recovery:
    call leaf

recovery_return:
    ret

rb 0400h-$

leaf:
    mov al, byte [0500h]
    test al, al
    jnz unwind_to_inner_return
    mov byte [0012h], 0DDh
    ret

unwind_to_inner_return:
    mov byte [0012h], 0EEh
    pop ax
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
