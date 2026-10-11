; Build: fasm terminate_after_create_child_psp_restores_parent_stack.asm terminate_after_create_child_psp_restores_parent_stack.com
; DOS saves the caller SS:SP in the current PSP on every INT 21h. A process created with AH=55h and made
; current with AH=50h, then terminated with AH=4Ch, must resume the parent at the child's INT 22h address
; with the parent's stack as of its last INT 21h call, without touching the interrupt vector table.
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
scratch_psp_segment equ 2000h

start:
    mov ah, 62h
    int 21h
    mov [own_psp], bx

    xor ax, ax
    mov es, ax
    mov ax, [es:0000h]
    mov word [ivt0], ax
    mov ax, [es:0002h]
    mov word [ivt0+2], ax

    mov dx, scratch_psp_segment
    mov si, 0010h
    mov [expected_ss], ss
    mov [expected_sp], sp
    mov ah, 55h
    int 21h

    mov ah, 62h
    int 21h
    cmp bx, scratch_psp_segment
    jne failed

    mov bx, scratch_psp_segment
    mov ah, 50h
    int 21h

    mov ax, scratch_psp_segment
    mov es, ax
    mov word [es:000Ah], resumed
    mov [es:000Ch], cs
    mov ax, 4C00h
    int 21h
    jmp failed

resumed:
    push cs
    pop ds
    mov ax, ss
    cmp ax, [expected_ss]
    jne failed
    cmp sp, [expected_sp]
    jne failed

    mov ah, 62h
    int 21h
    cmp bx, [own_psp]
    jne failed

    xor ax, ax
    mov es, ax
    mov ax, [es:0000h]
    cmp ax, word [ivt0]
    jne failed
    mov ax, [es:0002h]
    cmp ax, word [ivt0+2]
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

own_psp dw 0
expected_ss dw 0
expected_sp dw 0
ivt0 dd 0
