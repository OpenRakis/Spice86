; TSR basic test - allocate, terminate, and stay resident
; build: nasm -f bin tsr_basic.asm -o tsr_basic.com

BITS 16
ORG 100h

PSP_SIZE        EQU 100h
TSR_RET_CODE    EQU 0x00    ; Success return code
PARAGRAPHS      EQU 0x08    ; 8 paragraphs = 128 bytes

start:
    ; Set up INT 22h handler to point to our HLT
    mov dx, hlt_location
    mov ah, 0x25
    mov al, 0x22
    int 0x21            ; Set vector for INT 22h

    ; Call TSR with AL=0x00 (return code), DX=0x08 (8 paragraphs)
    mov ax, 0x3100 | TSR_RET_CODE
    mov dx, PARAGRAPHS
    int 0x21            ; Invoke TSR - terminates program, returns via INT 22h

hlt_location:
    hlt
