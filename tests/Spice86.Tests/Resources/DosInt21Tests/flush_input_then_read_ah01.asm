; Build: nasm -f bin flush_input_then_read_ah01.asm -o flush_input_then_read_ah01.com
; INT 21h AH=0Ch with AL=01h flushes the input buffer, then runs function 01h (read with echo).
; STDIN is redirected to a stream holding 'A', which the keyboard flush leaves alone.
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h
failure equ 0FFh

start:
    mov ax, 0C01h
    int 21h
    mov dx, details_port
    out dx, al
    cmp al, 'A'
    jne failed
    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
