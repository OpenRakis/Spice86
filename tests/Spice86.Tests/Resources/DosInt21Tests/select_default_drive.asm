; Build: fasm select_default_drive.asm select_default_drive.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh

start:
    ; AH=0Eh selects C:, returns max drive count 26, rejects drive index 26, and keeps C: selected.
    mov ah, 0Eh
    mov dl, 02h
    int 21h
    mov dx, details_port
    out dx, al
    cmp al, 1Ah
    jne failed

    mov ah, 19h
    int 21h
    mov dx, details_port
    out dx, al
    cmp al, 02h
    jne failed

    mov ah, 0Eh
    mov dl, 1Ah
    int 21h
    mov ah, 19h
    int 21h
    mov dx, details_port
    out dx, al
    cmp al, 02h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
