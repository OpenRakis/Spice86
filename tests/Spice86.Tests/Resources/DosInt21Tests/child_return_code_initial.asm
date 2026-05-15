; Build: fasm child_return_code_initial.asm child_return_code_initial.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; AH=4Dh initial return code should have normal termination type in AH.
    mov ah, 4Dh
    int 21h
    mov al, ah
    cmp al, 0
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
