; Build: fasm ems_get_unallocated_page_count.asm ems_get_unallocated_page_count.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; INT 67h AH=42h returns available pages in BX and total pages in DX.
    mov ah, 42h
    int 67h
    cmp ah, 00h
    jne failed
    cmp dx, 0
    jbe failed
    cmp bx, dx
    ja failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
