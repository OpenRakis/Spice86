; Build: fasm stdin_to_video_writer.asm stdin_to_video_writer.com
; Reads 1 byte from stdin (INT 21h AH=3Fh handle 0) and writes it into a text video cell,
; then exits. The test redirects a file as stdin and checks the character the cell holds.
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
    ; Point ES:DI at the receiving cell in the B800h text segment.
    mov ax, 0B800h
    mov es, ax
    mov di, video_offset
    ; Store AL with attribute 07h (light gray on black) in the cell.
    mov al, [read_buffer]      ; the byte read from stdin
    mov ah, 07h
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h

read_buffer:
    db 0                       ; one-byte read buffer
