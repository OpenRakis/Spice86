; compile it with fasm
use16

; Self-modifying rep string instruction: one address runs two variants that share the rep prefix.
; Pass 1 runs "rep stosb" and fills [0000..0003] with 0xAB.
; The code then patches the opcode byte (AA -> A4) and sets AL to 0xCD.
; Pass 2 runs "rep movsb" and copies [0000..0003] to [0004..0007].
; If pass 2 ran the old "rep stosb", [0004..0007] would be 0xCD instead of 0xAB.
;
; Expected memory: [0000..0007] = 0xAB, [0020] = 0x02 (pass counter).

start:
    xor ax,ax
    mov ss,ax
    mov sp,0100h
    mov ds,ax
    mov es,ax
    cld
    mov di,0
    mov al,0ABh
    mov byte [0020h],0
again:
    mov si,0
    mov cx,4
patched:
    rep stosb
    inc byte [0020h]
    cmp byte [0020h],2
    je done
    mov byte [cs:patched+1],0A4h
    mov al,0CDh
    jmp again
done:
    hlt

; BIOS entry point at offset FFF0
rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
