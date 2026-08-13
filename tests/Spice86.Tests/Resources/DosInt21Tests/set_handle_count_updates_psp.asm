; Build: fasm set_handle_count_updates_psp.asm set_handle_count_updates_psp.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    mov ah, 62h
    int 21h
    mov es, bx

    mov ah, 67h
    mov bx, 0030h
    int 21h
    jc failed

    cmp word [es:0032h], 0030h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
