; compile it with fasm
; Instruction idioms: xchg, push / pop between registers, cbw / cwd, les, mov AX,X / mov DS,AX, xor / and / sub of
; a register with itself, and lea as arithmetic.
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
; xchg of two registers
    mov cx, [cs:si]
    mov dx, [cs:si+2]
    xchg cx, dx
    mov [bx+00h], cx
    mov [bx+02h], dx

; push X / pop Y
    mov cx, [cs:si]
    push cx
    pop di
    mov [bx+04h], di

; cbw and cwd
    mov al, [cs:si+4]
    cbw
    mov [bx+06h], ax
    mov ax, [cs:si+4]
    cwd
    mov [bx+08h], dx
    mov [bx+0Ah], ax

; les from a far pointer
    les dx, [cs:si+6]
    mov [bx+0Ch], dx
    mov [bx+0Eh], es
    push ds
    pop es

; mov AX,X / mov DS,AX followed by a DS access
    mov ax, 2200h
    mov es, ax
    mov cx, [cs:si]
    mov [es:0004h], cx
    push ds
    pop es
    mov ax, 2200h
    mov ds, ax
    mov dx, [0004h]
    mov ax, 2000h
    mov ds, ax
    mov [bx+10h], dx

; xor, and and sub of a register with itself
    mov cx, [cs:si+2]
    xor cx, cx
    mov [bx+12h], cx
    mov dx, [cs:si+2]
    and dx, dx
    mov [bx+14h], dx
    sub dx, dx
    mov [bx+16h], dx

; lea as arithmetic
    mov di, [cs:si]
    lea dx, [di+5]
    mov [bx+18h], dx

    add bx, 40h
    add si, 0Ah
    dec word [0F00h]
    jnz pass_loop
    hlt

; per pass: w0, w1, w2, far pointer offset, far pointer segment
inputs:
    dw 1234h, 0ABCDh, 0080h, 5678h, 9ABCh
    dw 0FFFBh, 0000h, 0FF7Fh, 0001h, 0002h

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
