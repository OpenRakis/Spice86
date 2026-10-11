; Build: fasm child_exec_terminate_releases_handles.asm child_exec_terminate_releases_handles.com
; Executes CHILD.COM (child_opens_files_without_closing) several times. The child opens files and exits
; without closing them: DOS must release its handles so the parent's next open still returns handle 5.
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh
iterations equ 3

start:
    mov bx, 1000h
    mov ah, 4Ah
    int 21h
    jc failed

    mov [param_command_tail+2], cs
    mov [param_fcb1+2], cs
    mov [param_fcb2+2], cs

    mov cx, iterations
exec_loop:
    push cx
    mov [saved_sp], sp
    mov [saved_ss], ss
    push cs
    pop es
    mov bx, param_block
    mov dx, child_name
    mov ax, 4B00h
    int 21h
    cli
    mov ss, [cs:saved_ss]
    mov sp, [cs:saved_sp]
    sti
    push cs
    pop ds
    jc failed

    mov ah, 4Dh
    int 21h
    mov dx, details_port
    out dx, al
    cmp al, 0
    jne failed

    mov ax, 3D00h
    mov dx, file_a
    int 21h
    jc failed
    push ax
    mov dx, details_port
    out dx, al
    pop ax
    cmp ax, 5
    jne failed
    mov bx, ax
    mov ah, 3Eh
    int 21h
    jc failed

    pop cx
    loop exec_loop

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

saved_sp dw 0
saved_ss dw 0
child_name db 'CHILD.COM', 0
file_a db 'A.TXT', 0
command_tail db 0, 0Dh
empty_fcb db 0, 11 dup (' '), 0, 0, 0, 0
param_block:
    dw 0
param_command_tail dw command_tail, 0
param_fcb1 dw empty_fcb, 0
param_fcb2 dw empty_fcb, 0
