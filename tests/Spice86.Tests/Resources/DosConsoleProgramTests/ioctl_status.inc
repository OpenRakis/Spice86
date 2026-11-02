; Calls IOCTL on the given handle (INT 21h AH=44h), then writes the AL result byte into a
; text video cell. AL=0xFF means input available or output ready, AL=0x00 means neither.
; Parameters: ioctl_sub_function is the AL subfunction, handle is the BX handle, and
; video_offset is the byte offset of the receiving cell in the B800h text segment.
use16
org 100h

    ; Ask DOS for the status of the configured IOCTL subfunction on the given handle.
    mov ah, 44h                ; IOCTL
    mov al, ioctl_sub_function
    mov bx, handle
    int 21h
    ; Store AL with attribute 07h in the receiving cell of the B800h text segment. AL is
    ; the status byte the call returned.
    mov ah, 07h
    mov bx, 0B800h
    mov es, bx
    mov di, video_offset
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
