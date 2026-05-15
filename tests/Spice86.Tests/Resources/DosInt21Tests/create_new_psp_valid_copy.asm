; Build: fasm create_new_psp_valid_copy.asm create_new_psp_valid_copy.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
scratch_psp_segment equ 2000h

start:
    ; AH=26h creates a new PSP by copying the current PSP into the requested segment.
    mov ah, 62h
    int 21h
    mov bp, bx
    mov es, bx
    mov di, [es:002Ch]
    mov dx, scratch_psp_segment
    mov ah, 26h
    int 21h

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
    cmp word [es:000Ah], 0
    je failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
