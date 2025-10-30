; Simple test - just write success and halt
; This tests if the test infrastructure works at all
org 0x100

ResultPort equ 0x999

section .text
start:
    mov al, 0x00        ; TestResult.Success
    mov dx, ResultPort
    out dx, al
    hlt
