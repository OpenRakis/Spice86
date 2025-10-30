; Test that VBE mode 0x101 (640x480x256) is set successfully
; Uses don't clear memory bit for faster execution
org 0x100

ResultPort equ 0x999

section .text
start:
    ; Set VBE mode 0x101 (640x480x256) without clearing memory
    mov ax, 0x4F02
    mov bx, 0x8101      ; Mode 0x101 | 0x8000 (don't clear memory)
    int 0x10
    
    ; Check return value
    cmp ax, 0x004F
    jne failure
    
success:
    mov al, 0x00        ; TestResult.Success
    jmp writeResult
    
failure:
    mov al, 0xFF        ; TestResult.Failure
    
writeResult:
    mov dx, ResultPort
    out dx, al
    hlt
