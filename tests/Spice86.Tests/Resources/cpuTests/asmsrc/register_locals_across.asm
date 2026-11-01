; compile it with fasm
; Register-local promotion across blocks, phis and calls. Each section produces a result word at
; ds:40h.. that a wrong promotion corrupts: a phi read must see the arm that ran, a loop index must
; stay right through the loop, a register must be written back before a call whose callee reads it,
; a web ends where the callee changes the register, a web also ends at an event check whose
; hardware handler may change the register, and a write-back must stand where a path can bring a
; register value the local does not hold. DS = SS = 2000h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    mov di, 40h
    mov si, inputs

; M1: both arms of an if/else define BX, the join reads the version of the arm that ran
    mov bx, [cs:si]
    cmp ax, ax
    jne m1_else
    mov bx, [cs:si+2]
    jmp m1_join
m1_else:
    mov bx, [cs:si+4]
m1_join:
    mov [di+00h], bx

; M2: SI is the loop index of a cmp/jne loop and the index of the loads it feeds
    xor si, si
    xor ax, ax
m2_loop:
    mov cx, [cs:si+m2data]
    add ax, cx
    add si, 2
    cmp si, 6
    jne m2_loop
    mov [di+02h], ax
    mov [di+04h], si

    mov si, inputs

; M3: BX live across a call to a leaf that neither reads nor writes it
    mov bx, [cs:si+8]
    call leaf_alone
    mov [di+06h], bx

; M4: the leaf stores what it reads in BX, so the write-back must stand before the call
    mov bx, [cs:si+10]
    call leaf_reads_bx

; M5: the leaf changes BX; the read after the call is the leaf's value, not the local's
    mov bx, [cs:si+12]
    call leaf_writes_bx
    mov [di+0Ah], bx

; M6: the callee lands the program elsewhere instead of at its call site
    mov bx, [cs:si+14]
    call m6_elsewhere_entry
m6_retaddr:
    nop
m6_elsewhere:
    mov [di+0Ch], bx

; M7: the timer handler changes AX without restoring it; the value stored after the wait loop is
; the handler's, never the pre-interrupt local's
    xor ax, ax
    mov es, ax
    mov word [es:8*4], irq0_ax
    mov [es:8*4+2], cs
    mov ax, ds
    mov es, ax
    mov byte [0E60h], 0
    mov al, 00110110b
    out 43h, al
    mov al, 051h
    out 40h, al
    mov al, 022h
    out 40h, al
    xor al, al
    out 21h, al
    sti
    mov ax, [cs:si+6]
m7_wait:
    cmp byte [0E60h], 0
    je m7_wait
    cli
    mov [di+0Eh], ax

; M8: DX lives in a local across a call whose leaf checks events at its entry; a timer tick made
; pending by the caller's delay fires at the callee's post-sti check and must reach the store,
; not the local frozen before the call
    mov cx, 0FFFFh
m8_delay:
    dec cx
    jnz m8_delay
    mov dx, [cs:si+8]
    call m8_wait
    mov [di+14h], dx
    cli
    mov dx, 0

; W1: a loop back edge enters between two implicit uses of a bx local: the local changes on
; every iteration while the register does not, so the write-back inside the loop must stay
; (leaf_reads_bx_w1 stores what BX holds at each call; the last store lands at 000Ah)
    mov bx, 40h
    call leaf_alone
    mov cx, 3
w1_loop:
    call leaf_reads_bx_w1
    push bx
    pop ax
    add ax, 1
    mov bx, ax
    dec cx
    jnz w1_loop

; W2: a jump to a label brings a path where the register holds another value: the executed path
; redefines the local without touching the register, so the write-back at the join must stay
; (the segment-prefixed loads are implicit uses; the join load reads a different byte when the
; register keeps the pre-join segment, its byte reaching 0092h)
    mov bp, 0F000h
    mov es, bp
    mov cx, es
    mov dx, [cs:si]
    cmp dx, 1
    je w2_p1
w2_p2:
    mov bp, 0F001h
    mov es, bp
    jmp w2_join
w2_p1:
    mov al, [es:41h]
w2_join:
    mov al, [es:42h]
    mov [di+52h], al
w2_done:

