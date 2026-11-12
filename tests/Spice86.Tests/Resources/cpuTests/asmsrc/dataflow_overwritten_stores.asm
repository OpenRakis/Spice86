; compile it with fasm
; Sections for the overwritten-store legality conditions: an exact overwrite and a containing
; overwrite removed; a partial overlap, an aliased read, a call, a faulting statement and a port
; write between, and a store to device memory, each keeping the first store.
; DS = SS = 2000h. Results go to offsets 40h-4Eh (physical 20040h-2004Eh); offsets 20h-33h are
; scratch the sections store into.
use16
start:
; initial registers are zero: ds = 0 reaches the IVT
    mov word [0*4], divhandler
    mov word [0*4+2], cs
    mov word [cs:100h], 0

    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    xor bx, bx
    xor si, si

; OS-1: an exact overwrite removes the first store
    mov word [20h], 1111h
    mov word [20h], 2222h
    mov ax, [20h]
    mov [bx+40h], ax

; OS-1: a containing overwrite removes both byte stores
    mov byte [22h], 11h
    mov byte [23h], 22h
    mov word [22h], 3333h
    mov ax, [22h]
    mov [bx+42h], ax

; OS-2: a partial overlap keeps the store - the byte store covers only the low byte
    mov word [24h], 1111h
    mov byte [24h], 55h
    mov ax, [24h]
    mov [bx+44h], ax

; OS-3: an aliased read between keeps the store
    mov word [26h], 1111h
    mov ax, [26h]
    mov word [26h], 2222h
    mov [bx+46h], ax

; OS-3: a call between keeps the store - the callee reads [28h] into [bx+48h]
    mov word [28h], 1111h
    call read_28h
    mov word [28h], 2222h
    mov ax, [28h]
    mov [bx+4Ch], ax

; OS-3: a faulting statement between keeps the store - the div faults into int 0, whose handler
; reads [2Ah] into [bx+4Ah] before the second store covers it
    mov word [2Ah], 1111h
    mov dx, 0
    mov ax, 10
    div word [cs:100h]
resume_after_div:
    mov word [2Ah], 2222h
    mov ax, [2Ah]
    mov [bx+4Eh], ax

; OS-1: a store to device memory is never proved RAM - es = A000h keeps the first store
    mov ax, 0A000h
    mov es, ax
    mov word [es:30h], 1111h
    mov word [es:30h], 2222h

; OS-3: a port write between keeps the store - a port call can write RAM through a device
    mov word [32h], 1111h
    mov al, 0
    out 80h, al
    mov word [32h], 2222h
    mov ax, [32h]
    mov [bx+50h], ax

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

read_28h:
    mov ax, [28h]
    mov [bx+48h], ax
    ret

; int 0 handler: store the byte the first store left at [2Ah], set divisor 1 so a re-run would
; not fault, and adjust the return IP past the div.
divhandler:
    push bp
    mov bp, sp
    mov ax, [2Ah]
    mov [bx+4Ah], ax
    mov word [cs:100h], 1
    mov word [bp+2], resume_after_div
    pop bp
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
