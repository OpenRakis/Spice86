; compile with fasm
; A typed method whose inputs only reach an untyped call, and one of whose
; outputs only that call writes. The untyped callee runs an int into a program
; handler (an int keeps a method untyped). DS = SS = 2000h.
; Results (words): 40h = AX after the call (set by the handler),
; 42h = CX (computed by the typed method from AX).
use16

start:
    cli
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor ax, ax
    mov es, ax
    mov word [es:60h*4], handler
    mov [es:60h*4+2], cs
    mov ax, 5
    mov bx, 7
    call wrapper
    mov [40h], ax
    mov [42h], cx
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

wrapper:
    call untyped_int
    mov cx, ax
    add cx, cx
    ret

untyped_int:
    int 60h
    ret

handler:
    add ax, bx
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
