; Build: fasm ems_get_handle_count.asm ems_get_handle_count.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; INT 67h AH=4Bh returns at least the system handle in BX.
    mov ah, 4Bh
    int 67h
    cmp ah, 00h
    jne failed
    cmp bx, 0
    jbe failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
