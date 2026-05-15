; Build: fasm device_status_report.asm device_status_report.com
use16
org 100h

result_port equ 0999h
details_port equ 0998h
success equ 00h

start:
    ; Position cursor at row 4, col 9. ESC[6n should return ESC[5;10R followed by CR.
    mov ah, 02h
    mov bh, 00h
    mov dh, 04h
    mov dl, 09h
    int 10h

    ; Write ESC[6n through DOS output.
    mov ah, 02h
    mov dl, 1Bh
    int 21h
    mov dl, '['
    int 21h
    mov dl, '6'
    int 21h
    mov dl, 'n'
    int 21h

    ; Read the 8-byte DSR response through DOS and send each byte to DetailsPort.
    mov cx, 8
read_loop:
    mov ah, 08h
    int 21h
    mov dx, details_port
    out dx, al
    loop read_loop

    mov al, success
    mov dx, result_port
    out dx, al
    hlt
