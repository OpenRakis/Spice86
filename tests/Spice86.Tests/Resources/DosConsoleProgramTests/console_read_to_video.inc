; Reads a character with the given INT 21h console input function and writes it into a
; text video cell of segment B800h. The test preloads a BIOS key and compares the cell with
; the character that key carries.
; Parameters: ah_function is the INT 21h input function (01h reads with echo, 07h reads
; without echo, 08h reads without echo; 01h and 08h check for Ctrl-Break), video_offset
; is the byte offset of the receiving cell in the B800h text segment.
use16
org 100h

    ; Read one character from the console with the configured INT 21h function; it lands
    ; in AL.
    mov ah, ah_function
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
