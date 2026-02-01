; Simple EXE program used by integration tests.
; Writes the ASCII text "EXELOAD" to the upper-left character cell and exits.
; Small-model, tiny stack, and no relocations (PC-relative string pointer).
;
; This is documentation only—the compiled binary lives alongside this file as
; exec_test.exe.

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
        call    load_si
load_si:
        pop     si
        add     si, message - load_si
write_loop:
        lodsb
        mov     ah, 1Fh
        stosw
        loop    write_loop
        mov     ax, 4C00h
        int     21h

message db 'EXELOAD'
message_len equ $ - message

end start
