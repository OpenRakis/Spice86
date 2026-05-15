; Build: fasm ems_get_status.asm ems_get_status.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; INT 67h AH=40h returns status AH=00h when EMM is operational.
    mov ah, 40h
    int 67h
    cmp ah, 00h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
