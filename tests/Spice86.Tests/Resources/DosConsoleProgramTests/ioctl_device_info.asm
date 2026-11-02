; Build: fasm ioctl_device_info.asm ioctl_device_info.com
; Calls IOCTL Get Device Information (INT 21h AH=44h AL=00h) on the given handle, then
; writes DL (low byte of the device info) into a text video cell. The test checks that
; stdin reports the character device bit.
; Parameters: handle is the BX handle, video_offset is the byte offset of the receiving
; cell in the B800h text segment.
use16
org 100h

handle equ 0h
video_offset equ 0h

    ; Ask DOS for the device information of the configured handle; DX then holds it.
    mov ah, 44h                ; IOCTL
    mov al, 00h                ; get device information
    mov bx, handle
    int 21h
    ; Store AL with attribute 07h in the receiving cell of the B800h text segment.
    mov al, dl                 ; low byte of the device information
    mov ah, 07h
    mov bx, 0B800h
    mov es, bx
    mov di, video_offset
    stosw
    ; Terminate with return code 0.
    mov ax, 4C00h
    int 21h
