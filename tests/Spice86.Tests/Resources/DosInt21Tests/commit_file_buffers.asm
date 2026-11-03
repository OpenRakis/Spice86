; Build: fasm commit_file_buffers.asm commit_file_buffers.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
duplicate_handle equ 10
invalid_handle equ 0FFFFh
invalid_handle_error equ 6

start:
    push cs
    pop ds
    mov bx, invalid_handle
    mov ah, 68h
    clc
    int 21h
    jnc failed
    cmp ax, invalid_handle_error
    jne failed

    mov ah, 3Ch
    xor cx, cx
    mov dx, file_name
    int 21h
    jc failed
    mov [original_handle], ax
    mov bx, ax
    mov ah, 40h
    mov cx, payload_size
    mov dx, payload
    int 21h
    jc failed
    cmp ax, payload_size
    jne failed

    mov ah, 46h
    mov cx, duplicate_handle
    int 21h
    jc failed
    mov ah, 3Eh
    int 21h
    jc failed

    mov bx, duplicate_handle
    mov ah, 68h
    stc
    int 21h
    jc failed

    mov ax, 4201h
    xor cx, cx
    xor dx, dx
    int 21h
    jc failed
    cmp ax, payload_size
    jne failed
    or dx, dx
    jne failed
    mov ax, 4200h
    int 21h
    jc failed
    mov ah, 3Fh
    mov cx, payload_size
    mov dx, read_buffer
    int 21h
    jc failed
    cmp ax, payload_size
    jne failed
    mov ax, word [read_buffer]
    cmp ax, word [payload]
    jne failed
    mov ax, word [read_buffer+2]
    cmp ax, word [payload+2]
    jne failed
    mov ah, 3Eh
    int 21h
    jc failed

    mov ah, 68h
    clc
    int 21h
    jnc failed
    cmp ax, invalid_handle_error
    jne failed
    mov bx, [original_handle]
    mov ah, 68h
    int 21h
    jnc failed
    cmp ax, invalid_handle_error
    jne failed
    mov bx, 1
    mov ah, 68h
    stc
    int 21h
    jc failed
    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

original_handle dw 0
file_name db 'COMMIT.DAT', 0
payload db 'DATA'
payload_size = $-payload
read_buffer rb payload_size
