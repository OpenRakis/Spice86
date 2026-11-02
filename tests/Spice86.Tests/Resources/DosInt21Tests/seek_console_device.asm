; Build: fasm seek_console_device.asm seek_console_device.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; DOS keeps a file position for character devices too: lseek on handle 1
    ; (stdout = CON) sets it, and writes advance it.
    mov bx, 0001h
    xor cx, cx
    mov dx, 0005h
    mov ax, 4200h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0005h
    jne failed

    mov bx, 0001h
    xor cx, cx
    xor dx, dx
    mov ax, 4200h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0000h
    jne failed

    mov bx, 0001h
    mov cx, 0003h
    mov dx, text
    mov ah, 40h
    int 21h
    jc failed
    cmp ax, 0003h
    jne failed

    mov bx, 0001h
    xor cx, cx
    xor dx, dx
    mov ax, 4201h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0003h
    jne failed

    mov bx, 0001h
    xor cx, cx
    mov dx, 0002h
    mov ax, 4202h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0002h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

text db 'abc'
