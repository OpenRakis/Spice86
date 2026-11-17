; Build: fasm exec_nested_parent.asm exec_nested_parent.com
; The parent of the nested-EXEC fixture: EXECs CHILD.COM (which itself EXECs GRAND.COM),
; stores its resume marker to 5000:0038 once the child's terminate lands back here,
; reports success on the result port and halts.
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
result_seg equ 5000h

start:
    mov bx, 1000h
    mov ah, 4Ah
    int 21h
    jc failed

    mov [param_command_tail+2], cs
    mov [param_fcb1+2], cs
    mov [param_fcb2+2], cs

    push cs
    pop es
    mov bx, param_block
    mov dx, child_name
    mov ax, 4B00h
    int 21h
    ; control resumes here when the child terminates.
    mov ax, result_seg
    mov es, ax
    mov word [es:0038h], 01234h
    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

child_name db 'CHILD.COM', 0
command_tail db 0, 0Dh
empty_fcb db 0, 11 dup (' '), 0, 0, 0, 0
param_block:
    dw 0
param_command_tail dw command_tail, 0
param_fcb1 dw empty_fcb, 0
param_fcb2 dw empty_fcb, 0
