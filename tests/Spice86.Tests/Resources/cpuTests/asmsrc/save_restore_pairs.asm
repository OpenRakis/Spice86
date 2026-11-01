; compile it with fasm
; Save / restore pair removal traps. Each section stores at ds:40h.. the value a wrong rewrite
; corrupts: removing the push leaves SP two bytes higher everywhere in the region and leaves the
; word below SP unwritten, so a read of the shifted stack or of the abandoned word observes the
; difference. DS = SS = 2000h, SP = 1000h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    mov di, 40h
    mov si, inputs

; P1: a qualifying pair — nothing between but a register move, and push bx lands on the
; abandoned word, so pop ax restores 1111h from the saved local.
    mov ax, 1111h
    push ax
    mov cx, 2222h
    pop ax
    push bx
    pop bx
    mov [di+00h], ax

; P2: a write through [bp-2] aliases the pushed word between the pair — pop ax must observe 99h,
; not the saved 1111h.
    mov bp, sp
    mov ax, 2222h
    push ax
    mov word [bp-2], 99h
    pop ax
    push bx
    pop bx
    mov [di+02h], ax

; P3: the jz skips the pop on the taken path, so pop ax cannot close the region; pop bx below
; steals the pushed 3333h, and ax keeps the overwritten 5555h.
    mov ax, 3333h
    push ax
    mov ax, 5555h
    mov cx, [cs:si]
    test cx, cx
    jz .p3skip
    pop ax
.p3skip:
    pop bx
    mov [di+04h], bx
    mov [di+06h], ax

; P4: the call in between pushes a return address; reader4 reads the pushed word at [bp+2] —
; with the push removed it would read the return address instead of 4444h.
    mov ax, 4444h
    push ax
    call reader4
    pop ax
    push bx
    pop bx

; P5: mov bx,sp between the pair observes the shifted stack pointer 0FFEh; without the push it
; would read 1000h.
    mov ax, 5555h
    push ax
    mov bx, sp
    pop ax
    push cx
    pop cx
    mov [di+0Ah], bx

; P6: mov cx,[0FFEh] between the pair reads the pushed 6666h — DS = SS, so the access may alias
; the slot without reading SS.
    mov ax, 6666h
    push ax
    mov cx, [0FFEh]
    pop ax
    push bx
    pop bx
    mov [di+0Ch], cx

; P7: after the pop, mov dx,[bp-2] reads the abandoned word — still 7777h only while the push
; really ran.
    mov bp, sp
    mov ax, 7777h
    push ax
    mov cx, ax
    pop ax
    mov dx, [bp-2]
    mov [di+0Eh], dx

; Q1: push ax; pop bx qualify — nothing between but the instruction comments, so the pop reads
; the pushed expression directly; push cx lands on the abandoned word.
    mov ax, 8888h
    push ax
    pop bx
    push cx
    pop cx
    mov [di+10h], bx

; Q2: push es; popf; les di,[bx+si-46] reads the abandoned word through DS = SS (the datatrnf
; shape) — the pair must keep its real push and pop so di reads 1111h.
    mov ax, 1111h
    mov es, ax
    push es
    popf
    mov bx, 1000h
    mov si, 2Ch
    les di, [bx+si-46]
    mov bp, di
    mov di, 40h
    mov [di+12h], bp

; Q3: pushf; pop ax qualifies — flags hold 1111h from the popf above.
    pushf
    pop ax
    push bx
    pop bx
    mov [di+14h], ax

; Q4: a popf that sets IF must keep the flags semantics — pushf reads back what popf set.
    mov ax, 0200h
    push ax
    popf
    pushf
    pop cx
    mov [di+16h], cx

; Q5: mov bx,sp between the pair observes the shifted stack pointer 0FFEh — the pair stays.
    mov ax, 9999h
    push ax
    mov bx, sp
    pop cx
    push dx
    pop dx
    mov [di+18h], bx

