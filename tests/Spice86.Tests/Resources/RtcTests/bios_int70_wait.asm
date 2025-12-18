; BIOS INT 70h - RTC Periodic Interrupt Configuration Test
; Tests INT 70h (IRQ 8) RTC periodic interrupt handler setup
;
; This test verifies that INT 15h, AH=83h properly:
; - Enables the RTC periodic interrupt (bit 6 of Status Register B)
; - Sets up the wait flag and counter in BIOS data area
; - Can be canceled properly
;
; NOTE: Full wait completion testing requires real-time delays which are
; difficult to test in a cycle-limited emulator environment. This test
; focuses on verifying the setup and cancellation mechanisms.
;
; Test results are written to port 0x999 (0x00 = success, 0xFF = failure)
; Test progress is written to port 0x998 (test number)

    ORG 0x100
    BITS 16

    ; Constants
    RESULT_PORT     equ 0x999
    DETAILS_PORT    equ 0x998
    SUCCESS         equ 0x00
    FAILURE         equ 0xFF

    ; BIOS Data Area offsets (segment 0x0040)
    BDA_SEGMENT     equ 0x0040
    RTC_WAIT_FLAG   equ 0xA0    ; Offset for RTC wait flag
    USER_FLAG_PTR   equ 0x98    ; Offset for user wait complete flag pointer (segment:offset)
    USER_TIMEOUT    equ 0x9C    ; Offset for user wait timeout (32-bit)

start:
    ; Set up data segment to point to code segment
    push cs
    pop ds
    
    ; Test 1: Set up a wait with INT 15h, AH=83h
    mov dx, DETAILS_PORT
    mov al, 0x01
    out dx, al
    
    ; Clear the user flag location
    mov byte [user_flag], 0x00
    
    ; Set up wait: INT 15h, AH=83h, AL=00h (set wait)
    ; CX:DX = microseconds (use 10000 microseconds = 0x2710)
    mov ah, 0x83            ; Function 83h - WAIT
    mov al, 0x00            ; Sub-function 00h - Set wait
    mov cx, 0x0000          ; High word of microseconds
    mov dx, 0x2710          ; Low word (10000 microseconds)
    mov bx, user_flag       ; Offset of user flag
    push cs
    pop es                  ; ES:BX points to user_flag in our code segment
    int 0x15
    jc test_failed          ; CF should be clear on success
    
    ; Test 2: Verify RTC wait flag is set in BIOS data area
    mov dx, DETAILS_PORT
    mov al, 0x02
    out dx, al
    
    push ds
    mov ax, BDA_SEGMENT
    mov ds, ax
    mov al, [RTC_WAIT_FLAG]
    pop ds
    cmp al, 0x01            ; Should be 1 (wait active)
    jne test_failed
    
    ; Test 3: Verify CMOS Status Register B has periodic interrupt enabled (bit 6)
    mov dx, DETAILS_PORT
    mov al, 0x03
    out dx, al
    
    mov al, 0x0B            ; Status Register B
    out 0x70, al            ; Write to CMOS address port
    in al, 0x71             ; Read from CMOS data port
    test al, 0x40           ; Check bit 6 (PIE - Periodic Interrupt Enable)
    jz test_failed          ; Should be set
    
    ; Test 4: Verify user wait timeout was stored in BIOS data area
    mov dx, DETAILS_PORT
    mov al, 0x04
    out dx, al
    
    push ds
    mov ax, BDA_SEGMENT
    mov ds, ax
    mov ax, [USER_TIMEOUT]      ; Low word
    mov dx, [USER_TIMEOUT+2]    ; High word
    pop ds
    ; Should be 0x00002710 (10000 decimal)
    cmp dx, 0x0000
    jne test_failed
    cmp ax, 0x2710
    jne test_failed
    
    ; Test 5: Cancel the wait with AL=01h
    mov dx, DETAILS_PORT
    mov al, 0x05
    out dx, al
    
    mov ah, 0x83            ; Function 83h - WAIT
    mov al, 0x01            ; Sub-function 01h - Cancel wait
    int 0x15
    jc test_failed          ; CF should be clear on success
    
    ; Test 6: Verify RTC wait flag is cleared after cancel
    mov dx, DETAILS_PORT
    mov al, 0x06
    out dx, al
    
    push ds
    mov ax, BDA_SEGMENT
    mov ds, ax
    mov al, [RTC_WAIT_FLAG]
    pop ds
    cmp al, 0x00            ; Should be 0 (wait inactive)
    jne test_failed
    
    ; Test 7: Verify CMOS Status Register B has periodic interrupt disabled after cancel
    mov dx, DETAILS_PORT
    mov al, 0x07
    out dx, al
    
    mov al, 0x0B            ; Status Register B
    out 0x70, al            ; Write to CMOS address port
    in al, 0x71             ; Read from CMOS data port
    test al, 0x40           ; Check bit 6 (PIE)
    jnz test_failed         ; Should be cleared now
    
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

; Data section
user_flag:
    db 0x00                 ; User flag that would be set to 0x80 by INT 70h
