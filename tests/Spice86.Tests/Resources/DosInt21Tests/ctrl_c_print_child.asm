; Build: fasm ctrl_c_print_child.asm ctrl_c_print_child.com
; Stuffs a Ctrl-C keystroke (scancode 2Eh, ASCII 03h) into the BIOS keyboard buffer head/tail,
; then calls typed INT 21h/AH=09h. DOS sees the Ctrl-C and terminates this process inside the
; 09h call; the trailing 4Ch is never reached.
use16
org 100h

start:
    mov ax, 40h
    mov es, ax
    cli
    mov word [es:001Ah], 001Eh
    mov word [es:001Ch], 0020h
    mov word [es:001Eh], 2E03h
    sti
    mov ah, 09h
    mov dx, msg
    int 21h
    mov ax, 4C02h
    int 21h

msg db 'X$'
