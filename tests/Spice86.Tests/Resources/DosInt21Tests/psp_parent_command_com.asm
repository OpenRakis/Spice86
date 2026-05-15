; Build: fasm psp_parent_command_com.asm psp_parent_command_com.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
command_com_psp equ 0060h

start:
    ; PSP parent chain must point to COMMAND.COM, whose PSP points to itself.
    mov ah, 62h
    int 21h
    mov es, bx
    mov bx, [es:0016h]
    cmp bx, command_com_psp
    jne failed
    mov es, bx
    mov bx, [es:0016h]
    cmp bx, command_com_psp
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
