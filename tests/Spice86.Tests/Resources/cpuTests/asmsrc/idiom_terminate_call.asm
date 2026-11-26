; Build: fasm idiom_terminate_call.asm ../idiom_terminate_call.com
org 100h
use16

start:
    call finish
    hlt
finish:
    mov ax, 0x4C00
    int 0x21
