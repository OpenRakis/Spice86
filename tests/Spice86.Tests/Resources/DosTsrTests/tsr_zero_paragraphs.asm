; TSR zero paragraphs test - request 0 paragraphs
; build: nasm -f bin tsr_zero_paragraphs.asm -o tsr_zero_paragraphs.com

BITS 16
ORG 100h

TSR_RET_CODE    EQU 0x00

start:
    ; Set up INT 22h handler to point to our HLT
    mov dx, hlt_location
    mov ah, 0x25
    mov al, 0x22
    int 0x21            ; Set vector for INT 22h

    ; Call TSR with AL=0x00 (return code), DX=0x00 (zero paragraphs)
    mov ax, 0x3100 | TSR_RET_CODE
    mov dx, 0x00
    int 0x21            ; Invoke TSR

hlt_location:
    hlt
