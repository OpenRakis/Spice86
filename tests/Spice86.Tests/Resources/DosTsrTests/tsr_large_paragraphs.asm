; TSR large paragraph count test - request 4096 paragraphs
; build: nasm -f bin tsr_large_paragraphs.asm -o tsr_large_paragraphs.com

BITS 16
ORG 100h

TSR_RET_CODE    EQU 0x00
PARAGRAPHS      EQU 0x1000  ; 4096 paragraphs (large request)

start:
    ; Set up INT 22h handler to point to our HLT
    mov dx, hlt_location
    mov ah, 0x25
    mov al, 0x22
    int 0x21            ; Set vector for INT 22h

    ; Call TSR with AL=0x00 (return code), DX=0x1000 (4096 paragraphs)
    mov ax, 0x3100 | TSR_RET_CODE
    mov dx, PARAGRAPHS
    int 0x21            ; Invoke TSR

hlt_location:
    hlt
