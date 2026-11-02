; Build: fasm print_string_then_write_sentinel.asm print_string_then_write_sentinel.com
; Writes its dollar terminated string with INT 21h AH=09h, then writes a sentinel byte into
; video cell 0 and returns to the caller. The string data follows the code and ends with a
; trailing zero after the dollar, which is part of the program image. If AH=09h checks the
; standard input for a pending break and triggers INT 23h, the sentinel write is never
; reached, and the test checks that the cell does not hold the sentinel.
; Parameters: message is the text written by AH=09h, sentinel is the byte written to cell 0
; when the call returns normally.
use16
org 100h

message equ 'HELLO'
sentinel equ 'X'

    ; Print the message up to its '$' terminator with AH=09h. A pending Ctrl-Break makes
    ; the call terminate the program through INT 23h, so the sentinel write never runs.
    mov dx, message_data
    mov ah, 09h                ; print string
    int 21h
    ; Store AL with attribute 07h in the receiving cell of the B800h text segment. AL is
    ; the sentinel that marks a normal return of the call.
    mov al, sentinel
    mov ah, 07h
    mov bx, 0B800h
    mov es, bx
    mov di, 0
    stosw
    ; Return to DOS through the INT 20h at the start of the PSP.
    ret

message_data:
    db message, '$', 0 ; text, the '$' terminator of AH=09h, and one zero byte of the original image
