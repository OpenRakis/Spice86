; VBE Signature Test
; Verifies "VESA" signature in controller info block

org 0x100

ResultPort equ 0x999

section .text
start:
    ; Set up buffer pointer at ES:DI
    mov ax, 0x2000          ; Buffer segment
    mov es, ax
    mov di, 0x0000          ; Buffer offset
    
    ; Call INT 10h Function 4F00h
    mov ax, 0x4F00
    int 0x10
    
    ; Check signature - "VESA" = 56h 45h 53h 41h
    cmp byte [es:di+0], 0x56    ; 'V'
    jne failed
    cmp byte [es:di+1], 0x45    ; 'E'
    jne failed
    cmp byte [es:di+2], 0x53    ; 'S'
    jne failed
    cmp byte [es:di+3], 0x41    ; 'A'
    jne failed
    
    mov al, 0x00            ; TestResult.Success
    jmp writeResult
    
failed:
    mov al, 0xFF            ; TestResult.Failure
    
writeResult:
    mov dx, ResultPort
    out dx, al
    hlt
