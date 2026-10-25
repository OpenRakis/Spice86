; compile it with fasm
; 32-bit arithmetic on the DX:AX register pair: add / adc, sub / sbb, neg / adc / neg, shl / rcl and shr / rcr,
; with carries and borrows across the two halves.
; DS = SS = 2000h. Pass p writes its results at offset 40h * p (physical 20000h + 40h * p).
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
; add / adc
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov cx, [cs:si+4]
    mov di, [cs:si+6]
    add ax, cx
    adc dx, di
    mov [bx+00h], ax
    mov [bx+02h], dx

; sub / sbb
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    sub ax, cx
    sbb dx, di
    mov [bx+04h], ax
    mov [bx+06h], dx

; neg / adc / neg
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    neg ax
    adc dx, 0
    neg dx
    mov [bx+08h], ax
    mov [bx+0Ah], dx

; shl / rcl
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    shl ax, 1
    rcl dx, 1
    mov [bx+0Ch], ax
    mov [bx+0Eh], dx

; shr / rcr
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    shr dx, 1
    rcr ax, 1
    mov [bx+10h], ax
    mov [bx+12h], dx

    add bx, 40h
    add si, 8
    dec word [0F00h]
    jnz pass_loop
    hlt

; per pass: X, Y (32 bits each)
inputs:
    dd 0000FFFFh, 00000001h
    dd 00010000h, 00000001h
    dd 80000000h, 0FFFFFFFFh
    dd 12348000h, 0000FFFFh

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
