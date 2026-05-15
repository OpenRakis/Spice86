; Build: fasm ems_deallocate_pages.asm ems_deallocate_pages.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; Allocate pages, then deallocate the returned handle.
    mov bx, 4
    mov ah, 43h
    int 67h
    cmp ah, 00h
    jne failed
    mov cx, dx

    mov dx, cx
    mov ah, 45h
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
