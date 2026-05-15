; Build: fasm ems_memory_access_mapped_pages.asm ems_memory_access_mapped_pages.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
page_frame_segment equ 0E000h

start:
    ; Allocate and map EMS memory, then verify reads and writes through E000:0000.
    mov bx, 4
    mov ah, 43h
    int 67h
    cmp ah, 00h
    jne failed
    mov cx, dx

    mov al, 0
    mov bx, 0
    mov dx, cx
    mov ah, 44h
    int 67h
    cmp ah, 00h
    jne failed

    mov ax, page_frame_segment
    mov es, ax
    mov byte [es:0], 42h
    mov al, [es:0]
    cmp al, 42h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
