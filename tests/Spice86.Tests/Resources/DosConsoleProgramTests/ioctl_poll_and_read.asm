; Build: fasm ioctl_poll_and_read.asm ioctl_poll_and_read.com
; Polls IOCTL 06 (Get Input Status) on stdin in a loop and reads a character with INT 21h
; AH=07h as soon as input is available, then writes the read character to video memory.
; This is the real-world CHOICE.COM polling pattern that requires correct IOCTL 06
; behavior, so the test preloads a key and reads the character back from the cell.
; Parameter: video_offset is the byte offset of the receiving cell in the B800h segment.
use16
org 100h

video_offset equ 0h

poll_loop:
    ; Ask DOS whether stdin has input. IOCTL 06h returns AL=00h while it has none.
    mov ah, 44h                ; IOCTL
    mov al, 06h                ; get input status
    xor bx, bx                 ; handle 0 = stdin
    int 21h
    or al, al
    jz poll_loop
read_key:
    ; Read the waiting character without echo into AL.
    mov ah, 07h
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
