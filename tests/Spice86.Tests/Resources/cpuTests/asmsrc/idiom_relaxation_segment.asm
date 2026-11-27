; Build: fasm idiom_relaxation_segment.asm ../idiom_relaxation_segment.bin
; accesses-stay-in-segment: adjacent indexed word stores whose index range is not proved.
; DS is zero, so every access is ordinary RAM; only the segment-end wrap proof is missing.
use16

start:
    mov byte [0x0600], 0x01
    xor ax, ax
    mov ds, ax
    mov si, [cs:input]
    mov word [si+1], 0x1234
    mov word [si+3], 0x5678
    mov ax, [si+1]
    mov [0x0601], ax
    mov dx, [si+3]
    mov [0x0603], dx
    hlt
input: dw 0x060F

    rb 65520-$
    jmp start
    rb 65535-$
    db 0ffh
