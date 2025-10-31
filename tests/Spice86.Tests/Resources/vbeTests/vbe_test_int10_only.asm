; Minimal test - just call INT 10h and exit
org 0x100

ResultPort equ 0x999

section .text
start:
    ; Just call INT 10h (any function)
    mov ax, 0x4F00      ; VBE Get Controller Info
    mov es, 0x2000
    mov di, 0x0000
    int 0x10
    
    ; Write success
    mov al, 0x00
    mov dx, ResultPort
    out dx, al
    
    ; Exit
    mov ah, 0x4C
    mov al, 0x00
    int 0x21
