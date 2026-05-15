; Build: fasm xms_installation_check.asm xms_installation_check.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; INT 2Fh AX=4300h returns AL=80h when XMS is installed.
    mov ax, 4300h
    int 2Fh
    cmp al, 80h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
