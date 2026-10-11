; Build: fasm file_handles_are_lowest_free_jft_slot.asm file_handles_are_lowest_free_jft_slot.com
; DOS handles are indexes in the current PSP job file table (JFT), not system file table indexes.
; Creating a child PSP takes references on the inherited entries but must not shift the parent's handle numbers.
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

    mov dx, scratch_psp_segment
    mov si, 0010h
    mov ah, 55h
    int 21h
    mov bx, [own_psp]
    mov ah, 50h
    int 21h

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

    mov es, [own_psp]
    cmp byte [es:jft_offset+5], 0FFh
    je failed
    cmp byte [es:jft_offset+6], 0FFh
    je failed
    cmp byte [es:jft_offset+7], 0FFh
    je failed

    ; Closing a handle frees its JFT slot, which is reused by the next open.
    mov bx, 6
    mov ah, 3Eh
    int 21h
    jc failed
    cmp byte [es:jft_offset+6], 0FFh
    jne failed
    mov dx, file_d
    call open_file
    cmp ax, 6
    jne failed

    ; AH=45h returns the lowest free slot, sharing the system file table entry.
    mov bx, 5
    mov ah, 45h
    int 21h
    jc failed
    cmp ax, 8
    jne failed
    mov al, [es:jft_offset+5]
    cmp al, [es:jft_offset+8]
    jne failed

    ; AH=46h onto an open handle closes it and makes it share the source entry.
    mov bx, 5
    mov cx, 7
    mov ah, 46h
    int 21h
    jc failed
    mov al, [es:jft_offset+5]
    cmp al, [es:jft_offset+7]
    jne failed

    mov bx, 5
close_loop:
    mov ah, 3Eh
    int 21h
    jc failed
    inc bx
    cmp bx, 9
    jne close_loop

    mov dx, file_a
    call open_file
    cmp ax, 5
    jne failed
    mov bx, ax
    mov ah, 3Eh
    int 21h
    jc failed

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
file_a db 'A.TXT', 0
file_b db 'B.TXT', 0
file_c db 'C.TXT', 0
file_d db 'D.TXT', 0
