; Build: fasm check_stdin_status_ctrl_break.asm check_stdin_status_ctrl_break.com
use16
org 100h

result_port equ 0999h
success equ 00h
failure equ 0FFh

start:
    ; Enable Control-Break checking with AH=33h AL=01h DL=01h.
    mov ah, 33h
    mov al, 01h
    mov dl, 01h
    int 21h

    ; If INT 23h fires later, the process terminates and Failure is never written.
    mov al, success
    mov dx, result_port
    out dx, al

    ; Spin long enough for the injected keyboard event to pass through the hardware pipeline.
    mov cx, 1000
.spin:
    loop .spin

    ; With break ON and Ctrl-C/Ctrl-Break in the buffer, AH=0Bh should invoke INT 23h.
    mov ah, 0Bh
    int 21h

    mov al, failure
    mov dx, result_port
    out dx, al
    hlt
