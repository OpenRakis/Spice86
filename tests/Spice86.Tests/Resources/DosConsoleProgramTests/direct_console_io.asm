; Build: fasm direct_console_io.asm direct_console_io.com
; Performs INT 21h AH=06h (Direct Console I/O) in output mode with a fixed character, then
; exits. The test redirects stdout and checks that the character reached the output file.
; Parameter: dl_value is the character handed to DOS in DL. DL must not be 0FFh here,
; because 0FFh selects input mode instead of output mode.
use16
org 100h

dl_value equ 'Q'

    ; Run direct console I/O with the configured character in DL. A DL of FFh would select
    ; input mode instead of writing to standard output.
    mov ah, 06h
    mov dl, dl_value
    int 21h
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
