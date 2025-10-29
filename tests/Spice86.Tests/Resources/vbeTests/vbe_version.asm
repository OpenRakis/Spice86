; VBE Version Test
; Verifies VBE version is 0100h (1.0)

org 0x100

ResultPort equ 0x999

section .text
start:
    ; Set up buffer pointer
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0000
    
    ; Call INT 10h Function 4F00h
    mov ax, 0x4F00
    int 0x10
    
    ; Check version at offset 04h - should be 0100h
    mov ax, [es:di+04h]
    cmp ax, 0x0100
    je success
    
    mov al, 0xFF            ; TestResult.Failure
    jmp writeResult
    
success:
    mov al, 0x00            ; TestResult.Success
    
writeResult:
    mov dx, ResultPort
    out dx, al
    hlt
