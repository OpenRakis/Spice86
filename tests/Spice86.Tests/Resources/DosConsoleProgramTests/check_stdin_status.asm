; Build: fasm check_stdin_status.asm check_stdin_status.com
; Calls INT 21h AH=0Bh (Check Standard Input Status) and writes the AL result byte into a
; text video cell. AL=0xFF means a keystroke is pending, AL=0x00 means the buffer is empty.
; The test preloads a key and checks that the cell reports the pending keystroke.
; Parameter: video_offset is the byte offset of the receiving cell in the B800h segment.
use16
org 100h

video_offset equ 0h

    ; Ask DOS whether stdin has a keystroke pending: AL comes back FFh when one waits and
    ; 00h when the buffer is empty.
    mov ah, 0Bh
    int 21h
    ; Store AL with attribute 07h in the receiving cell of the B800h text segment.
    mov ah, 07h
    mov bx, 0B800h
    mov es, bx
    mov di, video_offset
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
