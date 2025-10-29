; VBE Save State Test
; Tests subfunction 01h - save state

org 0x100

ResultPort equ 0x999

section .text
start:
    ; Set up buffer pointer
    mov ax, 0x2000
    mov es, ax
    mov bx, 0x0000
    
    ; Call INT 10h Function 4F04h subfunction 01h
    mov cx, 0x000F          ; All states
    mov dl, 0x01            ; Subfunction: save
    mov ax, 0x4F04
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
