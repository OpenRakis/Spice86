; Build: fasm file_attributes_readonly_no_archive.asm file_attributes_readonly_no_archive.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh
readonly_attribute equ 001h

start:
    ; Unix host filesystems do not persist the DOS Archive bit, so this variant checks ReadOnly only.
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
