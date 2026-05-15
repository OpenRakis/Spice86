; Build: fasm ems_map_physical_page_4.asm ems_map_physical_page_4.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
illegal_physical_page_error equ 08Bh

start:
    ; Physical page 4 must be rejected with the illegal physical page error; only pages 0-3 are valid.
    mov bx, 4
    mov ah, 43h
    int 67h
    cmp ah, 00h
    jne failed
    mov cx, dx

    mov al, 4
    mov bx, 0
    mov dx, cx
    mov ah, 44h
    int 67h
    cmp ah, illegal_physical_page_error
    je succeeded

failed:
    mov al, failure
    jmp write_result

succeeded:
    mov al, success

write_result:
    mov dx, result_port
    out dx, al
    hlt
