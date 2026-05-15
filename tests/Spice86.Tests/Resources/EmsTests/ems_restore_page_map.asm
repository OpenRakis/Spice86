; Build: fasm ems_restore_page_map.asm ems_restore_page_map.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; Save the system page map, then restore it with INT 67h AH=48h.
    mov dx, 0
    mov ah, 47h
    int 67h
    cmp ah, 00h
    jne failed

    mov dx, 0
    mov ah, 48h
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
