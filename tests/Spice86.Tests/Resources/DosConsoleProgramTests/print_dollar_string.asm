; Build: fasm print_dollar_string.asm print_dollar_string.com
; Prints its dollar terminated string with INT 21h AH=09h and then returns to the caller.
; The test redirects stdout and checks that the output file contains the message, which
; AH=09h writes up to the dollar.
; Parameter: message is the text printed by AH=09h. The string data sits immediately after
; the code and is terminated by the dollar byte.
use16
org 100h

message equ 'Hello DOS'

    ; Print the message up to its '$' terminator with AH=09h.
    mov dx, message_data
    mov ah, 09h                ; print string
    int 21h
    ; Return to DOS through the INT 20h at the start of the PSP.
    ret

message_data:
    db message, '$'            ; text and the '$' terminator of AH=09h
