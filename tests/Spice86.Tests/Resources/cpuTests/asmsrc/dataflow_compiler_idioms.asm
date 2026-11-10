; compile it with fasm
; Compiler idiom traps: pure algebra and mask folding, partial-register composition and the XOR
; swap. A wrong simplification or a dropped observer shows up as a wrong result word. DS = SS =
; 2000h. Pass p reads the input pair (x, 5678h) at [cs:si] and writes its results at offset
; 40h * p (physical 20000h + 40h * p). Each independent section ends with `cmp di,di` to kill all
; status flags.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:63h*4], observe_dx_iret
    mov [es:63h*4+2], cs
    cld
    xor bx, bx
    xor di, di
    mov si, inputs
    mov bp, 4

pass_loop:
; S0: two AND masks combine
    mov ax, [cs:si]
    and ax, 0FFh
    and ax, 0FFFh
    mov [bx+00h], ax
    cmp di, di

; S1: two OR masks combine
    mov ax, [cs:si]
    or ax, 0F0h
    or ax, 0Fh
    mov [bx+02h], ax
    cmp di, di

; S2: two XOR constants cancel
    mov ax, [cs:si]
    xor ax, 0AA55h
    xor ax, 0AA55h
    mov [bx+04h], ax
    cmp di, di

; S3: double NOT cancels
    mov ax, [cs:si]
    not ax
    not ax
    mov [bx+06h], ax
    cmp di, di

; S4: mixed shift keeps the low 12 bits
    mov ax, [cs:si]
    shl ax, 4
    shr ax, 4
    mov [bx+08h], ax
    cmp di, di

; S5: low then high byte writes compose a word
    mov ax, [cs:si]
    mov dl, al
    mov dh, 0
    mov [bx+0Ah], dx
    cmp di, di

; S6: high then low byte writes compose a word
    mov ax, [cs:si]
    mov ah, al
    mov al, 0
    mov [bx+0Ch], ax
    cmp di, di

; S7: a high-byte read composes as a shift
    mov ax, [cs:si]
    mov dl, ah
    mov dh, 0
    mov [bx+0Eh], dx
    cmp di, di

; S8: XOR swap of AX and DX, with DX loaded so it cannot be folded away
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    xor ax, dx
    xor dx, ax
    xor ax, dx
    mov [bx+10h], ax
    mov [bx+12h], dx
    cmp di, di

; T1: a store between the byte writes observes the first half, so no composition
    mov dx, 0ABCDh
    mov ax, [cs:si]
    mov dl, al
    mov [bx+14h], dx
    mov dh, 0
    mov [bx+16h], dx
    cmp di, di

; T2: a store between the XORs observes the first swap step, so no swap
    mov ax, [cs:si]
    mov dx, 5678h
    xor ax, dx
    mov [bx+18h], ax
    xor dx, ax
    xor ax, dx
    mov [bx+1Ah], ax
    cmp di, di

; T3: an interrupt between the byte writes observes the first half, so no composition
    mov dx, 0ABCDh
    mov ax, [cs:si]
    mov dl, al
    int 63h
    mov dh, 0
    mov [bx+24h], dx
    cmp di, di

; T4: a live carry read after the first XOR keeps its flag writes, so no swap; the flags after
; the full swap still clear CF
    mov ax, [cs:si]
    mov dx, 5678h
    xor ax, dx
    setc cl
    mov [bx+1Ch], cl
    xor dx, ax
    xor ax, dx
    pushf
    pop cx
    and cx, 1
    mov [bx+1Eh], cx
    cmp di, di

; T5: alternating byte writes of one register must not merge into overlapping compositions
    mov cx, [cs:si]
    mov al, cl
    mov ah, 7
    mov al, 34h
    mov ah, ch
    mov [bx+26h], ax
    cmp di, di

; S9: six XORs swap twice, back to the inputs - not one swap
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    xor ax, dx
    xor dx, ax
    xor ax, dx
    xor dx, ax
    xor ax, dx
    xor dx, ax
    mov [bx+28h], ax
    mov [bx+2Ah], dx
    cmp di, di

; T6: the same triple on a constant DX stays correct even without a tuple swap
    mov ax, [cs:si]
    mov dx, 5678h
    xor ax, dx
    xor dx, ax
    xor ax, dx
    mov [bx+2Ch], ax
    mov [bx+2Eh], dx
    cmp di, di

    add bx, 40h
    add si, 4
    dec bp
    jnz pass_loop
    hlt

observe_dx_iret:
    mov [bx+22h], dx
    iret

inputs:
    dw 0000h, 5678h
    dw 0FFFFh, 5678h
    dw 8001h, 5678h
    dw 1234h, 5678h

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
