; Build: fasm idiom_memory_accessors.asm ../idiom_memory_accessors.bin
; absolute accesses lower to typed segment globals; accesses through an index read from
; memory lower to wrapping 16-bit indexers.
use16

start:
    mov byte [0x0600], 0x01
    mov word [0x0610], 0x5678
    mov ax, word [0x0610]
    mov byte [0x0601], al
    mov word [0x0620], 0x0600
    mov bx, [0x0620]
    mov byte [bx+2], 0x42
    mov ax, [0x0620]
    mov bx, ax
    mov cl, [bx+2]
    mov byte [0x0603], cl
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
