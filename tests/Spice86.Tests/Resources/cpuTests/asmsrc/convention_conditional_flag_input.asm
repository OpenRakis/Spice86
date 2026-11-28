; compile it with fasm
; A near routine shl ax,cl; setc dl; ret reads CF through a partial may-write:
; shl leaves CF unchanged when the masked count is 0, so the routine needs the
; caller's CF as an input even though shl writes it on the count != 0 paths.
; Four cases store DL at 2000:0200-0203: count 0 with CF=1, count 0 with CF=0,
; count 1 shifting out a 1, count 1 shifting out a 0.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h

    stc
    mov cl, 0
    call shift_fn
    mov [0200h], dl

    clc
    mov cl, 0
    call shift_fn
    mov [0201h], dl

    clc
    mov cl, 1
    mov ax, 8000h
    call shift_fn
    mov [0202h], dl

    clc
    mov cl, 1
    mov ax, 1
    call shift_fn
    mov [0203h], dl
    hlt

shift_fn:
    shl ax, cl
    setc dl
    ret

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
