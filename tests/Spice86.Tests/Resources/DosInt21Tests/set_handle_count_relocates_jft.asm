; Build: fasm set_handle_count_relocates_jft.asm set_handle_count_relocates_jft.com
; AH=67h with more than 20 handles moves the job file table out of the PSP, so handles 20 and up
; neither overwrite the PSP fields after the built-in table nor exceed the requested count.
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh
handle_count equ 30

start:
    mov ah, 62h
    int 21h
    mov [own_psp], bx
    mov es, bx
    mov ax, [es:002Ch]
    mov [environment_segment], ax

    mov ah, 67h
    mov bx, handle_count
    int 21h
    jc failed
    cmp word [es:0032h], handle_count
    jne failed
    mov ax, [es:0036h]
    cmp ax, [own_psp]
    je failed

    mov word [expected_handle], 5
open_loop:
    mov ax, 3D00h
    mov dx, file_a
    int 21h
    jc failed
    push ax
    mov dx, details_port
    out dx, al
    pop ax
    cmp ax, [expected_handle]
    jne failed
    inc word [expected_handle]
    cmp word [expected_handle], handle_count
    jne open_loop

    mov ax, 3D00h
    mov dx, file_a
    int 21h
    jnc failed
    cmp ax, 4
    jne failed

    mov ax, [es:002Ch]
    cmp ax, [environment_segment]
    jne failed

    mov bx, 5
close_loop:
    mov ah, 3Eh
    int 21h
    jc failed
    inc bx
    cmp bx, handle_count
    jne close_loop

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

own_psp dw 0
environment_segment dw 0
expected_handle dw 0
file_a db 'A.TXT', 0
