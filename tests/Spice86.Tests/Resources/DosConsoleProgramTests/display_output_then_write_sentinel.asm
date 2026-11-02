; Build: fasm display_output_then_write_sentinel.asm display_output_then_write_sentinel.com
; Calls INT 21h AH=02h (Display Output) with the given character in DL, then writes a
; sentinel byte into video cell 0, then exits. If AH=02h performs a Ctrl-C/Ctrl-Break
; check and triggers INT 23h, the sentinel write is never reached, and the test checks
; that the cell does not hold the sentinel.
; Parameters: output_character is the character passed to AH=02h in DL, sentinel is the
; byte written to cell 0 when the call returns normally.
use16
org 100h

output_character equ 'Q'
sentinel equ 'X'

    ; Display the configured character with AH=02h. A pending Ctrl-Break makes the call
    ; terminate the program through INT 23h, so the sentinel write below never runs.
    mov dl, output_character
    mov ah, 02h
    int 21h
    ; Store AL with attribute 07h in the receiving cell of the B800h text segment. AL is
    ; the sentinel that marks a normal return of the call.
    mov al, sentinel
    mov ah, 07h
    mov bx, 0B800h
    mov es, bx
    mov di, 0
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
