; compile it with fasm
; Chains of constant shifts on one register: same-operation chains fold, mixed chains and live
; flags between the shifts do not. DS = SS = 2000h. Pass p writes its results at offset 40h * p
; (physical 20000h + 40h * p). cmp di,di writes every status flag so no flag survives a section.
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
    mov word [0F00h], 2

pass_loop:
; shr by 1 three times
    mov dx, [cs:si]
    shr dx, 1
    shr dx, 1
    shr dx, 1
    mov [bx+00h], dx
    cmp di, di

; shl counts summing to the width
    mov dx, [cs:si]
    shl dx, 7
    shl dx, 9
    mov [bx+02h], dx
    cmp di, di

; 32-bit shr to zero
    mov edx, [cs:si]
    shr edx, 16
    shr edx, 16
    mov dword [bx+04h], edx
    cmp di, di

; 32-bit shl past the width
    mov edx, [cs:si]
    shl edx, 17
    shl edx, 16
    mov dword [bx+08h], edx
    cmp di, di

; sar counts saturating at width - 1
    mov dx, [cs:si]
    sar dx, 8
    sar dx, 24
    mov [bx+0Ch], dx
    cmp di, di

    mov edx, [cs:si]
    sar edx, 16
    sar edx, 17
    mov dword [bx+0Eh], edx
    cmp di, di

; shr count above 31 masked by the CPU
    mov dx, [cs:si]
    shr dx, 33
    shr dx, 1
    mov [bx+12h], dx
    cmp di, di

; mixed shl then shr does not combine
    mov dx, [cs:si]
    shl dx, 1
    shr dx, 1
    mov [bx+14h], dx
    cmp di, di

; mixed sar then shr does not combine
    mov dx, [cs:si]
    sar dx, 1
    shr dx, 1
    mov [bx+16h], dx
    cmp di, di

; different operand widths do not combine
    mov edx, [cs:si]
    shr edx, 8
    shr dx, 8
    mov dword [bx+18h], edx
    cmp di, di

; two separate shr statements
    mov dx, [cs:si]
    shr dx, 1
    mov [bx+1Ch], dx
    shr dx, 1
    mov [bx+1Eh], dx
    cmp di, di

; live carry between the shifts does not combine
    mov dx, [cs:si]
    shl dx, 1
    setc byte [bx+22h]
    shr dx, 1
    mov [bx+20h], dx
    cmp di, di

; shr count of exactly the promotion mask width
    mov dx, [cs:si]
    shr dx, 32
    shr dx, 1
    mov [bx+24h], dx
    cmp di, di

; 8-bit shr and sar chains
    mov dl, [cs:si]
    shr dl, 4
    shr dl, 4
    mov [bx+26h], dl
    cmp di, di

    mov dl, [cs:si]
    sar dl, 4
    sar dl, 4
    mov [bx+27h], dl
    cmp di, di

; variable count first does not combine
    mov dx, [cs:si]
    mov cl, [cs:si+4]
    shr dx, cl
    shr dx, 1
    mov [bx+28h], dx
    cmp di, di

    add bx, 40h
    add si, 5
    dec word [0F00h]
    jnz pass_loop
    hlt

; per pass: v (32 bits), CL count (8 bits)
inputs:
    dd 80018001h
    db 1
    dd 12347FFFh
    db 2

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
