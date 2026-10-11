; Build from WSL: nasm -f bin xms_request_release_umb.asm -o xms_request_release_umb.com
bits 16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    mov byte [stage], 1
    mov ax, 4310h
    int 2Fh
    mov [xms_entry], bx
    mov [xms_entry + 2], es

    mov ah, 10h
    mov dx, 2000h
    call far [xms_entry]
    cmp ax, 0000h
    jne failed
    cmp bl, 0B0h
    jne failed
    cmp dx, 1FFFh
    jne failed

    mov byte [stage], 2
    mov ax, 5800h
    int 21h
    cmp ax, 0000h
    jne failed
    mov ax, 5802h
    int 21h
    jc failed
    cmp al, 00h
    jne failed

    mov byte [stage], 3
    mov ah, 10h
    mov dx, 1FFFh
    call far [xms_entry]
    cmp ax, 0001h
    jne failed
    mov [umb_segment], bx
    cmp bx, 0D001h
    jne failed

    mov ah, 10h
    mov dx, 0001h
    call far [xms_entry]
    cmp ax, 0000h
    jne failed
    cmp bl, 0B1h
    jne failed
    cmp dx, 0000h
    jne failed

    mov byte [stage], 4
    mov ax, 5800h
    int 21h
    cmp ax, 0000h
    jne failed
    mov ax, 5802h
    int 21h
    jc failed
    cmp al, 00h
    jne failed

    mov byte [stage], 5
    mov ah, 11h
    mov dx, [umb_segment]
    call far [xms_entry]
    cmp ax, 0001h
    jne failed

    mov ax, 5800h
    int 21h
    cmp ax, 0000h
    jne failed
    mov ax, 5802h
    int 21h
    jc failed
    cmp al, 00h
    jne failed

    mov ah, 11h
    mov dx, [umb_segment]
    call far [xms_entry]
    cmp ax, 0000h
    jne failed
    cmp bl, 0B2h
    jne failed

    mov byte [stage], 6
    mov ah, 12h
    mov bx, 0100h
    mov dx, [umb_segment]
    call far [xms_entry]
    cmp ax, 0000h
    jne failed
    cmp bl, 80h
    jne failed

    mov byte [stage], 7
    mov ax, 5800h
    int 21h
    cmp ax, 0000h
    jne failed
    mov ax, 5802h
    int 21h
    jc failed
    cmp al, 00h
    jne failed

    mov al, success
    jmp report

failed:
    mov al, [stage]

report:
    mov dx, result_port
    out dx, al
    hlt

xms_entry dw 0, 0
umb_segment dw 0
stage db 0