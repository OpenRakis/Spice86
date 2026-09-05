; Build: nasm -f bin read_cd_image_file.asm -o read_cd_image_file.com
bits 16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    mov dx, file_name
    mov ax, 3D00h
    int 21h
    jc failed
    mov bx, ax
    mov cx, 8
    mov dx, buffer
    mov ah, 3Fh
    int 21h
    jc close_failed
    cmp ax, 8
    jne close_failed
    cmp byte [buffer], 'S'
    jne close_failed
    cmp byte [buffer + 1], 'P'
    jne close_failed
    cmp byte [buffer + 2], 'I'
    jne close_failed
    cmp byte [buffer + 3], 'C'
    jne close_failed
    cmp byte [buffer + 4], 'E'
    jne close_failed
    cmp byte [buffer + 5], '8'
    jne close_failed
    cmp byte [buffer + 6], '6'
    jne close_failed
    cmp byte [buffer + 7], '!'
    jne close_failed
    mov al, success
    jmp close_and_report

failed:
    mov al, failure
    jmp report

close_failed:
    mov al, failure

close_and_report:
    push ax
    mov ah, 3Eh
    int 21h
    pop ax

report:
    mov dx, result_port
    out dx, al
    hlt

file_name db 'D:\README.TXT', 0
buffer times 8 db 0
