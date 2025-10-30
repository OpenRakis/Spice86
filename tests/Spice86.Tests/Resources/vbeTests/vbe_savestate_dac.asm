; VBE DAC Save/Restore Test
; Tests that DAC palette is saved and restored correctly

org 0x100

ResultPort equ 0x999

section .text
start:
    ; Set DAC color 0 to red (63, 0, 0)
    mov dx, 0x3C8       ; DAC write index port
    xor al, al          ; Color index 0
    out dx, al
    
    mov dx, 0x3C9       ; DAC data port
    mov al, 63          ; Red = 63
    out dx, al
    xor al, al          ; Green = 0
    out dx, al
    xor al, al          ; Blue = 0
    out dx, al
    
    ; Set up buffer pointer for save
    mov ax, 0x2000
    mov es, ax
    mov bx, 0x0000
    
    ; Save DAC state
    mov cx, 0x0004      ; DAC state only (bit 2)
    mov dl, 0x01        ; Subfunction: save
    mov ax, 0x4F04
    int 0x10
    
    cmp ax, 0x004F
    jne failed
    
    ; Change DAC color 0 to blue (0, 0, 63)
    mov dx, 0x3C8
    xor al, al
    out dx, al
    
    mov dx, 0x3C9
    xor al, al          ; Red = 0
    out dx, al
    xor al, al          ; Green = 0
    out dx, al
    mov al, 63          ; Blue = 63
    out dx, al
    
    ; Restore DAC state
    mov cx, 0x0004      ; DAC state
    mov dl, 0x02        ; Subfunction: restore
    mov ax, 0x4F04
    int 0x10
    
    cmp ax, 0x004F
    jne failed
    
    ; Read back color 0 and verify it's red again
    mov dx, 0x3C7       ; DAC read index port
    xor al, al
    out dx, al
    
    mov dx, 0x3C9       ; DAC data port
    in al, dx           ; Red component
    cmp al, 63
    jne failed
    
    in al, dx           ; Green component
    cmp al, 0
    jne failed
    
    in al, dx           ; Blue component
    cmp al, 0
    jne failed
    
    mov al, 0x00        ; TestResult.Success
    jmp writeResult
    
failed:
    mov al, 0xFF        ; TestResult.Failure
    
writeResult:
    mov dx, ResultPort
    out dx, al
    hlt
