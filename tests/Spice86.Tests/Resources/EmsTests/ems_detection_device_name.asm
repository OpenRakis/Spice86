; Build: fasm ems_detection_device_name.asm ems_detection_device_name.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; Detection method 2: EMMXXXX0 must be openable as a DOS device.
    push cs
    pop ds
    mov ax, 3D00h
    mov dx, emm_device_name
    int 21h
    jc failed
    mov bx, ax
    mov ax, 3E00h
    int 21h

    mov al, success
    jmp write_result

failed:
    mov al, failure

write_result:
    mov dx, result_port
    out dx, al
    hlt

emm_device_name db 'EMMXXXX0', 0
