; VBE Save/Restore Full Cycle Test
; Tests all subfunctions: get buffer size, save, restore

org 0x100

ResultPort equ 0x999

section .text
start:
    ; First get buffer size
    mov cx, 0x000F          ; All states
    mov dl, 0x00            ; Subfunction: get buffer size
    mov ax, 0x4F04
    int 0x10
    
    cmp ax, 0x004F
    jne failed
    
    cmp bx, 0
    jbe failed
    
    ; Set up buffer pointer for save
    mov ax, 0x2000
    mov es, ax
    mov bx, 0x0000
    
    ; Save state
    mov cx, 0x000F          ; All states
    mov dl, 0x01            ; Subfunction: save
    mov ax, 0x4F04
    int 0x10
    
    cmp ax, 0x004F
    jne failed
    
    ; Restore state
    mov cx, 0x000F          ; All states
    mov dl, 0x02            ; Subfunction: restore
    mov ax, 0x4F04
    int 0x10
    
    cmp ax, 0x004F
    jne failed
    
    mov al, 0x00            ; TestResult.Success
    jmp writeResult
    
failed:
    mov al, 0xFF            ; TestResult.Failure
    
writeResult:
    mov dx, ResultPort
    out dx, al
    hlt
