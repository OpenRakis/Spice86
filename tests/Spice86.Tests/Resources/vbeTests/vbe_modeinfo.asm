; VBE Mode Info Test
; Tests mode information retrieval for mode 0x100

org 0x100

ResultPort equ 0x999

section .text
start:
    ; Set up buffer pointer
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0000
    
    ; Call INT 10h Function 4F01h with mode 0x100
    mov cx, 0x0100          ; Mode 0x100
    mov ax, 0x4F01
    int 0x10
    
    ; Check if AX = 004Fh
    cmp ax, 0x004F
    je success
    
    mov al, 0xFF            ; TestResult.Failure
    jmp writeResult
    
success:
    mov al, 0x00            ; TestResult.Success
    
writeResult:
    mov dx, ResultPort
    out dx, al
    hlt
