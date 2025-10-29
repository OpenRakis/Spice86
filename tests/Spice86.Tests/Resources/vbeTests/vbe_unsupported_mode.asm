; VBE Unsupported Mode Test
; Tests error handling for invalid mode number

org 0x100

ResultPort equ 0x999

section .text
start:
    ; Set up buffer pointer
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0000
    
    ; Call INT 10h Function 4F01h with invalid mode
    mov cx, 0xFFFF          ; Invalid mode
    mov ax, 0x4F01
    int 0x10
    
    ; Check if AX = 014Fh (supported but failed)
    cmp ax, 0x014F
    je success
    
    mov al, 0xFF            ; TestResult.Failure
    jmp writeResult
    
success:
    mov al, 0x00            ; TestResult.Success
    
writeResult:
    mov dx, ResultPort
    out dx, al
    hlt
