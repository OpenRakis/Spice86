; Build: fasm idiom_relaxation_segment.asm ../idiom_relaxation_segment.bin
; accesses-stay-in-segment: an indexed access whose index is read through CS, so its range
; is not proved and strict mode must handle the segment-wrap case.
use16

start:
    mov byte [0x0600], 0x01
    mov si, input
    mov bx, [cs:si]
    mov byte [bx], 0x42
    mov al, [bx]
    mov byte [0x0601], al
    hlt
input: dw 0x0610

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
