; Build from WSL: nasm -f bin umb_alloc_strategy.asm -o umb_alloc_strategy.com
bits 16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    push cs
    pop es
    mov ax, 4A00h
    mov bx, 1000h
    int 21h
    jc failed

    mov ax, 5801h
    mov bx, 0040h
    int 21h
    jc failed

    mov ax, 4800h
    mov bx, 2000h
    int 21h
    jnc failed
    cmp bx, 1FFFh
    jne failed

    mov ax, 4800h
    mov bx, 1FFFh
    int 21h
    jc failed
    cmp ax, 0D001h
    jne failed
    mov [umb_segment], ax

    mov ax, 5801h
    mov bx, 0080h
    int 21h
    jc failed

    mov ax, 4800h
    mov bx, 2000h
    int 21h
    jc failed
    cmp ax, 0A000h
    jae failed
    mov [low_segment], ax

    mov ax, 5801h
    mov bx, 00C3h
    int 21h
    jnc failed
    cmp ax, 0001h
    jne failed

    mov ax, 5801h
    mov bx, 00C0h
    int 21h
    jc failed

    mov ax, 4800h
    mov bx, 0100h
    int 21h
    jc failed
    cmp ax, 0A000h
    jae failed
    mov [fallback_segment], ax

    mov ax, 4900h
    mov es, [fallback_segment]
    int 21h
    jc failed

    mov ax, 4900h
    mov es, [low_segment]
    int 21h
    jc failed

    mov ax, 4900h
    mov es, [umb_segment]
    int 21h
    jc failed

    mov al, success
    jmp report

failed:
    mov al, failure

report:
    mov dx, result_port
    out dx, al
    hlt

umb_segment dw 0
low_segment dw 0
fallback_segment dw 0
