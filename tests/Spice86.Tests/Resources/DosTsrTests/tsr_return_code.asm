; TSR return code test - use custom return code 0x42
; build: nasm -f bin tsr_return_code.asm -o tsr_return_code.com

BITS 16
ORG 100h

TSR_RET_CODE    EQU 0x42    ; Custom return code
PARAGRAPHS      EQU 0x10    ; 16 paragraphs

start:
    ; Set up INT 22h handler to point to our HLT
    mov dx, hlt_location
    mov ah, 0x25
    mov al, 0x22
    int 0x21            ; Set vector for INT 22h

    ; Call TSR with AL=0x42 (custom return code), DX=0x10 (16 paragraphs)
    mov ax, 0x3100 | TSR_RET_CODE
    mov dx, PARAGRAPHS
    int 0x21            ; Invoke TSR

hlt_location:
    hlt
