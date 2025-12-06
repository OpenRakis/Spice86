; BIOS INT 15h, AH=83h - Event Wait Interval Test
; Tests BIOS INT 15h, AH=83h (WAIT FUNCTION)
;
; Test results are written to port 0x999 (0x00 = success, 0xFF = failure)
; Test progress is written to port 0x998 (test number)
;
; This test verifies:
; - Setting a wait event (AL=00h)
; - Detecting already-active wait (error condition)
; - Canceling a wait event (AL=01h)

    ORG 0x100
    BITS 16

    ; Constants
    RESULT_PORT     equ 0x999
    DETAILS_PORT    equ 0x998
    SUCCESS         equ 0x00
    FAILURE         equ 0xFF

start:
    ; Test 1: Set a wait event (AL=00h)
    mov dx, DETAILS_PORT
    mov al, 0x01
    out dx, al
    
    mov ah, 0x83            ; Function 83h - WAIT
    mov al, 0x00            ; Sub-function 00h - Set wait
    mov cx, 0x0000          ; High word of microseconds (0x00001000 = 4096 us)
    mov dx, 0x1000          ; Low word of microseconds
    mov bx, 0x0000          ; Offset of callback (0000:0000 = no callback)
    push cs
    pop es                  ; ES = CS
    int 0x15
    jc test_failed          ; CF should be clear on success
    
    ; Test 2: Try to set another wait while one is active (should fail with AH=80h)
    mov dx, DETAILS_PORT
    mov al, 0x02
    out dx, al
    
    mov ah, 0x83            ; Function 83h - WAIT
    mov al, 0x00            ; Sub-function 00h - Set wait
    mov cx, 0x0000
    mov dx, 0x1000
    mov bx, 0x0000
    push cs
    pop es
    int 0x15
    jnc test_failed         ; CF should be set (error)
    cmp ah, 0x80            ; AH should be 0x80 (event already in progress)
    jne test_failed
    
    ; Test 3: Cancel the wait event (AL=01h)
    mov dx, DETAILS_PORT
    mov al, 0x03
    out dx, al
    
    mov ah, 0x83            ; Function 83h - WAIT
    mov al, 0x01            ; Sub-function 01h - Cancel wait
    int 0x15
    jc test_failed          ; CF should be clear on success
    
    ; Test 4: Set a new wait after canceling (should succeed)
    mov dx, DETAILS_PORT
    mov al, 0x04
    out dx, al
    
    mov ah, 0x83            ; Function 83h - WAIT
    mov al, 0x00            ; Sub-function 00h - Set wait
    mov cx, 0x0000
    mov dx, 0x2000          ; Different wait time
    mov bx, 0x0000
    push cs
    pop es
    int 0x15
    jc test_failed          ; CF should be clear on success
    
    ; Test 5: Cancel the second wait
    mov dx, DETAILS_PORT
    mov al, 0x05
    out dx, al
    
    mov ah, 0x83            ; Function 83h - WAIT
    mov al, 0x01            ; Sub-function 01h - Cancel wait
    int 0x15
    jc test_failed          ; CF should be clear on success
    
    ; All tests passed
    mov dx, RESULT_PORT
    mov al, SUCCESS
    out dx, al
    hlt

test_failed:
    mov dx, RESULT_PORT
    mov al, FAILURE
    out dx, al
    hlt
