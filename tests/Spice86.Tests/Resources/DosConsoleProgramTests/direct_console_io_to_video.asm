; Build: fasm direct_console_io_to_video.asm direct_console_io_to_video.com
; Performs INT 21h AH=06h (Direct Console I/O) with DL=FFh, which selects input mode, then
; writes the AL result byte into a text video cell. The test preloads a key for the run
; that expects a character and leaves the buffer empty for the run that expects 00h.
; Parameters: dl_value is the input mode selector passed in DL, video_offset is the byte
; offset of the receiving cell in the B800h text segment.
use16
org 100h

dl_value equ 0FFh
video_offset equ 0h

    ; Run direct console I/O in input mode, which the FFh in DL selects. AL then holds the
    ; waiting character, or 00h while no input is pending.
    mov ah, 06h
    mov dl, dl_value
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
