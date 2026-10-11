; Build from WSL: nasm -f bin umb_ems_size.asm -o umb_ems_size.com
bits 16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    mov ax, 5200h
    int 21h
    cmp word [es:bx + 66h], 9FFFh
    jne failed

    mov ax, 0D000h
    mov es, ax
    cmp byte [es:0], 5Ah
    jne failed
    cmp word [es:3], 0FFFh
    jne failed

    mov al, success
    jmp report

failed:
    mov al, failure

report:
    mov dx, result_port
    out dx, al
    hlt