; W3: a call whose callee changes the register sits between two implicit uses; the version after
; the call belongs to another web (the stores land at 0094h and 0096h)
    mov bx, 40h
    push bx
    pop ax
    mov [di+54h], ax
    call leaf_alone
    call leaf_writes_bx
    push bx
    pop ax
    mov bx, ax
    push bx
    pop cx
    mov [di+56h], cx
    call leaf_alone

; W4: an event check between two implicit uses while the IRQ0 handler changes AX; the read after
; the check is the handler's value, not the pre-check local's (the words land at 009Ah..009Eh)
    mov ax, 40h
    push ax
    pop cx
    mov [di+5Ah], cx
    mov [di+5Ch], ax
    mov byte [0E60h], 0
    sti
w4_wait:
    cmp byte [0E60h], 0
    je w4_wait
    cli
    push ax
    pop cx
    mov ax, cx
    push ax
    pop dx
    mov [di+5Eh], dx
    call leaf_alone

; W5: a sub-register write between two implicit uses of DX (div reads DX:AX); the div after reads
; dx with dl replaced, which belongs to another web (quotients land at 007Ch and 007Eh)
    mov dx, 0
    push dx
    pop cx
    add cx, 100h
    mov ax, [cs:si+8]
    div cx
    mov dl, 55h
    push dx
    pop bx
    mov dx, bx
    push dx
    pop bx
    add ax, bx
    div cx
    mov [di+3Ch], ax
    mov [di+3Eh], dx

; F2's stosw writes through ES
    mov ax, 2000h
    mov es, ax

; F1: the mid-body jump back to the loop head skips the increment; a for header would run the step on
; that path and change the sum
    xor ax, ax
    xor si, si
    xor bx, bx
f1_head:
    cmp bx, 10
    jae f1_done
    add bx, [cs:si+f1data]
    add si, 2
    add ax, bx
    test bx, 1
    jnz f1_head
    inc bx
    jmp f1_head
f1_done:
    mov [di+10h], ax
    mov [di+12h], bx

; D1: two loop exits jump to one shared ret tail; tail duplication pastes it at each exit. BX holds
; a different value per branch, so each copy must write its local back before the ret or the real
; register the caller reads afterwards stays stale
    mov si, inputs
    xor ax, ax
    call d1_fn
    mov [di+26h], bx
    add di, 8
    mov ax, 1
    call d1_fn
    mov [di+26h], bx

; F2: a rep stos counted store over CX; the counted loop must print as a for whose init, test and step
; all read the promoted local
    mov ax, 5555h
    mov di, 58h
    mov cx, 5
    rep stosw
    hlt

leaf_alone:
    ret
m8_wait:
    sti
    nop
    ret
d1_fn:
    cmp ax, 0
    jne d1_p2
    mov bx, [cs:si]
    mov byte [di+22h], 1
    mov [di+24h], bx
    mov cx, 3
d1_w1:
    cmp cx, 9
    je d1_u1
    cmp cx, 1
    je d1_epi
    dec cx
    jmp d1_w1
d1_u1:
    inc si
    hlt
d1_p2:
    mov bx, [cs:si+2]
    mov byte [di+22h], 2
    mov [di+24h], bx
    mov cx, 2
d1_w2:
    cmp cx, 9
    je d1_u2
    cmp cx, 1
    je d1_epi
    dec cx
    jmp d1_w2
d1_u2:
    inc si
    hlt
leaf_reads_bx:
    mov [di+08h], bx
    ret
leaf_reads_bx_w1:
    mov [di+50h], bx
    ret
leaf_writes_es:
    mov bp, 2001h
    mov es, bp
    ret
leaf_writes_bx:
    mov bx, 7777h
    ret
m6_elsewhere_entry:
    mov bp, sp
    add word [bp], m6_elsewhere - m6_retaddr
    ret
irq0_ax:
    mov [0E64h], ax
    mov ax, 0BEEFh
    mov dx, 0BEEFh
    mov bp, 2001h
    mov es, bp
    mov byte [0E60h], 1
    mov al, 20h
    out 20h, al
    iret
d1_epi:
    ret

m2data:
    dw 0010h, 0020h, 0030h
f1data:
    dw 0003h, 0004h, 0005h, 0006h
inputs:
    dw 1111h, 2222h, 3333h, 0000h, 4444h, 5555h, 6666h, 8888h

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
