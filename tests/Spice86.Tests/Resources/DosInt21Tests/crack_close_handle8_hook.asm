; Build: fasm crack_close_handle8_hook.asm crack_close_handle8_hook.com
; Reproduces the Alone in the Dark TRSI loader: a resident INT 21h hook counts close(BX=8) and patches
; the protection check from the 19th one. The game runs in a child PSP (AH=55h + AH=50h) with three
; inherited files open, so the file it reopens again and again must get handle 8.
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh
scratch_psp_segment equ 2000h
churn_count equ 19

start:
    mov ah, 62h
    int 21h
    mov [own_psp], bx

    mov ax, 3521h
    int 21h
    mov word [old_int21], bx
    mov word [old_int21+2], es
    mov dx, close_hook
    mov ax, 2521h
    int 21h

    mov dx, file_a
    call open_file
    jc failed
    mov dx, file_b
    call open_file
    jc failed
    mov dx, file_c
    call open_file
    jc failed

    mov dx, scratch_psp_segment
    mov si, 0010h
    mov ah, 55h
    int 21h
    mov bx, scratch_psp_segment
    mov ah, 50h
    int 21h

    mov cx, churn_count
churn:
    push cx
    mov dx, file_d
    call open_file
    pop cx
    jc failed
    mov bx, ax
    mov ah, 3Eh
    int 21h
    jc failed
    loop churn

    call close_inherited_files
    mov bx, [own_psp]
    mov ah, 50h
    int 21h
    call close_inherited_files

    push ds
    lds dx, [old_int21]
    mov ax, 2521h
    int 21h
    pop ds

    mov al, [close8_count]
    mov dx, details_port
    out dx, al
    cmp byte [close8_count], churn_count
    jne failed
    cmp byte [patched_byte], 90h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

open_file:
    mov ax, 3D00h
    int 21h
    ret

close_inherited_files:
    mov bx, 5
close_inherited_loop:
    mov ah, 3Eh
    int 21h
    inc bx
    cmp bx, 8
    jne close_inherited_loop
    ret

close_hook:
    cmp ah, 3Eh
    jne chain
    cmp bx, 8
    jne chain
    inc byte [cs:close8_count]
    cmp byte [cs:close8_count], 13h
    jl chain
    mov byte [cs:patched_byte], 90h
chain:
    jmp dword [cs:old_int21]

own_psp dw 0
old_int21 dd 0
close8_count db 0
patched_byte db 75h
file_a db 'A.TXT', 0
file_b db 'B.TXT', 0
file_c db 'C.TXT', 0
file_d db 'D.TXT', 0
