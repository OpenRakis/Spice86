; Build: fasm stdin_read_ah08.asm stdin_read_ah08.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh

start:
    ; AH=08h should read from redirected STDIN handle, not directly from INT 16h.
    mov ah, 08h
    int 21h
    mov dx, details_port
    out dx, al
    cmp al, 'A'
    jne failed
    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
