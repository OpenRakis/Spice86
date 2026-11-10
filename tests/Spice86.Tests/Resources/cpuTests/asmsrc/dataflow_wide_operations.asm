; compile it with fasm
; Wide operations: two half-register writes of one 32-bit operation combine into a wide
; temporary, a counted pair-shift loop keeps its event checks, and a loop-carried pair can
; promote to one uint local. DS = SS = 2000h, SP = 1000h. Pass p reads input pair (X in
; DX:AX, Y in CX:BX) at index p and writes results at offset 80h * p (physical 20000h + 80h * p).
; The int 64h observer (installed below) records BX:BP after the loop section. Every section
; ends with `cmp di,di` to kill all status flags.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov es, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:64h*4], observe_iret
    mov [es:64h*4+2], cs
    mov ax, 2000h
    mov es, ax
    cld
    xor di, di
    mov si, inputs

pass_loop:
; 00: AND halves split by an independent pure statement -> X & Y
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov bx, [cs:si+4]
    mov cx, [cs:si+6]
    and ax, bx
    inc bp
    and dx, cx
    dec bp
    mov [di+00h], ax
    mov [di+02h], dx
    cmp di, di

; 04: OR halves -> X | Y
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov bx, [cs:si+4]
    mov cx, [cs:si+6]
    or ax, bx
    or dx, cx
    mov [di+04h], ax
    mov [di+06h], dx
    cmp di, di

; 08: XOR halves -> X ^ Y
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov bx, [cs:si+4]
    mov cx, [cs:si+6]
    xor ax, bx
    xor dx, cx
    mov [di+08h], ax
    mov [di+0Ah], dx
    cmp di, di

; 0C: NOT halves -> ~X
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    not ax
    not dx
    mov [di+0Ch], ax
    mov [di+0Eh], dx
    cmp di, di

; 10: sar dx,1; rcr ax,1 -> (uint)((int)X >> 1). The second pair below repeats the
; same shift with pushf right after: a live flag traps the combine there.
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    sar dx, 1
    rcr ax, 1
    mov [di+10h], ax
    mov [di+12h], dx
    cmp di, di
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    sar dx, 1
    rcr ax, 1
    pushf
    pop cx
    and cx, 1
    mov [di+24h], cl        ;; CF out of rcr = old bit 0 of ax = X bit 0
    cmp di, di

; 14: the canonical compiler 32-bit product -> low 32 bits of X * Y. si/di are
; scratch inside the sequence, so the pointers are saved on the stack first.
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov bx, [cs:si+4]
    mov cx, [cs:si+6]
    push si
    push di
    mov si, ax
    mov di, dx
    mul bx
    mov bp, ax
    mov ax, si
    imul ax, cx
    add dx, ax
    mov ax, di
    imul ax, bx
    add dx, ax
    mov ax, bp
    pop di
    pop si
    mov [di+14h], ax
    mov [di+16h], dx
    cmp di, di

; 18: loop-carried pair BX:CX, initialized by pure copies, incremented three times
; -> X + 3. cmp kills the rotate's flags so the int observer does not pin them;
; the int forces the pair's write-backs, and the byte store at 2Eh reads BL alone.
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov bx, ax
    mov cx, dx
    mov bp, 3
.l18:
    add bx, 1
    adc cx, 0
    dec bp
    jnz .l18
    cmp di, di
    int 64h                 ;; observer: [di+30h]=bx, [di+32h]=cx
    mov [di+18h], bx
    mov [di+1Ah], cx
    mov [di+2Eh], bl
    cmp di, di

; 1C: counted shift pair, CX = 3 -> X << 3 (the event checks of a real loop keep
; it a loop; the count is constant so the semantics are still X << 3)
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov cx, 3
.l1c:
    shl ax, 1
    rcl dx, 1
    loop .l1c
    mov [di+1Ch], ax
    mov [di+1Eh], dx
    cmp di, di

; 20: the same counted shift with CX = 0 wraps to 65536 iterations -> 0; a
; rewrite of a zero entry as a no-shift would store X instead.
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    xor cx, cx
.l20:
    shl ax, 1
    rcl dx, 1
    loop .l20
    mov [di+20h], ax
    mov [di+22h], dx
    cmp di, di

; 28: the low half is stored between the two ANDs -> the pair must not combine,
; and both halves still land.
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    mov bx, [cs:si+4]
    mov cx, [cs:si+6]
    and ax, bx
    mov [di+28h], ax
    and dx, cx
    mov [di+2Ah], dx
    cmp di, di

; 34: NOT halves after `add ax,1` -> ~((x & 0xFFFF0000) | ((x + 1) & 0xFFFF)).
; X = 0xFFFF makes the low add carry into bit 16: a pair operand that forgets to
; truncate the low half to 16 bits would widen the carry into the high half.
    mov ax, [cs:si]
    mov dx, [cs:si+2]
    add ax, 1
    not ax
    not dx
    mov [di+34h], ax
    mov [di+36h], dx
    cmp di, di

    add di, 80h
    add si, 8
    cmp di, 4 * 80h
    jb pass_loop
    hlt

; Observer for int 64h: records BX:CX as the pair's write-backs must reach it.
observe_iret:
    mov [di+30h], bx
    mov [di+32h], cx
    iret

align 8
inputs:
    dd 000000000h, 000000001h
    dd 00000FFFFh, 000010001h
    dd 080000000h, 0FFFFFFFFh
    dd 012345678h, 0ABCDEF01h

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
