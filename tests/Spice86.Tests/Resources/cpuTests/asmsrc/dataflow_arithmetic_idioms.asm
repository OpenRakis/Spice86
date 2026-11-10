; compile it with fasm
; Compiler arithmetic idioms: flag-to-value materialization, signed power-of-two division and
; modulo, reciprocal multiply and scalar constant multiplication. A wrong simplification or a
; dropped observer shows up as a wrong result word. DS = SS = 2000h. Pass p reads the input x at
; [cs:si] and writes its results at offset 80h * p (physical 20000h + 80h * p). Each independent
; section ends with `cmp di,di` to kill all status flags.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:64h*4], observe_ax_iret
    mov [es:64h*4+2], cs
    cld
    xor bx, bx
    xor di, di
    mov si, inputs
    mov bp, 8

pass_loop:
; A0: cmp/sbb materializes the carry mask
    mov ax, [cs:si]
    cmp ax, 7
    sbb dx, dx
    mov [bx+00h], dx        ;; x < 7 ? FFFF : 0
    cmp di, di

; A1: neg/sbb/inc materializes x == 0
    mov ax, [cs:si]
    neg ax
    sbb ax, ax
    inc ax
    mov [bx+02h], ax        ;; x == 0 ? 1 : 0
    cmp di, di

; A2: neg/sbb/neg materializes x != 0
    mov ax, [cs:si]
    neg ax
    sbb ax, ax
    neg ax
    mov [bx+04h], ax        ;; x != 0 ? 1 : 0
    cmp di, di

; A3: cwd/sub/sar is signed /2
    mov ax, [cs:si]
    cwd
    sub ax, dx
    sar ax, 1
    mov [bx+06h], ax        ;; bits((short)x / 2)
    cmp di, di

; A4: cwd/and/add/sar is signed /4
    mov ax, [cs:si]
    cwd
    and dx, 3
    add ax, dx
    sar ax, 2
    mov [bx+08h], ax        ;; bits((short)x / 4)
    cmp di, di

; A5: the same form with shift 3 is signed /8
    mov ax, [cs:si]
    cwd
    and dx, 7
    add ax, dx
    sar ax, 3
    mov [bx+0Ah], ax        ;; bits((short)x / 8)
    cmp di, di

; A6: reciprocal multiply 0xCCCD >> 3 is x / 10
    mov ax, [cs:si]
    mov dx, 0CCCDh
    mul dx
    shr dx, 3
    mov [bx+0Ch], dx        ;; x / 10
    cmp di, di

; A7: the signed modulo-by-power-of-two idiom, mask 3 -> x % 4
    mov ax, [cs:si]
    cwd
    xor ax, dx
    sub ax, dx
    and ax, 3
    xor ax, dx
    sub ax, dx
    mov [bx+0Eh], ax        ;; bits((short)x % 4)
    cmp di, di

; A8: shl/add chain through a saved copy is x * 10
    mov ax, [cs:si]
    mov cx, ax
    shl ax, 1
    shl ax, 1
    add ax, cx
    shl ax, 1
    mov [bx+10h], ax        ;; bits(x * 10)
    cmp di, di

; A9: four shifts feeding an arithmetic reader are x * 16 + 3
    mov ax, [cs:si]
    shl ax, 1
    shl ax, 1
    shl ax, 1
    shl ax, 1
    add ax, 3
    mov [bx+12h], ax        ;; bits(x * 16 + 3)
    cmp di, di

; T1: the /2 idiom but DX stays live: the scratch write must survive, only the
; final sar may fold - DX keeps the sign mask
    mov ax, [cs:si]
    cwd
    sub ax, dx
    sar ax, 1
    mov [bx+14h], ax        ;; bits((short)x / 2)
    mov [bx+16h], dx        ;; x >= 8000 ? FFFF : 0
    cmp di, di

; T2: a flag observer between the bias and the shift: the partial sequence
; must not fold across it, and CF after the final sar stays observable
    mov ax, [cs:si]
    cwd
    sub ax, dx
    sar ax, 1
    pushf
    pop cx
    and cx, 1
    mov [bx+18h], ax        ;; bits((short)x / 2)
    mov [bx+1Ah], cx        ;; carry of the sar
    cmp di, di

; T3: a store between the shift chain's operations keeps both values observable
    mov ax, [cs:si]
    mov cx, ax
    shl ax, 1
    mov [bx+1Ch], ax        ;; bits(x * 2)
    shl ax, 1
    add ax, cx
    shl ax, 1
    mov [bx+1Eh], ax        ;; bits(x * 10)
    cmp di, di

; T4: an interrupt between neg and sbb observes negated AX, so the flag
; materialization must not fold across it
    mov ax, [cs:si]
    neg ax
    int 64h
    sbb ax, ax
    inc ax
    mov [bx+22h], ax        ;; x == 0 ? 1 : 0
    cmp di, di

; B1: reciprocal multiply 0xAAAB >> 1 is x / 3
    mov ax, [cs:si]
    mov dx, 0AAABh
    mul dx
    shr dx, 1
    mov [bx+24h], dx        ;; x / 3
    mov ax, 0               ;; kill the dead low product before the exit observer
    cmp di, di

; T5: a partial write to DL after the SAR leaves DH still holding the sign:
; the division sequence must not consume DX's cwd value
    mov ax, [cs:si]
    cwd
    sub ax, dx
    sar ax, 1
    mov dl, 7
    mov [bx+26h], ax        ;; bits((short)x / 2)
    mov [bx+28h], dx        ;; x >= 8000h ? FF07 : 0007
    mov dx, 0               ;; clobber scratch
    cmp di, di

    add bx, 80h
    add si, 2
    dec bp
    jnz pass_loop
    hlt

observe_ax_iret:
    mov [bx+20h], ax
    iret

align 16
inputs:
    dw 0000h, 0001h, 0FFFFh, 8000h, 7FFFh, 8001h, 1234h, 0FFF9h

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
