; Build: fasm file_attributes_readonly.asm file_attributes_readonly.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh
readonly_attribute equ 001h

start:
    ; The archive bit depends on the host file system, so this program checks ReadOnly only and reports
    ; CL as a detail byte for the oracle.
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

file_name db 'TESTATTR.TXT', 0
