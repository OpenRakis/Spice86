; Build: fasm ems_get_handle_pages.asm ems_get_handle_pages.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; An eight-page allocation must report eight pages for its handle.
    mov bx, 8
    mov ah, 43h
    int 67h
    cmp ah, 00h
    jne failed

    mov ah, 4Ch
    int 67h
    cmp ah, 00h
    jne failed
    cmp bx, 8
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
