; Build: fasm standard_file_handles_inherited.asm standard_file_handles_inherited.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh

start:
    ; Standard handles 0, 1, and 2 are inherited in the PSP handle table.
    mov ah, 62h
    int 21h
    mov es, bx

    mov al, [es:0018h]
    mov dx, details_port
    out dx, al
    cmp al, 0
    jne failed

    mov al, [es:0019h]
    mov dx, details_port
    out dx, al
    cmp al, 1
    jne failed

    mov al, [es:001Ah]
    mov dx, details_port
    out dx, al
    cmp al, 2
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
