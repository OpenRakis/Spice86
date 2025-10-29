; VBE Detection Test
; Tests VBE presence via INT 10h AX=4F00h
; Returns success if AX=004Fh indicating VBE support

org 0x100

ResultPort equ 0x999

section .text
start:
    ; Set up buffer pointer at ES:DI
    mov ax, 0x2000          ; Buffer segment
    mov es, ax
    mov di, 0x0000          ; Buffer offset
    
    ; Call INT 10h Function 4F00h (Return Controller Info)
    mov ax, 0x4F00
    int 0x10
    
    ; Check if AX = 004Fh (VBE supported and successful)
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
