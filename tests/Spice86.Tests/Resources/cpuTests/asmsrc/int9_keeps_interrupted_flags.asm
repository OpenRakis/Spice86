; compile with fasm
; A hardware keyboard interrupt must not change the interrupted code's flags.
; The program enables interrupts, clears CF, then waits for a key with flag-free
; instructions only (mov, not, lea, jcxz, jmp): while the BIOS keyboard buffer
; is empty head == tail and cx = head + ~tail + 1 stays 0. A key press delivers
; IRQ1 through the BIOS int9 handler, which fills the buffer; when the loop
; exits, the pushed FLAGS word is stored. Result region (DS = SS = 2000h):
; 40h = FLAGS after the key interrupt, 42h = key-arrived marker.
use16

start:
    cli
    mov ax, 2000h
    mov ds, ax
    mov ss, ax
    mov sp, 1000h
    mov ax, 40h
    mov es, ax
    sti
    clc
wait_key:
    mov bx, [es:1Ah]
    mov si, [es:1Ch]
    not si
    lea cx, [bx+si+1]
    jcxz wait_key
    pushf
    pop ax
    mov [40h], ax
    mov word [42h], 1
    hlt

    rb 65520-$
    jmp start
    rb 65535-$
    db 0FFh
