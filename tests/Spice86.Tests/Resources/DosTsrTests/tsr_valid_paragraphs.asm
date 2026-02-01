; TSR valid paragraphs test - request 32 paragraphs
; build: nasm -f bin tsr_valid_paragraphs.asm -o tsr_valid_paragraphs.com

BITS 16
ORG 100h

TSR_RET_CODE    EQU 0x00
PARAGRAPHS      EQU 0x20    ; 32 paragraphs = 512 bytes

start:
    ; Set up INT 22h handler to point to our HLT
    mov dx, hlt_location
    mov ah, 0x25
    mov al, 0x22
    int 0x21            ; Set vector for INT 22h

    ; Call TSR with AL=0x00 (return code), DX=0x20 (32 paragraphs)
    mov ax, 0x3100 | TSR_RET_CODE
    mov dx, PARAGRAPHS
    int 0x21            ; Invoke TSR

hlt_location:
    hlt
