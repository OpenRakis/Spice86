; Terminates through INT 21h AH=4Ch with the configured exit code. The test checks the
; code the DOS process manager reports for the child process.
; Parameter: exit_code is the byte handed back to DOS in AL.
use16
org 100h

    ; Terminate with the configured return code in AL.
    mov ax, 4C00h or exit_code
    int 21h
