; Test INT 10h AX=4F02h without checking return value
; This will help us see if INT 10h returns at all
org 0x100

ResultPort equ 0x999

section .text
start:
    ; Set VBE mode 0x100 (640x400x256)
    mov ax, 0x4F02
    mov bx, 0x0100      ; Mode 0x100
    int 0x10
    
    ; Always write success (don't check AX)
    mov al, 0x00        ; TestResult.Success
    mov dx, ResultPort
    out dx, al
    hlt
