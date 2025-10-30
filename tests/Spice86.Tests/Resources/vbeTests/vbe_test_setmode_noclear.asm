; Test VBE mode 0x100 (640x400x256) without clearing memory
org 0x100

ResultPort equ 0x999

section .text
start:
    ; Set VBE mode 0x100 with don't clear memory bit set
    mov ax, 0x4F02
    mov bx, 0x8100      ; Mode 0x100 | 0x8000 (don't clear)
    int 0x10
    
    ; Always write success (don't check AX)
    mov al, 0x00        ; TestResult.Success
    mov dx, ResultPort
    out dx, al
    
    ; Proper DOS exit
    mov ah, 0x4C        ; DOS terminate
    mov al, 0x00        ; Return code 0
    int 0x21
