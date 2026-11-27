; compile it with fasm
; If-conversion traps. Each section stores a result at ds:40h.. that a wrong value-choice rewrite
; corrupts: a second statement of an arm lost (bsf, daa, cmpxchg), a partial register write widened,
; a cross-instruction diamond merged. DS = SS = 2000h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    mov di, 40h
    mov si, inputs

; register setcc pair, converted: CL = 1, CH = 0
    mov ax, [cs:si]
    cmp ax, [cs:si+2]
    setb cl
    seta ch
    mov [di+00h], cx
; setcc into DL keeps DH
    mov dx, [cs:si+4]
    cmp dx, ax
    setz dl
    mov [di+02h], dx
; salc, converted, with CF set then clear
    cmp ax, [cs:si+2]
    db 0D6h
    mov [di+04h], al
    mov ax, [cs:si+2]
    cmp ax, [cs:si]
    db 0D6h
    mov [di+05h], al
; bsf writes CX and ZF on the taken arm, only ZF on the other
    mov bx, [cs:si+6]
    mov cx, 0FFFFh
    bsf cx, bx
    setz byte [di+06h]
    mov [di+07h], cx
    mov bx, [cs:si+8]
    mov cx, 0FFFFh
    bsf cx, bx
    setz byte [di+09h]
    mov [di+0Ah], cx
; daa adds 60h and sets the final carry on one arm, only clears it on the other
    mov al, [cs:si+10]
    add al, [cs:si+11]
    daa
    setc ah
    mov [di+0Ch], ax
    mov al, [cs:si+12]
    add al, [cs:si+13]
    daa
    setc ah
    mov [di+0Eh], ax
; cmpxchg writes ZF and a different register on each arm
    mov ax, [cs:si]
    mov bx, [cs:si]
    mov cx, [cs:si+2]
    cmpxchg bx, cx
    setz byte [di+10h]
    mov [di+11h], bx
    mov [di+13h], ax
    mov bx, [cs:si+4]
    cmpxchg bx, cx
    setz byte [di+15h]
    mov [di+16h], bx
    mov [di+18h], ax
; a cross-instruction diamond stays a branch
    mov ax, [cs:si]
    test al, 1
    jz .two
    mov cx, 1
    jmp .done
.two:
    mov cx, 2
.done:
    mov [di+1Ah], cx
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

inputs:
    dw 1111h, 2222h, 1234h, 0A00h, 0000h
    db 99h, 01h, 12h, 34h

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
