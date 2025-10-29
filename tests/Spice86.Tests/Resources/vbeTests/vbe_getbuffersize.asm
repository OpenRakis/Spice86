; VBE Save/Restore State Buffer Size Test
; Tests subfunction 00h - get buffer size

org 0x100

ResultPort equ 0x999

section .text
start:
    ; Call INT 10h Function 4F04h subfunction 00h
    mov cx, 0x000F          ; All states (bits 0-3)
    mov dl, 0x00            ; Subfunction: get buffer size
    mov ax, 0x4F04
    int 0x10
    
    ; Check if AX = 004Fh
    cmp ax, 0x004F
    jne failed
    
    ; Check if BX > 0 (some buffer size returned)
    cmp bx, 0
    jbe failed
    
    mov al, 0x00            ; TestResult.Success
    jmp writeResult
    
failed:
    mov al, 0xFF            ; TestResult.Failure
    
writeResult:
    mov dx, ResultPort
    out dx, al
    hlt
