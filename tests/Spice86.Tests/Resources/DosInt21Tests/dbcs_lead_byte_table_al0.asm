; Build: fasm dbcs_lead_byte_table_al0.asm dbcs_lead_byte_table_al0.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; INT 21h AH=63h AL=00h returns AL=0, CF clear, and DS:SI points to an empty DBCS table.
    mov ax, 6300h
    int 21h
    cmp al, 0
    jne failed
    jc failed
    mov dx, ds
    cmp dx, 0
    je failed
    cmp word [si], 0
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
