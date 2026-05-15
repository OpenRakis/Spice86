; Build: fasm dbcs_table_private_area.asm dbcs_table_private_area.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh
dos_private_tables_start equ 0C800h
dos_private_tables_end equ 0D000h

start:
    ; The DBCS table returned by INT 21h AH=63h lives in C800h-D000h.
    mov ax, 6300h
    int 21h
    mov dx, ds
    cmp dx, dos_private_tables_start
    jb failed
    cmp dx, dos_private_tables_end
    jae failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
