; Build: fasm exec_nested_child.asm exec_nested_child.com
; The child of the nested-EXEC fixture: EXECs GRAND.COM, stores its resume marker to
; 5000:0034 once the grandchild's terminate lands back here, then terminates so control
; resumes in the parent.
use16
org 100h

result_seg equ 5000h

start:
    mov bx, 1000h
    mov ah, 4Ah
    int 21h

    mov [param_command_tail+2], cs
    mov [param_fcb1+2], cs
    mov [param_fcb2+2], cs

    push cs
    pop es
    mov bx, param_block
    mov dx, grand_name
    mov ax, 4B00h
    int 21h
    ; control resumes here when the grandchild terminates.
    mov ax, result_seg
    mov es, ax
    mov word [es:0034h], 0CAFEh
    int 20h

grand_name db 'GRAND.COM', 0
command_tail db 0, 0Dh
empty_fcb db 0, 11 dup (' '), 0, 0, 0, 0
param_block:
    dw 0
param_command_tail dw command_tail, 0
param_fcb1 dw empty_fcb, 0
param_fcb2 dw empty_fcb, 0
