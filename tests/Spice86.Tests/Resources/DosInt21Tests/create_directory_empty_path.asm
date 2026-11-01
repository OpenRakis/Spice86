; Build: fasm create_directory_empty_path.asm create_directory_empty_path.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; AH=39h with an empty ASCIIZ path must fail with CF=1 and error 03h (path not found).
    mov dx, empty_path
    mov ah, 39h
    int 21h
    jnc failed
    cmp ax, 0003h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

empty_path db 00h
