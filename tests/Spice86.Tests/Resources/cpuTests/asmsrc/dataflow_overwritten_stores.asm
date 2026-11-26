; compile it with fasm
; Sections for the overwritten-store legality conditions: an exact overwrite and a containing
; overwrite removed; a partial overlap, an aliased read, a call, a faulting statement and a port
; write between, and a store to device memory, each keeping the first store.
; DS = SS = 2000h. Results go to offsets 40h-64h (physical 20040h-20064h); offsets 20h-3Eh are
; scratch the sections store into.
use16
start:
; initial registers are zero: ds = 0 reaches the IVT
    mov word [0*4], divhandler
    mov word [0*4+2], cs
    mov word [cs:400h], 0

    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    xor bx, bx
    xor si, si

; an exact overwrite removes the first store
    mov word [20h], 1111h
    mov word [20h], 2222h
    mov ax, [20h]
    mov [bx+40h], ax

; a containing overwrite removes both byte stores
    mov byte [22h], 11h
    mov byte [23h], 22h
    mov word [22h], 3333h
    mov ax, [22h]
    mov [bx+42h], ax

; a partial overlap keeps the store - the byte store covers only the low byte
    mov word [24h], 1111h
    mov byte [24h], 55h
    mov ax, [24h]
    mov [bx+44h], ax

; an aliased read between keeps the store
    mov word [26h], 1111h
    mov ax, [26h]
    mov word [26h], 2222h
    mov [bx+46h], ax

; a call between keeps the store - the callee reads [28h] into [bx+48h]
    mov word [28h], 1111h
    call read_28h
    mov word [28h], 2222h
    mov ax, [28h]
    mov [bx+4Ch], ax

; a faulting statement between keeps the store - the div faults into int 0, whose handler
; reads [2Ah] into [bx+4Ah] before the second store covers it
    mov word [2Ah], 1111h
    mov dx, 0
    mov ax, 10
    div word [cs:400h]
resume_after_div:
    mov word [2Ah], 2222h
    mov ax, [2Ah]
    mov [bx+4Eh], ax

; a store to device memory is never proved RAM - es = A000h keeps the first store
    mov ax, 0A000h
    mov es, ax
    mov word [es:30h], 1111h
    mov word [es:30h], 2222h

; a port write between keeps the store - a port call can write RAM through a device
    mov word [32h], 1111h
    mov al, 0
    out 80h, al
    mov word [32h], 2222h
    mov ax, [32h]
    mov [bx+50h], ax

; reload DS so the sections' stores provably hit a known RAM segment
    mov ax, 2000h
    mov ds, ax

; a store overwritten in the jump target's block is removed too
    mov word [34h], 1111h
    jmp os6_cover
os6_cover:
    mov word [34h], 2222h
    mov ax, [34h]
    mov [bx+52h], ax

; both arms of a branch cover the store - it is removed in principle, but the
; not-taken arm lands on a speculated entry whose verification stands between, so the
; generated code keeps the first store.
    mov word [36h], 1111h
    mov cx, [4Ah]
    cmp cx, 0
    je os7_b
    mov word [36h], 2222h
    jmp os7_done
os7_b:
    mov word [36h], 2222h
os7_done:
    mov ax, [36h]
    mov [bx+54h], ax

; one arm reads the slot before covering - the first store stays and the read sees it
    mov word [38h], 1111h
    mov cx, [4Eh]
    cmp cx, 0
    jne os7_read
    mov word [38h], 2222h
    jmp os7_rdone
os7_read:
    mov ax, [38h]
    mov [bx+56h], ax
    mov word [38h], 2222h
os7_rdone:
    mov ax, [38h]
    mov [bx+58h], ax

; one arm never covers the store - it stays and the non-covering path sees it
    mov word [3Ah], 1111h
    mov cx, [62h]
    cmp cx, 0
    je os7_nc
    mov word [3Ah], 2222h
    jmp os7_ncdone
os7_nc:
    mov ax, [3Ah]
    mov [bx+5Ah], ax
os7_ncdone:
    mov ax, [3Ah]
    mov [bx+5Ch], ax

; a branch condition reading the stored bytes counts as a read - the store stays and
; the not-taken arm (a wrong removal would take the branch instead) covers it
    mov word [3Ch], 1111h
    cmp word [3Ch], 0
    je os7c_b
    mov word [3Ch], 2222h
    jmp os7c_done
os7c_b:
    mov word [bx+5Eh], 0BADh
os7c_done:
    mov ax, [3Ch]
    mov [bx+60h], ax

; a call inside the chained block keeps the store - the callee reads it
    mov word [3Eh], 1111h
    jmp os6_call
os6_call:
    call read_3eh
    mov word [3Eh], 2222h
    mov ax, [3Eh]
    mov [bx+62h], ax

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

read_3eh:
    mov ax, [3Eh]
    mov [bx+64h], ax
    ret

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
    mov word [cs:400h], 1
    mov word [bp+2], resume_after_div
    pop bp
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
