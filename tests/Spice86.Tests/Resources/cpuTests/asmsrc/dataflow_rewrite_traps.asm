; compile it with fasm
; Shapes on which a wrong data-flow rewrite prints but breaks: a dropped register clear, a
; constant inherited across a join, and condition combinations whose moved boundary is a goto target.
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
    mov word [0F00h], 2

pass_loop:
; a 32-bit write then a 16-bit write: the high word must be the cleared one
    mov eax, [cs:si+4]
    mov [bx+00h], eax
    mov [bx+80h], eax
    mov eax, 0
    mov ax, [cs:si]
    mov [bx+04h], eax

; a constant flowing through two joins: AH at the merge is not the other arm's constant
    mov dx, [cs:si+8]
    test dl, 1
    jz .other
    mov eax, 12340000h
    test dh, 1
    jz .a
    mov ax, 1
    jmp .j1
.a:
    mov ax, 2
.j1:
    mov al, 5
    jmp .j2
.other:
    mov eax, 12349905h
.j2:
    mov [bx+08h], ax
    mov [bx+0Ah], ah

; a second sequential test reached through a goto must not re-evaluate the first one
    mov ax, [cs:si+10]
    mov bp, [cs:si+12]
    mov cx, [cs:si+14]
.reloop3:
    test dh, 4
    jz .nojmp3
    jmp .second
.nojmp3:
    dec cx
    jnz .reloop3
.first:
    cmp ax, 1
    je .exit3
.second:
    cmp bp, 2
    je .exit3
    mov word [bx+0Ch], 0
    jmp .done3
.exit3:
    mov word [bx+0Ch], 1
.done3:

; a nested test combined with its outer one keeps evaluating the outer condition first
    mov ax, [cs:si+16]
    mov bp, [cs:si+18]
    test dl, 2
    jnz .inner4
    cmp ax, 1
    jne .cont4
.inner4:
    cmp bp, 2
    jne .cont4
    mov word [bx+0Eh], 1
    jmp .done4
.cont4:
    mov word [bx+0Eh], 0
.done4:

    add bx, 40h
    add si, 22
    dec word [0F00h]
    jnz pass_loop
    hlt

; per pass: T1 word + T1 dword + T2 test word + T3 ax/bp + T4 ax/bp (+pad)
inputs:
    dw 1234h, 0
    dd 89ABCDEFh
    dw 0403h
    dw 1, 3, 1
    dw 0, 2, 0
    dw 5678h, 0
    dd 11223344h
    dw 0000h
    dw 1, 3, 1
    dw 1, 1, 0

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
