; Tests that physical page 4 is rejected (only 0-3 are valid).
; This tests for the off-by-one bug where > was used instead of >= in bounds checking.

use16
org 0x100

start:
    ; Allocate 4 pages
    mov bx, 4
    mov ah, 43h
    int 67h
    cmp ah, 0
    jne failed
    mov cx, dx              ; Save handle

    ; Try to map to physical page 4 (should fail - only 0-3 valid)
    mov al, 4               ; Physical page 4 (invalid!)
    mov bx, 0               ; Logical page 0
    mov dx, cx
    mov ah, 44h
    int 67h
    cmp ah, 8Bh             ; Should return illegal physical page error
    je success

failed:
    mov al, 0FFh            ; TestResult.Failure
    jmp writeResult

success:
    mov al, 0               ; TestResult.Success

writeResult:
    mov dx, 0999h           ; ResultPort
    out dx, al
    hlt
