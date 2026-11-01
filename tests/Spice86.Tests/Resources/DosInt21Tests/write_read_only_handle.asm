; Build: fasm write_read_only_handle.asm write_read_only_handle.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; A file opened read-only (AL=00h) must reject AH=40h writes with CF=1, AX=0005h.
    mov dx, file_name
    mov ax, 3D00h
    int 21h
    jc failed
    mov [file_handle], ax

    mov bx, [file_handle]
    mov cx, 0001h
    mov dx, byte_x
    mov ah, 40h
    int 21h
    jnc close_failed
    cmp ax, 0005h
    jne close_failed

    mov bx, [file_handle]
    mov ah, 3Eh
    int 21h
    jc failed

    ; Reopen read-only and read the 4 bytes back: content must be unchanged.
    mov dx, file_name
    mov ax, 3D00h
    int 21h
    jc failed
    mov [file_handle], ax

    mov bx, [file_handle]
    mov cx, 0004h
    mov dx, buffer
    mov ah, 3Fh
    int 21h
    jc close_failed
    cmp ax, 0004h
    jne close_failed
    cmp word [buffer], 'da'
    jne close_failed
    cmp word [buffer+2], 'ta'
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
byte_x db 'x'
buffer db 00h, 00h, 00h, 00h
file_name db 'RO.TXT', 00h
