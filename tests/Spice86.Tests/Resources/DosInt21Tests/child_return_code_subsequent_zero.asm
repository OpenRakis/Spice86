; Build: fasm child_return_code_subsequent_zero.asm child_return_code_subsequent_zero.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; AH=4Dh clears the stored child return code, so the second read returns AX=0.
    mov ah, 4Dh
    int 21h
    mov ah, 4Dh
    int 21h
    test ax, ax
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
