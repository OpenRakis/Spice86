; Build from WSL: nasm -f bin xms_umb_disabled.asm -o xms_umb_disabled.com
bits 16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    mov ax, 4310h
    int 2Fh
    mov [xms_entry], bx
    mov [xms_entry + 2], es

    mov ah, 10h
    mov dx, 0100h
    call far [xms_entry]
    cmp ax, 0000h
    jne failed
    cmp bl, 80h
    jne failed

    mov al, success
    jmp report

failed:
    mov al, failure

report:
    mov dx, result_port
    out dx, al
    hlt

xms_entry dw 0, 0