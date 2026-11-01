; compile it with fasm
; Sections for the alias-analysis legality conditions: a read merged past a word store that wraps
; the 16-bit offset, a read merged past an overlapping store, a read merged past an equal linear
; range reached through another segment register, an equal range reached through the A20 wrap, a
; device read moved past a device store, a read forwarded across a call, and one section where
; store forwarding fires.
; DS = SS = 2000h. Results go to offsets 40h-55h (physical 20040h-20055h); offsets 00h-1Fh are
; scratch the sections leave deterministic bytes in.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    xor bx, bx
    xor si, si

; AL-1: a word store at [0FFFFh] also touches offset 0, so the read must not move past it
; (each trap read is the last write of its register: a later write of the register would read
; the version the merge wants to move and refuse it)
    mov byte [0], 55h
    mov byte [1], 0
    mov di, [0]
    mov word [0FFFFh], 1234h
    shr di, 1
    mov [bx+40h], di

; AL-2: a store overlapping the read range between the read and its reader blocks the merge
    mov byte [si+2], 55h
    mov byte [si+3], 0
    mov bp, [si+2]
    mov word [si+1], 1234h
    shr bp, 1
    mov [bx+42h], bp

; AL-3 (A20 wrap): ES is set to 0, then to 0FFFFh, and the A20 gate is closed: the old es:[8000h]
; and the new es:[8010h] are the same linear bytes once the address wraps at 1MB
    xor ax, ax
    mov es, ax
    mov byte [es:8000h], 55h
    mov byte [es:8001h], 0
    mov si, [es:8000h]
    mov ax, 0FFFFh
    mov es, ax
    mov word [es:8010h], 1234h
    mov [bx+54h], si

; AL-4: a device read moved past a device store - planar memory at A000h: a read loads the 4
; latches, a write-mode-1 write copies them back out. The read of es:[0] loads the latches with the
; plane bytes written below; the mode-1 store to es:[10h] copies them there. Moving the read past
; the store leaves stale latches for the copy, and the read-back of es:[10h] shows it.
    push es
    mov dx, 03C4h
    mov ax, 0F02h
    out dx, ax
    mov ax, 0604h
    out dx, ax
    mov dx, 03CEh
    mov ax, 0006h
    out dx, ax
    mov ax, 0FF08h
    out dx, ax
    mov ax, 0A000h
    mov es, ax
    mov dx, 03C4h
    mov ax, 0102h
    out dx, ax
    mov byte [es:0], 11h
    mov ax, 0202h
    out dx, ax
    mov byte [es:0], 22h
    mov ax, 0402h
    out dx, ax
    mov byte [es:0], 44h
    mov ax, 0802h
    out dx, ax
    mov byte [es:0], 88h
    mov ax, 0F02h
    out dx, ax
    mov dx, 03CEh
    mov ax, 0105h
    out dx, ax
    mov cl, [es:0]
    mov byte [es:10h], 0
    mov [bx+4Eh], cl
    mov ax, 0005h
    out dx, ax
    mov ax, 0004h
    out dx, ax
    mov al, [es:10h]
    mov [bx+50h], al
    mov ax, 0104h
    out dx, ax
    mov al, [es:10h]
    mov [bx+51h], al
    mov ax, 0204h
    out dx, ax
    mov al, [es:10h]
    mov [bx+52h], al
    mov ax, 0304h
    out dx, ax
    mov al, [es:10h]
    mov [bx+53h], al
    mov ax, 0E06h
    out dx, ax
    pop es

; SF: forwarding fires - the read of [8] becomes a copy of the stored AX
    mov ax, 3333h
    mov word [8], ax
    mov dx, [8]
    mov [bx+4Ch], dx

; AL-5: rep movsb is a full memory boundary for forwarding (it rewrites [6])
    mov word [6], 1111h
    mov word [70h], 2222h
    mov ax, ds
    mov es, ax
    mov si, 70h
    mov di, 6
    mov cx, 2
    rep movsb
    mov cx, [6]
    mov [bx+4Ah], cx

; AL-3: DS = 2000h and ES = 2001h: [10h] and es:[0] are the same linear bytes
    mov byte [10h], 55h
    mov byte [11h], 0
    mov ax, 2001h
    mov es, ax
    mov dx, [10h]
    mov word [es:0], 1234h
    shr dx, 1
    mov [bx+44h], dx
; kill the shift's flag results so the shr expands to a plain >> (a call reader cannot merge)
    xor ax, ax

    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
