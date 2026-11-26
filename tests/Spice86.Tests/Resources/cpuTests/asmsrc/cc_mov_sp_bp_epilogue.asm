; compile it with fasm
; Shared "mov sp,bp" tail epilogues (the Watcom tail-merge shape): functions push
; their saved registers, copy SP into BP, and jmp a shared epilogue partition that
; restores SP from BP, pops the saves and rets.
; S1: two callers whose jump sites each hold SP and BP at entry delta -6 -> the
;     shared epilogue's mov sp,bp restores a trusted copy, the pops land the ret
;     on entry delta 0 and the callers type (w1 reads a word, w2 adds DX to it).
; S2: a jumper that rewrites BP between mov bp,sp and the jmp (`push bp; pop bp`
;     leaves the same value at runtime, but the analysis forgets the copy) ->
;     its shared epilogue stays unproved.
; S3: a jumper with an unproved call between mov bp,sp and the jmp (its register
;     call has two observed targets, so the slot model cannot prove its pop count:
;     it is a stack boundary that rebases the offset and forgets BP) -> unproved.
; DS = SS = 2000h, SP = 1000h. Results at offsets 40h-53h (physical 20040h-20053h).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

    mov word [100h], 1234h      ; w1 / w2 argument word
    mov bx, 7777h
    mov dx, 5555h
    mov bp, 5A5Ah

; S1: two callers sharing `mov sp,bp; pop bp; pop dx; pop bx; ret`.
    mov ax, 100h
    call w1
    mov [40h], ax
    mov ax, 100h
    call w2
    mov [42h], ax
    mov [44h], bx                 ; the epilogue's pops restored the caller's BX
    mov [46h], dx                 ; and DX
    mov [48h], bp                 ; and BP
; S2: a clean jumper and a BP-rewritten jumper share epi2 -> unproved.
    mov ax, 000Ah
    call w3a
    mov [4Ah], ax
    mov ax, 000Ah
    call w3b
    mov [4Ch], ax
; S3: a clean jumper and one with an unproved call share epi3 -> unproved.
    mov ax, 0100h
    call w4a
    mov [4Eh], ax
    mov word [84h], tiny_leaf
    mov ax, 0100h
    call w4b
    mov [50h], ax
    mov word [84h], tiny_leaf2
    mov ax, 0100h
    call w4b
    mov [52h], ax
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    mov [54h], sp
    hlt

w1:                             ; returns ax = [arg] or 0FFFFh
    push bx
    push dx
    push bp
    mov bp, sp
    mov bx, ax
    mov ax, [bx]
    test ax, ax
    jnz w1_done
    mov ax, 0FFFFh
w1_done:
    jmp epi1

w2:                             ; returns ax = [arg] + dx
    push bx
    push dx
    push bp
    mov bp, sp
    mov bx, ax
    mov ax, [bx]
    add ax, dx
    jmp epi1

epi1:
    mov sp, bp
    pop bp
    pop dx
    pop bx
    ret

w3a:                            ; returns arg + 1
    push bx
    push dx
    push bp
    mov bp, sp
    inc ax
    jmp epi2

w3b:                            ; BP rewritten between the copy and the jmp:
                                ; the pop leaves BP == SP at run time but the
                                ; analysis can no longer prove the copy.
    push bx
    push dx
    push bp
    mov bp, sp
    push bp
    pop bp
    add ax, 2
    jmp epi2

epi2:
    mov sp, bp
    pop bp
    pop dx
    pop bx
    ret

w4a:                            ; returns arg + 4
    push bx
    push dx
    push bp
    mov bp, sp
    add ax, 4
    jmp epi3

w4b:                            ; an unproved call between the copy and the jmp:
                                ; the unresolved register call is a stack boundary
                                ; that rebases the offset and forgets BP.
    push bx
    push dx
    push bp
    mov bp, sp
    mov bx, [84h]
    call bx
    add ax, 8
    jmp epi3

epi3:
    mov sp, bp
    pop bp
    pop dx
    pop bx
    ret

tiny_leaf:
    ret

tiny_leaf2:
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
