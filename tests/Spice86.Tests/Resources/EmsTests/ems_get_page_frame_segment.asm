; Build: fasm ems_get_page_frame_segment.asm ems_get_page_frame_segment.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
page_frame_segment equ 0E000h

start:
    ; INT 67h AH=41h returns the EMS page frame segment in BX.
    mov ah, 41h
    int 67h
    cmp ah, 00h
    jne failed
    cmp bx, page_frame_segment
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
