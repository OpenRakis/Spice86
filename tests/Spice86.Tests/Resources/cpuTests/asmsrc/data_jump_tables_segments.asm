; compile with fasm
; Two far-called routines in different segments (F000:1000 and F100:1000) dispatch
; through tables at the same offset. Each dispatch reads its own segment's table.
; DS = SS = 2000h. Results at 40h-47h (physical 20040h): each counter reaches 2.
use16

macro dispatch_routine delta, segment_value, result_offset {
local pass,back,done,h0,h1,t0,t1,table
    mov cx, 4
pass:
    cmp cx, 4
    je h1
    cmp cx, 3
    je h0
    mov si, cx
    dec si
    shl si, 1
    mov ax, segment_value
    mov es, ax
    cmp si, 2
    ja done
    jmp word [es:si+table-delta]
back:
    dec cx
    jnz pass
done:
    retf
h0: jmp t0
h1: jmp t1
t0:
    inc word [result_offset]
    jmp back
t1:
    inc word [result_offset+2]
    jmp back
table:
    dw h0-delta, h1-delta
}

start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld
    call 0F000h:1000h
    call 0F100h:1000h
    hlt

    rb 1000h-$
    dispatch_routine 0, 0F000h, 40h

    rb 2000h-$
    dispatch_routine 1000h, 0F100h, 44h

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
