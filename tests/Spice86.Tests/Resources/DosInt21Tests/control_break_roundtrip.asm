; Build: fasm control_break_roundtrip.asm control_break_roundtrip.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; AH=33h get/set Control-Break flag round-trips ON then OFF.
    mov ah, 33h
    mov al, 01h
    mov dl, 01h
    int 21h

    mov ah, 33h
    mov al, 00h
    int 21h
    cmp dl, 01h
    jne failed

    mov ah, 33h
    mov al, 01h
    mov dl, 00h
    int 21h

    mov ah, 33h
    mov al, 00h
    int 21h
    cmp dl, 00h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
