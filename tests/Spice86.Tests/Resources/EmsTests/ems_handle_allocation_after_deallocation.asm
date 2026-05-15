; Build: fasm ems_handle_allocation_after_deallocation.asm ems_handle_allocation_after_deallocation.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
page_frame_segment equ 0E000h
data_marker equ 0AAh

start:
    ; Deallocating one handle must not corrupt data mapped through another active handle.
    mov bx, 4
    mov ah, 43h
    int 67h
    cmp ah, 00h
    jne failed
    mov di, dx

    mov bx, 4
    mov ah, 43h
    int 67h
    cmp ah, 00h
    jne failed
    mov si, dx

    mov al, 0
    mov bx, 0
    mov dx, si
    mov ah, 44h
    int 67h
    cmp ah, 00h
    jne failed

    mov ax, page_frame_segment
    mov es, ax
    mov byte [es:0], data_marker

    mov dx, di
    mov ah, 45h
    int 67h
    cmp ah, 00h
    jne failed

    mov al, [es:0]
    cmp al, data_marker
    je succeeded

failed:
    mov al, failure
    jmp write_result

succeeded:
    mov al, success

write_result:
    mov dx, result_port
    out dx, al
    hlt
