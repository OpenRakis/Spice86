; TSR interrupt vector test - set and verify INT F0h vector
; build: nasm -f bin tsr_interrupt_vector.asm -o tsr_interrupt_vector.com

BITS 16
ORG 100h

EXIT_SUCCESS    EQU 0x00
EXIT_FAILURE    EQU 0xFF

start:
    ; Set DS to 1234h, DX to 5678h for our fake handler address
    mov ax, 0x1234
    mov ds, ax
    mov dx, 0x5678

    ; Set INT F0h vector: AH=25h, AL=F0h
    mov ah, 0x25
    mov al, 0xF0
    int 0x21

    ; Get INT F0h vector: AH=35h, AL=F0h
    mov ah, 0x35
    mov al, 0xF0
    int 0x21            ; Now ES:BX = 1234:5678

    ; Check BX == 5678h
    cmp bx, 0x5678
    jne failed

    ; Check ES == 1234h
    mov ax, es
    cmp ax, 0x1234
    jne failed

    ; Success
    mov al, EXIT_SUCCESS
    mov ax, 0x4C00 | EXIT_SUCCESS
    int 0x21

failed:
    mov ax, 0x4C00 | EXIT_FAILURE
    int 0x21
