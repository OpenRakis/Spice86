; Build: fasm environment_block_not_corrupted.asm environment_block_not_corrupted.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
command_com_psp equ 0060h

start:
    ; Environment block starts with BLASTER=... and must not be corrupted by PSP initialization.
    mov ah, 62h
    int 21h
    mov es, bx
    mov ax, [es:002Ch]
    test ax, ax
    jz failed
    mov es, ax
    cmp byte [es:0000h], 'B'
    jne failed
    cmp byte [es:0001h], 'L'
    jne failed
    cmp byte [es:0016h], command_com_psp
    je failed
    cmp byte [es:002Ch], command_com_psp
    je failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
