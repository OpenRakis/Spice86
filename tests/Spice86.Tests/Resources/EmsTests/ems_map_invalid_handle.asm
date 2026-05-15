; Build: fasm ems_map_invalid_handle.asm ems_map_invalid_handle.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
invalid_handle_error equ 083h

start:
    ; Mapping with invalid handle FFFFh must fail with error 83h.
    mov al, 0
    mov bx, 0
    mov dx, 0FFFFh
    mov ah, 44h
    int 67h
    cmp ah, invalid_handle_error
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
