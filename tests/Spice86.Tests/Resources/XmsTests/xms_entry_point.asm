; Build: fasm xms_entry_point.asm xms_entry_point.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; INT 2Fh AX=4310h returns the XMS entry point in ES:BX.
    mov ax, 4310h
    int 2Fh
    mov ax, es
    or ax, bx
    jz failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
