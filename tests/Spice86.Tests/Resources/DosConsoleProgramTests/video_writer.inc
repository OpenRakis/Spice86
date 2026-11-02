; Writes one character with attribute 07h into a text video cell of segment B800h, then
; exits through INT 21h AH=4Ch. The test reads the video cell back and compares the
; character with the value its scenario expects.
; Parameters: character is the byte stored in the cell, video_offset is the byte offset of
; the cell inside the B800h text segment (cell n sits at byte offset 2n).
use16
org 100h

    ; Point ES:DI at the receiving cell in the B800h text segment.
    mov ax, 0B800h
    mov es, ax
    mov di, video_offset
    ; Store the character with attribute 07h (light gray on black) in the cell.
    mov al, character
    mov ah, 07h
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
