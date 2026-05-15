; Build: fasm dbcs_lead_byte_table_invalid_al.asm dbcs_lead_byte_table_invalid_al.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; INT 21h AH=63h with an invalid AL returns AL=FFh.
    mov ax, 6301h
    int 21h
    cmp al, 0FFh
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
