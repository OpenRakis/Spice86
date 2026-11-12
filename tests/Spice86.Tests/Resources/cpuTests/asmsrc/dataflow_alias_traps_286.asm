; compile it with fasm
; 286 strict-MMU variant of the AL-6 wrap trap: a word access at offset 0FFFFh
; raises #GP instead of wrapping, so the store is skipped by the handler and the
; read of es:[10h] sees the 11h the es store left, not 22h. The handler counts
; the fault at DS:57h and resumes past the 6-byte store.
; DS = SS = 2000h. Result bytes go to DS:56h-57h.
use16
start:
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    xor bx, bx
    xor ax, ax
    mov es, ax
    mov word [es:0Dh*4], gp_handler
    mov word [es:0Dh*4+2], 0F000h
    mov ax, 1FFFh
    mov es, ax
    mov byte [es:10h], 11h
    mov word [0FFFFh], 2233h
    mov cl, [es:10h]
    mov [bx+56h], cl
    mov word [ss:600h], sp
    mov word [ss:602h], ss
    hlt

; #GP handler: counts the fault at DS:57h and resumes past the 6-byte
; mov word [0FFFFh], imm store
gp_handler:
    inc byte [57h]
    push bp
    mov bp, sp
    add word [ss:bp+2], 6
    pop bp
    iret

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
