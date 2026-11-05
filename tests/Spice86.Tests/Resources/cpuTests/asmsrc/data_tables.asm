; compile it with fasm
; Data-modelling fixture: tables. DS = SS = 2000h. Results go to offsets 40h-4Ch (physical
; 20040h-2004Ch); offsets 60h-9Bh hold the tables. A 32-bit-offset SS read past the 64 KiB
; limit must fault (#SS) instead of wrapping to offset 0.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

; a byte table indexed by BX with a constant base - wrong rewrite: a base+index model that drops
; the index and reads the entry at the base
    mov byte [60h], 10
    mov byte [61h], 20
    mov byte [62h], 30
    mov byte [63h], 40
    mov bx, 2
    mov al, [bx+60h]
    mov [40h], al

; a word table indexed by shl bx,1 - wrong rewrite: modelling the index in bytes instead of
; elements (bx without the shl reads the odd bytes 2222h/1111h)
    mov word [64h], 1111h
    mov word [66h], 2222h
    mov word [68h], 3333h
    mov word [6Ah], 4444h
    mov bx, 1
    shl bx, 1
    mov ax, [bx+64h]
    mov [41h], ax

; a table of far pointers - wrong rewrite: reading the offset half as a near pointer and losing
; the segment half the les loads
    mov word [70h], 1234h
    mov word [72h], 5678h
    mov word [74h], 9ABCh
    mov word [76h], 0DEF0h
    xor bx, bx
    les di, [bx+70h]
    mov ax, di
    mov [43h], ax
    mov ax, es
    mov [45h], ax
    mov ax, 2000h
    mov es, ax

; a table written at run time - wrong rewrite: reading the (never initialized) table bytes as a
; build-time constant instead of the values stored here
    mov byte [80h], 1
    mov byte [81h], 2
    mov byte [82h], 3
    mov word [88h], 2
    mov bx, [88h]
    mov al, [bx+80h]
    mov [47h], al

; a two-dimensional table, row * width + column by shl and add - wrong rewrite: a flat model that
; loses the row stride and reads column alone
    mov byte [90h], 0
    mov byte [91h], 1
    mov byte [92h], 2
    mov byte [93h], 3
    mov byte [94h], 10
    mov byte [95h], 11
    mov byte [96h], 12
    mov byte [97h], 13
    mov byte [98h], 20
    mov byte [99h], 21
    mov byte [9Ah], 22
    mov byte [9Bh], 23
    mov ax, 1
    shl ax, 2
    add ax, 3
    mov bx, ax
    mov al, [bx+90h]
    mov [48h], al

; a byte read through a wrapping effective address - bx+60h wraps past 64KiB to 0050h, so
; the read lands on the byte at DS:0050 (physical 20050h), not the byte at linear 30050h.
; Wrong rewrite: an offset that keeps its full width faults on a 286+ model or reads the
; wrong byte on a wrapping memory model
    mov byte [50h], 5Ch
    mov ax, 3005h
    mov es, ax
    mov byte [es:0], 9Eh
    mov ax, 2000h
    mov es, ax
    mov ax, 0FFF0h
    push ax
    xor ax, ax
    pop bx
    mov al, [bx+60h]
    mov [49h], al

; A stack read whose 32-bit offset does not fit 16 bits. On the 286 model the access
; raises #SS: the handler marks [4Ah], snapshots the live BX at [4Bh] and resumes at
; the continuation label by rewriting the saved IP in its interrupt frame. The offset
; must stay a 32-bit value; a wrapped 16-bit offset reads SS:0000h, skips the fault
; and leaves [4Ah] = 0.
    mov byte [4Ah], 0
    xor ax, ax
    mov es, ax
    mov word [es:30h], ss_fault_handler
    mov word [es:32h], cs
    mov ax, 2000h
    mov es, ax
    mov bx, 1234h
    mov al, [ss:10000h]
ss_fault_continuation:
    hlt

ss_fault_handler:
    push bp
    mov bp, sp
    mov byte [4Ah], 1
    mov [4Bh], bx
    mov word [bp+2], ss_fault_continuation
    pop bp
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
