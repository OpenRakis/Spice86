; Build: fasm int20_terminates.asm int20_terminates.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; Write success before invoking the legacy INT 20h termination path.
    mov al, success
    mov dx, result_port
    out dx, al

    ; INT 20h should terminate the program normally, so the failure marker below is never written.
    int 20h

    cmp al, 0
    jne failed
    hlt

failed:
    mov al, failure
    mov dx, result_port
    out dx, al
    hlt
