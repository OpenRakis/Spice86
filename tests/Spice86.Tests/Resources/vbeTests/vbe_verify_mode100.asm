; Test that VBE mode 0x100 (640x400x256) is set successfully
; Just verifies the VBE function returns success
org 0x100

section .text
start:
    ; Set VBE mode 0x100 (640x400x256)
    mov ax, 0x4F02
    mov bx, 0x0100      ; Mode 0x100
    int 0x10
    
    ; Check return value
    cmp ax, 0x004F
    jne failure
    
success:
    mov dx, 0x999       ; Test result port
    mov al, 0x00        ; Success
    out dx, al
    
    ; Exit
    mov ax, 0x4C00
    int 0x21
    
failure:
    mov dx, 0x999       ; Test result port
    mov al, 0xFF        ; Failure
    out dx, al
    
    ; Exit
    mov ax, 0x4C01
    int 0x21
