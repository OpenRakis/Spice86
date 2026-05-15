; Build: fasm stdin_read_ah01.asm stdin_read_ah01.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh

start:
    ; AH=01h should read the redirected STDIN byte and echo it.
    mov ah, 01h
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
