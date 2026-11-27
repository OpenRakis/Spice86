; compile it with fasm
; One call [bx] dispatching over two leaf targets: the loop runs the same call once
; per slot, so the instruction observes both targets and the generated code prints a
; switch with one section per target. d_ax reads CX and writes AX; d_cx reads AX and
; writes CX. The registers the caller stores after the call are also stored before
; it, so each version has two uses and may print through a local - a section charged
; by the other target keeps the pre-call version across the call and its store would
; take the stale local instead of the register the callee wrote.
; DS = SS = 2000h, SP = 1000h. Results go to offsets 40h-4Bh (physical 20040h-2004Bh);
; the dispatch slots sit at DS:0200h/0202h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    cld

    mov word [200h], d_ax
    mov word [202h], d_cx

    xor si, si
dispatch_loop:
    mov ax, 1111h
    mov cx, 0AAAAh
    mov [48h], cx
    mov [4Ah], ax
    lea bx, [si + 200h]
    call word [bx]
    mov [40h + si], ax
    mov [44h + si], cx
    add si, 2
    cmp si, 4
    jb dispatch_loop

    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

d_ax:
    mov dx, cx
    add dx, 5
    mov ax, dx
    ret

d_cx:
    mov dx, ax
    add dx, 7
    mov cx, dx
    ret

rb 65520-$
    jmp start
rb 65535-$
    db 0FFh
