; VBE Mode 0x101 Resolution Test
; Verifies mode 0x101 reports 640x480 resolution

org 0x100

ResultPort equ 0x999

section .text
start:
    ; Set up buffer pointer
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0000
    
    ; Call INT 10h Function 4F01h with mode 0x101
    mov cx, 0x0101          ; Mode 0x101
    mov ax, 0x4F01
    int 0x10
    
    ; Check width at offset 12h - should be 640 (0x0280)
    mov ax, [es:di+12h]
    cmp ax, 640
    jne failed
    
    ; Check height at offset 14h - should be 480 (0x01E0)
    mov ax, [es:di+14h]
    cmp ax, 480
    jne failed
    
    mov al, 0x00            ; TestResult.Success
    jmp writeResult
    
failed:
    mov al, 0xFF            ; TestResult.Failure
    
writeResult:
    mov dx, ResultPort
    out dx, al
    hlt