; T1: BX holds the address of the future slot before the push; flag propagation moves the
; cmp's [ss:bx] read into the je's condition, so it observes the word push si just wrote.
; With the push removed the read sees the stale slot and the else arm runs.
    mov bx, sp
    sub bx, 2
    mov si, 1234h
    push si
    cmp word [ss:bx], 1234h
    je .t1equal
    mov ax, 0BAD0h
    jmp .t1done
.t1equal:
    mov ax, 1357h
.t1done:
    pop si
    push cx
    pop cx
    mov [di+1Ah], ax

; T2: after pop dx the same flag-propagated [ss:bx] read sits in the je's condition in the
; compare block — it observes the abandoned word, still 2222h only while push dx really ran.
; push bx covers the slot later.
    mov bx, sp
    sub bx, 2
    mov dx, 2222h
    push dx
    mov cx, dx
    pop dx
    jmp .t2cmp
.t2cmp:
    cmp word [ss:bx], 2222h
    je .t2equal
    mov ax, 0BAD1h
    jmp .t2done
.t2equal:
    mov ax, 2468h
.t2done:
    mov [di+1Ch], ax
    xor dx, dx
    push bx
    pop bx

; T3: same abandoned-word read after a segment-register pop - pop es frees the slot through
; its SP advance, the compare block's je condition reads [ss:bx], push bx covers it later.
    mov bx, sp
    sub bx, 2
    mov dx, 3333h
    push dx
    mov cx, dx
    pop es
    jmp .t3cmp
.t3cmp:
    cmp word [ss:bx], 3333h
    je .t3equal
    mov ax, 0BAD2h
    jmp .t3done
.t3equal:
    mov ax, 3579h
.t3done:
    mov [di+1Eh], ax
    xor dx, dx
    push bx
    pop bx

; T4: same shape with popf as the pop - 4646h keeps IF set so the final hlt still retires.
    mov bx, sp
    sub bx, 2
    mov dx, 4646h
    push dx
    mov cx, dx
    popf
    jmp .t4cmp
.t4cmp:
    cmp word [ss:bx], 4646h
    je .t4equal
    mov ax, 0BAD3h
    jmp .t4done
.t4equal:
    mov ax, 09ABCh
.t4done:
    mov [di+20h], ax
    xor dx, dx
    push bx
    pop bx

; T5: the direct form - push dx / pop dx are adjacent, so the rewrite would print the pop as
; the pushed expression and drop the push; the compare block's je condition reads the
; abandoned word at [ss:bx], still 5555h only while the push really ran.
    mov bx, sp
    sub bx, 2
    mov dx, 5555h
    push dx
    pop dx
    jmp .t5cmp
.t5cmp:
    cmp word [ss:bx], 5555h
    je .t5equal
    mov ax, 0BAD4h
    jmp .t5done
.t5equal:
    mov ax, 5151h
.t5done:
    mov [di+22h], ax
    xor dx, dx
    push bx
    pop bx

; T6: the direct form with a segment-register pop.
    mov bx, sp
    sub bx, 2
    mov dx, 6666h
    push dx
    pop es
    jmp .t6cmp
.t6cmp:
    cmp word [ss:bx], 6666h
    je .t6equal
    mov ax, 0BAD5h
    jmp .t6done
.t6equal:
    mov ax, 6262h
.t6done:
    mov [di+24h], ax
    xor dx, dx
    push bx
    pop bx

; T7: the direct form with popf - 4646h keeps IF set so the final hlt still retires.
    mov bx, sp
    sub bx, 2
    mov dx, 4646h
    push dx
    popf
    jmp .t7cmp
.t7cmp:
    cmp word [ss:bx], 4646h
    je .t7equal
    mov ax, 0BAD6h
    jmp .t7done
.t7equal:
    mov ax, 7373h
.t7done:
    mov [di+26h], ax
    xor dx, dx
    push bx
    pop bx

    hlt

reader4:
    mov bp, sp
    mov cx, [bp+2]
    mov [di+08h], cx
    ret

inputs:
    dw 0

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
