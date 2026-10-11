; Build: fasm child_psp_inherits_jft_slots.asm child_psp_inherits_jft_slots.com
; A child PSP created with AH=55h shares the parent's system file table entries in the same JFT slots,
; and the child's next open returns the next free slot of its own JFT.
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh
scratch_psp_segment equ 2000h
jft_offset equ 0018h

start:
    mov ah, 62h
    int 21h
    mov [own_psp], bx

    mov dx, file_a
    call open_file
    cmp ax, 5
    jne failed
    mov dx, file_b
    call open_file
    cmp ax, 6
    jne failed
    mov dx, file_c
    call open_file
    cmp ax, 7
    jne failed

    mov dx, scratch_psp_segment
    mov si, 0010h
    mov ah, 55h
    int 21h
    mov bx, scratch_psp_segment
    mov ah, 50h
    int 21h

    mov si, 0
compare_jft:
    mov es, [own_psp]
    mov al, [es:jft_offset+si]
    mov bx, scratch_psp_segment
    mov es, bx
    cmp al, [es:jft_offset+si]
    jne failed
    inc si
    cmp si, 8
    jne compare_jft

    mov bx, 5
    mov cx, 1
    mov dx, read_buffer
    mov ah, 3Fh
    int 21h
    jc failed
    cmp ax, 1
    jne failed
    cmp byte [read_buffer], 'A'
    jne failed

    mov dx, file_d
    call open_file
    cmp ax, 8
    jne failed
    mov bx, ax
    mov ah, 3Eh
    int 21h
    jc failed

    mov bx, 5
close_child_loop:
    mov ah, 3Eh
    int 21h
    jc failed
    inc bx
    cmp bx, 8
    jne close_child_loop

    mov bx, [own_psp]
    mov ah, 50h
    int 21h

    mov bx, 5
close_loop:
    mov ah, 3Eh
    int 21h
    jc failed
    inc bx
    cmp bx, 8
    jne close_loop

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
    jnc open_done
    mov ax, 0FFFFh
open_done:
    push ax
    push dx
    mov dx, details_port
    out dx, al
    pop dx
    pop ax
    ret

own_psp dw 0
read_buffer db 0
file_a db 'A.TXT', 0
file_b db 'B.TXT', 0
file_c db 'C.TXT', 0
file_d db 'D.TXT', 0
