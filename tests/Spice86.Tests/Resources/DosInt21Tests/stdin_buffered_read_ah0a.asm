; Build: fasm stdin_buffered_read_ah0a.asm stdin_buffered_read_ah0a.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh
buffer_offset equ 0080h

start:
    ; AH=0Ah reads a DOS line buffer from redirected STDIN. The CR is excluded from read count.
    mov byte [buffer_offset], 20
    mov byte [buffer_offset + 1], 0
    mov dx, buffer_offset
    mov ah, 0Ah
    int 21h

    mov al, [buffer_offset + 1]
    mov dx, details_port
    out dx, al
    cmp al, 5
    jne failed

    mov al, [buffer_offset + 2]
    mov dx, details_port
    out dx, al
    cmp al, 'H'
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
