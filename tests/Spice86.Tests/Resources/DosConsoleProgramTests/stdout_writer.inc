; Writes the message to standard output with INT 21h AH=40h, then exits. The test runs the
; program with a redirected output file and compares the file content with the message.
; Parameter: message is the ASCII text written to stdout.
use16
org 100h

    ; Write the message to standard output with AH=40h.
    mov bx, 1                  ; handle 1 = stdout
    mov dx, message_data
    mov cx, message_end - message_data
    mov ah, 40h                ; write to file or device
    int 21h
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h

message_data:
    db message                  ; text written to stdout
message_end:
