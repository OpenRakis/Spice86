; Build: fasm move_file_pointer_signed_offset.asm move_file_pointer_signed_offset.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    mov dx, file_name
    mov ax, 3D00h
    int 21h
    jc failed
    mov [file_handle], ax

    mov bx, [file_handle]
    xor cx, cx
    mov dx, 0200h
    mov ax, 4200h
    int 21h
    jc close_failed
    cmp dx, 0000h
    jne close_failed
    cmp ax, 0200h
    jne close_failed

    mov bx, [file_handle]
    mov cx, 0FFFFh
    mov dx, 0FFFFh
    mov ax, 4201h
    int 21h
    jc close_failed
    cmp dx, 0000h
    jne close_failed
    cmp ax, 01FFh
    jne close_failed

    mov bx, [file_handle]
    mov cx, 0001h
    mov dx, buffer
    mov ah, 3Fh
    int 21h
    jc close_failed
    cmp ax, 0001h
    jne close_failed
    cmp byte [buffer], 0FFh
    jne close_failed

    mov al, success
    jmp close_and_write

close_failed:
    mov al, failure

close_and_write:
    push ax
    mov bx, [file_handle]
    mov ah, 3Eh
    int 21h
    pop ax
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

file_handle dw 0000h
buffer db 00h
file_name db 'SEEKTEST.BIN', 00h
