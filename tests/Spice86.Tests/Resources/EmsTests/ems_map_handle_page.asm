; Build: fasm ems_map_handle_page.asm ems_map_handle_page.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; Allocate pages, then map logical page 0 to physical page 0.
    mov bx, 4
    mov ah, 43h
    int 67h
    cmp ah, 00h
    jne failed
    mov cx, dx

    mov al, 0
    mov bx, 0
    mov dx, cx
    mov ah, 44h
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
