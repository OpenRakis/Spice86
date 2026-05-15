; Build: fasm keyboard_reassignment_reset.asm keyboard_reassignment_reset.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h

start:
    ; Write ESC[0p to reset key redefinitions and install Ctrl+PrintScreen -> Ctrl+P.
    mov ah, 02h
    mov dl, 1Bh
    int 21h
    mov dl, '['
    int 21h
    mov dl, '0'
    int 21h
    mov dl, 'p'
    int 21h

    ; Stuff Ctrl+PrintScreen into the BIOS keyboard buffer with scan 72h and ASCII 00h.
    mov ah, 05h
    mov cx, 7200h
    int 16h

    ; DOS AH=08h should read Ctrl+P (10h) after key reassignment.
    mov ah, 08h
    int 21h
    mov dx, details_port
    out dx, al

    mov al, success
    mov dx, result_port
    out dx, al
    hlt
