; Build: fasm ems_detection_interrupt_vector.asm ems_detection_interrupt_vector.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; Detection method 1: INT 67h Get Status must return AH=00h.
    mov ah, 40h
    int 67h
    cmp ah, 00h
    jne failed

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt
