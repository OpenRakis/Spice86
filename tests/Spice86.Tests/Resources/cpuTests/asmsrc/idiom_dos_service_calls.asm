; Build: fasm idiom_dos_service_calls.asm ../idiom_dos_service_calls.com
org 100h
use16

start:
    mov ah, 0x09
    mov dx, msg
    int 0x21
    mov ax, 0x3D00
    mov dx, fname
    int 0x21
    mov ah, 0x30
    int 0x21
    mov ah, 0x4C
    int 0x21
msg: db 'HI$'
fname: db 'NOFILE.TXT', 0
