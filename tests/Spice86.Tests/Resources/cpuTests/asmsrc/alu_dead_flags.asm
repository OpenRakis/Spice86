; compile it with fasm
; ALU operations whose flags are dead: each result is stored, then cmp DI,DI writes every status flag before any
; flag is read. The last cases keep exactly one flag live with a jump on it.
; DS = SS = 2000h. Pass p writes its results at offset 80h * p (physical 20000h + 80h * p).
; Jump cases write 1 when the jump is taken and 2 when it is not.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 1000h
    cld
    xor bx, bx
    mov si, inputs
    mov word [0F00h], 4

pass_loop:
; shifts by 1
    mov dx, [cs:si]
    shl dx, 1
    mov [bx+00h], dx
    cmp di, di
    mov dx, [cs:si]
    shr dx, 1
    mov [bx+02h], dx
    cmp di, di
    mov dx, [cs:si]
    sar dx, 1
    mov [bx+04h], dx
    cmp di, di

; shifts by a constant above 1, at the width and above the width
    mov dx, [cs:si]
    shl dx, 4
    mov [bx+06h], dx
    cmp di, di
    mov dx, [cs:si]
    shr dx, 16
    mov [bx+08h], dx
    cmp di, di
    mov dx, [cs:si]
    sar dx, 17
    mov [bx+0Ah], dx
    cmp di, di

; a shift by CL (0, 1, 33 masked to 1, and 4)
    mov cl, [cs:si+2]
    mov dx, [cs:si]
    shr dx, cl
    mov [bx+0Ch], dx
    cmp di, di

; two shifts in a row
    mov dx, [cs:si]
    shr dx, 1
    shr dx, 1
    mov [bx+0Eh], dx
    cmp di, di

; rotates by 0, by the width and by more than the width
    mov dx, [cs:si]
    rol dx, 0
    mov [bx+10h], dx
    cmp di, di
    mov dx, [cs:si]
    rol dx, 16
    mov [bx+12h], dx
    cmp di, di
    mov dx, [cs:si]
    ror dx, 20
    mov [bx+14h], dx
    cmp di, di

; rotates through the carry, and adc / sbb, with the carry in from the input
    mov ax, [cs:si+4]
    shr ax, 1
    mov dx, [cs:si]
    rcl dx, 1
    mov [bx+16h], dx
    cmp di, di
    mov ax, [cs:si+4]
    shr ax, 1
    mov dx, [cs:si]
    rcr dx, 1
    mov [bx+18h], dx
    cmp di, di
    mov ax, [cs:si+4]
    shr ax, 1
    mov dx, [cs:si]
    adc dx, 5
    mov [bx+1Ah], dx
    cmp di, di
    mov ax, [cs:si+4]
    shr ax, 1
    mov dx, [cs:si]
    sbb dx, 5
    mov [bx+1Ch], dx
    cmp di, di

; mul and imul (one operand), imul with an immediate
    mov ax, [cs:si]
    mov cx, 3
    mul cx
    mov [bx+1Eh], ax
    mov [bx+20h], dx
    cmp di, di
    mov ax, [cs:si]
    mov cx, -3
    imul cx
    mov [bx+22h], ax
    mov [bx+24h], dx
    cmp di, di
    imul dx, word [cs:si], 7
    mov [bx+26h], dx
    cmp di, di

; neg
    mov dx, [cs:si]
    neg dx
    mov [bx+28h], dx
    cmp di, di

; 8-bit shifts
    mov dl, [cs:si]
    shl dl, 1
    mov [bx+2Ah], dl
    cmp di, di
    mov dl, [cs:si]
    sar dl, 7
    mov [bx+2Bh], dl
    cmp di, di

; 32-bit shifts
    mov edx, [cs:si+6]
    shr edx, 3
    mov [bx+2Ch], edx
    cmp di, di
    mov edx, [cs:si+6]
    sar edx, 31
    mov [bx+30h], edx
    cmp di, di

; plain flag writes, some overwritten before the carry is read
    mov dx, [cs:si]
    stc
    clc
    adc dx, 0
    mov [bx+34h], dx
    cmp di, di
    mov dx, [cs:si]
    clc
    cmc
    adc dx, 0
    mov [bx+36h], dx
    cmp di, di
    mov dx, [cs:si]
    mov ah, 1
    sahf
    adc dx, 0
    mov [bx+38h], dx
    cmp di, di
    mov dx, [cs:si]
    mov ah, 1
    sahf
    clc
    adc dx, 0
    mov [bx+3Ah], dx
    cmp di, di

; rcl by 17: the count reduced modulo 17 is 0
    mov ax, [cs:si+4]
    shr ax, 1
    mov dx, [cs:si]
    rcl dx, 17
    mov [bx+3Ch], dx
    cmp di, di

; one live flag
    mov byte [bx+3Eh], 1
    mov dx, [cs:si]
    shr dx, 1
    jc shr_carry_end
    mov byte [bx+3Eh], 2
shr_carry_end:
    mov byte [bx+3Fh], 1
    mov dx, [cs:si]
    shl dx, 1
    jc shl_carry_end
    mov byte [bx+3Fh], 2
shl_carry_end:
    mov byte [bx+40h], 1
    mov dx, [cs:si]
    neg dx
    jc neg_carry_end
    mov byte [bx+40h], 2
neg_carry_end:
    mov byte [bx+41h], 1
    mov dx, [cs:si]
    sar dx, 1
    js sar_sign_end
    mov byte [bx+41h], 2
sar_sign_end:
    mov byte [bx+42h], 1
    mov dx, [cs:si]
    shr dx, 4
    jz shr_zero_end
    mov byte [bx+42h], 2
shr_zero_end:
    cmp di, di

    add bx, 80h
    add si, 0Ah
    dec word [0F00h]
    jnz pass_loop
    hlt

; per pass: v, CL count, carry in (bit 0), w (32 bits)
inputs:
    dw 8000h, 0000h, 0001h
    dd 80000001h
    dw 0001h, 0001h, 0000h
    dd 12345678h
    dw 0000h, 0021h, 0001h
    dd 0FFFFFFFFh
    dw 7FFFh, 0004h, 0000h
    dd 00000000h

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
