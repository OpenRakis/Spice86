; Build from WSL: nasm -f bin umb_disabled.asm -o umb_disabled.com
bits 16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    mov ax, 5200h
    int 21h
    cmp word [es:bx + 66h], 0FFFFh
    jne failed
    test byte [es:bx + 63h], 01h
    jnz failed

    mov ax, 5803h
    mov bx, 0001h
    int 21h
    jnc failed
    cmp ax, 0001h
    jne failed

    mov ax, 5802h
    int 21h
    jc failed
    cmp al, 00h
    jne failed

    mov al, success
    jmp report

failed:
    mov al, failure

report:
    mov dx, result_port
    out dx, al
    hlt