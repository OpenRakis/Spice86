; Calls a drive based IOCTL subfunction (INT 21h AH=44h with BX holding the 1-based drive
; number) and writes AX and DX to video memory as four consecutive bytes: AL, AH, DL, DH,
; each followed by the 07h attribute byte. The test reads those bytes and compares them
; with the register contract it expects.
; Parameters: ioctl_sub_function is the AL subfunction, drive_number is the 1-based drive
; number passed in BX, video_offset is the byte offset of the first receiving cell in the
; B800h text segment.
use16
org 100h

    ; Ask DOS for the result of the configured drive based IOCTL subfunction.
    mov ah, 44h                ; IOCTL
    mov al, ioctl_sub_function
    mov bx, drive_number
    int 21h
    ; Point ES:DI at the receiving cell in the B800h text segment. The result still sits
    ; in AX and DX, so both are pushed before the setup and popped after it.
    push ax
    push dx
    mov ax, 0B800h
    mov es, ax
    mov di, video_offset
    pop dx
    pop ax
    ; Store AL, the low byte of the result, followed by attribute 07h, in the first cell.
    stosb
    mov al, 07h
    stosb
    ; Store AH, DL and DH, each followed by attribute 07h, in the next three cells.
    db 8Ah, 0C4h               ; mov al, ah (8Ah form of the original image; fasm emits 88h)
    stosb
    mov al, 07h
    stosb
    db 8Ah, 0C2h               ; mov al, dl (8Ah form of the original image; fasm emits 88h)
    stosb
    mov al, 07h
    stosb
    db 8Ah, 0C6h               ; mov al, dh (8Ah form of the original image; fasm emits 88h)
    stosb
    mov al, 07h
    stosb
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
