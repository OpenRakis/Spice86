; Build: nasm -f bin read_floppy_image_file.asm -o read_floppy_image_file.com
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
    mov cx, 7
    mov dx, buffer
    mov ah, 3Fh
    int 21h
    jc close_failed
    cmp ax, 7
    jne close_failed
    cmp word [buffer], 'FL'
    jne close_failed
    cmp word [buffer + 2], 'OP'
    jne close_failed
    cmp word [buffer + 4], 'PY'
    jne close_failed
    cmp byte [buffer + 6], '!'
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

file_name db 'A:\SUBDIR\HELLO.TXT', 0
buffer times 7 db 0
