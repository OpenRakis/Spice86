; Build: fasm ems_allocate_zero_pages.asm ems_allocate_zero_pages.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
zero_page_error equ 089h

start:
    ; Allocating zero EMS pages must fail with error 89h.
    mov bx, 0
    mov ah, 43h
    int 67h
    cmp ah, zero_page_error
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
