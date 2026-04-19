; Reproducer for https://github.com/dadhi/FastExpressionCompiler/issues/520
use16
org 0

start:
    cli

    ; ----------------------------
    ; Setup flat segments
    ; ----------------------------
    xor ax, ax
    mov ds, ax
    mov ss, ax
    mov sp, 0xFFFE

    ; ----------------------------
    ; Prepare stack frame area
    ; We will use [SS:BP-14]
    ; ----------------------------
    mov bp, 0x0100          ; safe area in low memory

    ; divisor = 0xE4C3 at [BP-14]
    mov word [bp-14], 0xE4C3

    ; ----------------------------
    ; Setup dividend
    ; DX:AX = 0x8000:0000
    ; ----------------------------
    mov ax, 0x0000
    mov dx, 0x8000

    ; ----------------------------
    ; THE INSTRUCTION
    ; ----------------------------
    div word [bp-14]

    ; ----------------------------
    ; Store results for inspection
    ; ----------------------------
    ; EAX 00008F3D
    ; EDX 00009089
    mov [0x0000], ax     ; quotient
    mov [0x0002], dx     ; remainder

    ; also store divisor
    mov cx, [bp-14]
    mov [0x0004], cx

hang:
    hlt
    jmp hang

; ----------------------------
; BIOS padding + reset vector
; ----------------------------
rb 65520 - $

    jmp start
    dw 0x0000

rb 65534 - $
    dw 0xFFFF