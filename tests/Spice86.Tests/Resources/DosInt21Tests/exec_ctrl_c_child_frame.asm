; Build: fasm exec_ctrl_c_child_frame.asm exec_ctrl_c_child_frame.com
; A program without 50h/55h EXECs CTRLCD.COM, which stuffs a Ctrl-C key into the BIOS keyboard
; buffer and calls the typed INT 21h/AH=09h service. DOS terminates the child inside the 09h
; call; the resume pops the parent's EXEC frame, so no frame the typed 09h would write is read.
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
result_seg equ 5000h

r_flags3 equ 0
r_ss3 equ 2
r_sp3 equ 4
r_exp_ss3 equ 6
r_exp_sp3 equ 8

start:
    mov bx, 1000h
    mov ah, 4Ah
    int 21h
    jc failed

    mov [param_command_tail+2], cs
    mov [param_fcb1+2], cs
    mov [param_fcb2+2], cs

    ; EXEC frame flags: OF=1 DF=1 ZF=1 PF=1 CF=1.
    mov al, 7Fh
    add al, 7Fh
    mov ah, 45h
    sahf
    std
    mov [exp_sp3], sp
    mov [exp_ss3], ss

    push cs
    pop es
    mov bx, param_block
    mov dx, child_name
    mov ax, 4B00h
    int 21h
    ; control resumes here when the child's Ctrl-C terminate pops the EXEC frame.
    pushf
    pop ax
    mov bx, result_seg
    mov es, bx
    mov [es:r_flags3], ax
    mov ax, ss
    mov [es:r_ss3], ax
    mov [es:r_sp3], sp
    mov ax, [cs:exp_ss3]
    mov [es:r_exp_ss3], ax
    mov ax, [cs:exp_sp3]
    mov [es:r_exp_sp3], ax
    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

exp_ss3 dw 0
exp_sp3 dw 0
saved_ss dw 0
child_name db 'CTRLCD.COM', 0
command_tail db 0, 0Dh
empty_fcb db 0, 11 dup (' '), 0, 0, 0, 0
param_block:
    dw 0
param_command_tail dw command_tail, 0
param_fcb1 dw empty_fcb, 0
param_fcb2 dw empty_fcb, 0
