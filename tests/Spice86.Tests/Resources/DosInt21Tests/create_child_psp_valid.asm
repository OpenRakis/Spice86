; Build: fasm create_child_psp_valid.asm create_child_psp_valid.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
scratch_psp_segment equ 2000h

start:
    ; AH=55h creates a child PSP while leaving the current PSP as the parent.
    mov ah, 62h
    int 21h
    mov bp, bx
    mov es, bx
    mov di, [es:002Ch]
    mov dx, scratch_psp_segment
    mov si, 0010h
    mov ah, 55h
    int 21h
    cmp al, 0F0h
    jne failed

    mov ah, 62h
    int 21h
    cmp bx, bp
    jne failed

    mov ax, scratch_psp_segment
    mov es, ax
    cmp byte [es:0000h], 0CDh
    jne failed
    cmp byte [es:0001h], 020h
    jne failed
    cmp word [es:0016h], bp
    jne failed
    cmp word [es:002Ch], di
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
