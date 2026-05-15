; Build: fasm ems_logical_pages_independent.asm ems_logical_pages_independent.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
page_frame_segment equ 0E000h

start:
    ; Logical pages must keep independent data when remapped through the same physical page.
    mov bx, 2
    mov ah, 43h
    int 67h
    cmp ah, 00h
    jne failed
    mov si, dx
    mov ax, page_frame_segment
    mov es, ax

    mov al, 0
    mov bx, 0
    mov dx, si
    mov ah, 44h
    int 67h
    cmp ah, 00h
    jne failed
    mov byte [es:0], 11h

    mov al, 0
    mov bx, 1
    mov dx, si
    mov ah, 44h
    int 67h
    cmp ah, 00h
    jne failed
    mov byte [es:0], 22h

    mov al, 0
    mov bx, 0
    mov dx, si
    mov ah, 44h
    int 67h
    cmp ah, 00h
    jne failed
    mov al, [es:0]
    cmp al, 11h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
