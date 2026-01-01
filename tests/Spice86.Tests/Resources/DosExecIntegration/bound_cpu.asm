; Simple EXE used by integration tests to ensure BOUND executes without fault.
; Source assembled for reference (see bound_cpu.bin for executable image).
;
; This program assumes the CPU supports the 80186+ BOUND instruction. It writes
; the string "BOUNDOK" to the top-left of the text mode screen.
;
; Assembled with MASM using the small model and linked to produce an MZ EXE.

.model small
.stack 0
.code
start:
        push    cs
        pop     ds
        mov     ax, 0B800h
        mov     es, ax
        xor     di, di
        mov     cx, message_len
        mov     si, OFFSET message
        mov     dx, 3
        bound   dx, bounds
print_loop:
        lodsb
        mov     ah, 7
        stosw
        loop    print_loop
        mov     ax, 4C00h
        int     21h

bounds  dw 0, 5
message db 'BOUNDOK'
message_len equ $ - message

end start
