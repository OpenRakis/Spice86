; Build: fasm file_attributes_readonly.asm file_attributes_readonly.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh
readonly_attribute equ 001h
archive_attribute equ 020h

start:
    ; AH=43h AL=0 gets TESTATTR.TXT attributes from DS:DX.
    mov dx, file_name
    mov ax, 4300h
    int 21h
    jc failed

    ; Report CL for diagnostics, then require Archive and ReadOnly bits.
    mov al, cl
    mov dx, details_port
    out dx, al
    test cl, archive_attribute
    jz failed
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

file_name db 'TESTATTR.TXT', 0
