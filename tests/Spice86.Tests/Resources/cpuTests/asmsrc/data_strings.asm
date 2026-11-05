; compile it with fasm
; Data-modelling fixture: strings, a DOS .com. Results go to ES=2000h offsets 40h-49h (physical
; 20040h-20049h). The int 21h return values are oracle bytes, not fixtures of DOS.
org 100h
use16
start:
    mov ax, 2000h
    mov ss, ax
    mov sp, 1000h
    mov es, ax

; int 21h AH=09 printing a '$'-terminated string - wrong rewrite: modelling the message bytes as
; something else than the string constant the call reads
    mov dx, msg
    mov ah, 09h
    int 21h
    mov [es:40h], ax

; int 21h AH=40 with an explicit length to the standard output handle - wrong rewrite: assuming
; the string shape (a terminator) the handle write does not use
    mov bx, 1
    mov cx, blob_len
    mov dx, blob
    mov ah, 40h
    int 21h
    mov [es:42h], ax
    mov ax, 0
    adc ax, 0
    mov [es:44h], al

; an ASCIIZ file name for AH=3D: the open may fail - wrong rewrite: modelling the name without
; its zero terminator so the open reads past it
    mov dx, fname
    mov al, 0
    mov ah, 3Dh
    int 21h
    mov [es:45h], ax
    mov ax, 0
    adc ax, 0
    mov [es:47h], al

; a string built at run time in a buffer, then printed - wrong rewrite: printing (or reading) the
; buffer as the constant the assembler left in it instead of the stored bytes
    mov byte [dynmsg], 'H'
    mov byte [dynmsg+1], 'i'
    mov byte [dynmsg+2], '$'
    mov dx, dynmsg
    mov ah, 09h
    int 21h
    mov [es:48h], ax

; a non-printable blob read as data - wrong rewrite: modelling it as a string and stopping at the
; first byte a printer would not show
    mov al, [blob+1]
    mov [es:49h], al

    hlt

msg     db 'HELLO$'
fname   db 'NOFILE.TXT', 0
blob    db 01h, 02h, 03h, 0FFh
blob_len = $ - blob
dynmsg  rb 8
