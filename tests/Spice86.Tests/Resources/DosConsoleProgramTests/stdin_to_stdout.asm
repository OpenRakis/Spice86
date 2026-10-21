; Build: fasm stdin_to_stdout.asm stdin_to_stdout.com
; Reads 1 byte from stdin (INT 21h AH=3Fh handle 0) and writes it back to stdout
; (INT 21h AH=40h handle 1), then exits. The test redirects both standard streams and
; checks that the byte it fed in came back out unchanged.
; The 1-byte transfer buffer sits after the code, so the program carries one trailing zero
; byte.
use16
org 100h

    ; Read one byte from stdin into the buffer after the code.
    mov bx, 0                  ; handle 0 = stdin
    mov dx, data_buffer
    mov cx, 1
    mov ah, 3Fh                ; read from file or device
    int 21h
    ; Write that byte back to stdout.
    mov bx, 1                  ; handle 1 = stdout
    mov dx, data_buffer
    mov cx, 1
    mov ah, 40h                ; write to file or device
    int 21h
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h

data_buffer:
    db 0                       ; one-byte transfer buffer
