; Build: fasm set_file_attributes_roundtrip.asm set_file_attributes_roundtrip.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh
readonly_attribute equ 001h
archive_attribute equ 020h

start:
    ; AH=43h AL=1 sets ReadOnly + Archive on SETATR.TXT.
    mov dx, file_name
    mov cx, readonly_attribute or archive_attribute
    mov ax, 4301h
    int 21h
    jc failed

    ; Reload DS:DX before AH=43h AL=0 because later diagnostics use DX for I/O ports.
    mov dx, file_name
    mov ax, 4300h
    int 21h
    jc failed

    mov al, cl
    mov dx, details_port
    out dx, al
    test cl, readonly_attribute
    jz failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

file_name db 'SETATR.TXT', 0
