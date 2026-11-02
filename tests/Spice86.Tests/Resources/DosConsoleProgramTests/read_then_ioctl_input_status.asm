; Build: fasm read_then_ioctl_input_status.asm read_then_ioctl_input_status.com
; Reads 1 byte from stdin (INT 21h AH=3Fh handle 0), then calls IOCTL 06 on stdin and
; writes the IOCTL result into a text video cell. The test redirects a file and uses this
; program to check IOCTL 06 on a file handle that reached EOF.
; Parameter: video_offset is the byte offset of the receiving cell in the B800h segment.
; The 1-byte read buffer sits after the code, so the program carries one trailing zero byte.
use16
org 100h

video_offset equ 0h

    ; Read one byte from stdin into the buffer after the code.
    mov bx, 0                  ; handle 0 = stdin
    mov dx, read_buffer
    mov cx, 1
    mov ah, 3Fh                ; read from file or device
    int 21h
    ; Ask DOS for the input status of stdin now that the read consumed one byte.
    mov ah, 44h                ; IOCTL
    mov al, 06h                ; get input status
    xor bx, bx                 ; handle 0 = stdin
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

read_buffer:
    db 0                       ; one-byte read buffer
