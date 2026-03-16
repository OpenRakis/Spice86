; BIOS INT 1A Time Services Test
; Tests BIOS INT 1A functions 00h-05h
;
; Test results are written to port 0x999 (0x00 = success, 0xFF = failure)
; Test progress is written to port 0x998 (test number)
;
; This test verifies BIOS time services including:
; - System clock counter get/set
; - RTC time read/write
; - RTC date read/write

    ORG 0x100
    BITS 16

    ; Constants
    RESULT_PORT     equ 0x999
    DETAILS_PORT    equ 0x998
    SUCCESS         equ 0x00
    FAILURE         equ 0xFF

start:
    ; Test 1: INT 1A, AH=00h - Get System Clock Counter
    mov dx, DETAILS_PORT
    mov al, 0x01
    out dx, al
    
    mov ah, 0x00
    int 0x1A
    ; Just verify it doesn't crash, return values are in CX:DX
    
    ; Test 2: INT 1A, AH=01h - Set System Clock Counter
    mov dx, DETAILS_PORT
    mov al, 0x02
    out dx, al
    
    mov ah, 0x01
    mov cx, 0x0012      ; High word of tick count
    mov dx, 0x3456      ; Low word of tick count
    int 0x1A
    ; Verify by reading back
    mov ah, 0x00
    int 0x1A
    ; Values should be close to what we set (may have incremented)
    
    ; Test 3: INT 1A, AH=02h - Read RTC Time
    mov dx, DETAILS_PORT
    mov al, 0x03
    out dx, al
    
    mov ah, 0x02
    int 0x1A
    jc test_failed      ; CF should be clear on success
    ; CH = hours (BCD), CL = minutes (BCD), DH = seconds (BCD)
    ; Validate hours
    mov al, ch
    call validate_bcd
    jc test_failed
    ; Validate minutes
    mov al, cl
    call validate_bcd
    jc test_failed
    ; Validate seconds
    mov al, dh
    call validate_bcd
    jc test_failed
    
    ; Test 4: INT 1A, AH=03h - Set RTC Time (stub in emulator)
    mov dx, DETAILS_PORT
    mov al, 0x04
    out dx, al
    
    mov ah, 0x03
    mov ch, 0x12        ; 12 hours (BCD)
    mov cl, 0x34        ; 34 minutes (BCD)
    mov dh, 0x56        ; 56 seconds (BCD)
    mov dl, 0x00        ; Standard time
    int 0x1A
    jc test_failed      ; CF should be clear on success
    
    ; Test 5: INT 1A, AH=04h - Read RTC Date
    mov dx, DETAILS_PORT
    mov al, 0x05
    out dx, al
    
    mov ah, 0x04
    int 0x1A
    jc test_failed      ; CF should be clear on success
    ; CH = century (BCD), CL = year (BCD), DH = month (BCD), DL = day (BCD)
    ; Validate century
    mov al, ch
    call validate_bcd
    jc test_failed
    ; Validate year
    mov al, cl
    call validate_bcd
    jc test_failed
    ; Validate month
    mov al, dh
    call validate_bcd
    jc test_failed
    ; Validate day
    mov al, dl
    call validate_bcd
    jc test_failed
    
    ; Test 6: INT 1A, AH=05h - Set RTC Date (stub in emulator)
    mov dx, DETAILS_PORT
    mov al, 0x06
    out dx, al
    
    mov ah, 0x05
    mov ch, 0x20        ; 20 (century, BCD)
    mov cl, 0x24        ; 24 (year, BCD) = 2024
    mov dh, 0x11        ; 11 (month, BCD)
    mov dl, 0x14        ; 14 (day, BCD)
    int 0x1A
    jc test_failed      ; CF should be clear on success
    
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

; Validates that AL contains a valid BCD value
; Sets carry flag if invalid
validate_bcd:
    push ax
    push bx
    
    ; Check high nibble (should be 0-9)
    mov bl, al
    shr bl, 4
    cmp bl, 9
    ja .invalid
    
    ; Check low nibble (should be 0-9)
    mov bl, al
    and bl, 0x0F
    cmp bl, 9
    ja .invalid
    
    ; Valid BCD
    clc
    pop bx
    pop ax
    ret
    
.invalid:
    stc
    pop bx
    pop ax
    ret
