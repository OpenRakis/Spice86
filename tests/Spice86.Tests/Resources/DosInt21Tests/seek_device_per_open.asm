; Build: fasm seek_device_per_open.asm seek_device_per_open.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; Each open handle owns a file position in its SFT entry, also for character
    ; devices: a second open starts at 0, a duplicated handle shares the entry,
    ; a reopen resets to 0, and writes advance the position.
    mov bx, 0001h
    xor cx, cx
    mov dx, 0007h
    mov ax, 4200h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0007h
    jne failed

    mov dx, con_name
    mov ax, 3D02h
    int 21h
    jc failed
    mov [handle_a], ax

    mov bx, [handle_a]
    xor cx, cx
    xor dx, dx
    mov ax, 4201h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0000h
    jne failed

    mov bx, [handle_a]
    mov ah, 45h
    int 21h
    jc failed
    mov [handle_b], ax

    mov bx, [handle_b]
    xor cx, cx
    mov dx, 0004h
    mov ax, 4200h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0004h
    jne failed

    mov bx, [handle_a]
    xor cx, cx
    xor dx, dx
    mov ax, 4201h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0004h
    jne failed

    mov bx, [handle_a]
    mov cx, 0002h
    mov dx, text
    mov ah, 40h
    int 21h
    jc failed
    cmp ax, 0002h
    jne failed

    mov bx, [handle_b]
    xor cx, cx
    xor dx, dx
    mov ax, 4201h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0006h
    jne failed

    mov bx, [handle_a]
    mov ah, 3Eh
    int 21h
    jc failed

    mov bx, [handle_b]
    xor cx, cx
    xor dx, dx
    mov ax, 4201h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0006h
    jne failed

    mov bx, [handle_b]
    mov ah, 3Eh
    int 21h
    jc failed

    mov dx, con_name
    mov ax, 3D02h
    int 21h
    jc failed
    mov [handle_c], ax

    mov bx, [handle_c]
    xor cx, cx
    xor dx, dx
    mov ax, 4201h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0000h
    jne failed

    mov bx, [handle_c]
    xor cx, cx
    mov dx, 0003h
    mov ax, 4202h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0003h
    jne failed

    mov bx, [handle_c]
    mov ah, 3Eh
    int 21h
    jc failed

    mov dx, aux_name
    mov ax, 3D02h
    int 21h
    jc failed
    mov [handle_aux], ax

    mov bx, [handle_aux]
    mov cx, 0005h
    mov dx, text
    mov ah, 40h
    int 21h
    jc failed
    cmp ax, 0005h
    jne failed

    mov bx, [handle_aux]
    xor cx, cx
    xor dx, dx
    mov ax, 4201h
    int 21h
    jc failed
    cmp dx, 0000h
    jne failed
    cmp ax, 0005h
    jne failed

    mov bx, [handle_aux]
    mov ah, 3Eh
    int 21h
    jc failed

    ; An unknown seek origin is invalid function (error 01h), on any handle.
    mov bx, 0001h
    xor cx, cx
    xor dx, dx
    mov ax, 4203h
    int 21h
    jnc failed
    cmp ax, 0001h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

handle_a dw 0
handle_b dw 0
handle_c dw 0
handle_aux dw 0
con_name db 'CON', 0
aux_name db 'AUX', 0
text db 'xy'
