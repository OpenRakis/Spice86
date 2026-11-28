; compile it with fasm
; A typed callee with a standard frame (push bp; mov bp,sp; no locals) holds a
; private push si / pop si pair, then reads the abandoned word back through the
; frame after the pop. The caller pre-fills that stack word (SS:0FFAh) with a
; sentinel, so a generated callee that dropped the push shows the sentinel
; instead of the pushed si. Under the abandoned-stack-slots relaxation the
; callee must be refused: a proved read of the abandoned bytes is a contrary
; fact.
; DS = SS = 2000h, SP = 1000h.
; Results go to offsets 40h-43h (physical 20040h-20043h).
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

; callee_read_after_pop's pushed si lands at SS:0FFAh (the call pushes the
; return address to 0FFEh, push bp takes 0FFCh and push si takes 0FFAh).
    mov word [ss:0FFAh], 0FACEh
    mov si, 1111h
    call callee_read_after_pop
    mov [40h], ax

    mov [42h], si

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

callee_read_after_pop:
    push bp
    mov bp, sp
    push si
    pop si
    mov ax, [bp-2]
    pop bp
    ret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
