; Build: fasm ems_allocate_pages.asm ems_allocate_pages.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; INT 67h AH=43h allocates pages and returns a valid handle in DX.
    mov bx, 4
    mov ah, 43h
    int 67h
    cmp ah, 00h
    jne failed
    cmp dx, 0
    jbe failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
