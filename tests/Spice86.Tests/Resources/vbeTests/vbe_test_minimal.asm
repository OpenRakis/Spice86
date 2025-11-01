; Minimal test - just call INT 10h AX=4F02h and halt
org 0x100

section .text
start:
    ; Set VBE mode 0x100
    mov ax, 0x4F02
    mov bx, 0x8100      ; Mode 0x100 | 0x8000 (don't clear)
    int 0x10
    
    ; If we get here, write success
    mov dx, 0x0999
    mov al, 0x00
    out dx, al
    
    ; Halt
    hlt
