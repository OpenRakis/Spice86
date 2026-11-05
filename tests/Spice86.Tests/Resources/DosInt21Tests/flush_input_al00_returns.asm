; Build: nasm -f bin flush_input_al00_returns.asm -o flush_input_al00_returns.com
; INT 21h AH=0Ch only runs a follow-up input function for AL=01h, 06h, 07h, 08h or 0Ah.
; With AL=00h it flushes the input buffer and returns: function 00h (terminate) must not run.
use16
org 100h

result_port equ 0999h
success equ 00h

start:
    mov ax, 0C00h
    int 21h

    mov al, success
    mov dx, result_port
    out dx, al
    hlt
