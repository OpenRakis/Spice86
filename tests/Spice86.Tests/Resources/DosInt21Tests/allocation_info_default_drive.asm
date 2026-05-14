; Build: fasm allocation_info_default_drive.asm allocation_info_default_drive.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; AH=1Ch for default C: returns DOSBox-style allocation info and media-id pointer.
    mov ah, 1Ch
    mov dl, 00h
    int 21h
    cmp al, 0FFh
    je failed
    cmp ah, 00h
    jne failed
    cmp al, 20h
    jne failed
    cmp cx, 0200h
    jne failed
    cmp dx, 7FFDh
    jne failed
    cmp bx, 0029h
    jne failed
    mov si, bx
    cmp byte [si], 0F8h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
