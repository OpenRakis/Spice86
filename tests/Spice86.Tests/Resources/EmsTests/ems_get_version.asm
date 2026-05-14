; Build: fasm ems_get_version.asm ems_get_version.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
emm_version_32 equ 032h

start:
    ; INT 67h AH=46h reports LIM EMS 3.2 as AL=32h.
    mov ah, 46h
    int 67h
    cmp ah, 00h
    jne failed
    cmp al, emm_version_32
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
