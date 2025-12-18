; CMOS/RTC Port Access Test
; Tests direct access to CMOS registers via ports 0x70 (address) and 0x71 (data)
; 
; Test results are written to port 0x999 (0x00 = success, 0xFF = failure)
; Test progress is written to port 0x998 (test number)
;
; This test verifies that CMOS time/date registers return valid BCD values
; and that the registers are accessible via the standard I/O ports.

    ORG 0x100
    BITS 16

    ; Constants
    RESULT_PORT     equ 0x999
    DETAILS_PORT    equ 0x998
    SUCCESS         equ 0x00
    FAILURE         equ 0xFF
    
    CMOS_ADDR_PORT  equ 0x70
    CMOS_DATA_PORT  equ 0x71
    
    ; CMOS Register addresses
    REG_SECONDS     equ 0x00
    REG_MINUTES     equ 0x02
    REG_HOURS       equ 0x04
    REG_DAY_OF_WEEK equ 0x06
    REG_DAY         equ 0x07
    REG_MONTH       equ 0x08
    REG_YEAR        equ 0x09

start:
    ; Test 1: Read seconds register
    mov dx, DETAILS_PORT
    mov al, 0x01
    out dx, al
    
    mov al, REG_SECONDS
    out CMOS_ADDR_PORT, al
    in al, CMOS_DATA_PORT
    call validate_bcd
    jc test_failed
    
    ; Test 2: Read minutes register
    mov dx, DETAILS_PORT
    mov al, 0x02
    out dx, al
    
    mov al, REG_MINUTES
    out CMOS_ADDR_PORT, al
    in al, CMOS_DATA_PORT
    call validate_bcd
    jc test_failed
    
    ; Test 3: Read hours register
    mov dx, DETAILS_PORT
    mov al, 0x03
    out dx, al
    
    mov al, REG_HOURS
    out CMOS_ADDR_PORT, al
    in al, CMOS_DATA_PORT
    call validate_bcd
    jc test_failed
    
    ; Test 4: Read day of week register
    mov dx, DETAILS_PORT
    mov al, 0x04
    out dx, al
    
    mov al, REG_DAY_OF_WEEK
    out CMOS_ADDR_PORT, al
    in al, CMOS_DATA_PORT
    call validate_bcd
    jc test_failed
    
    ; Test 5: Read day of month register
    mov dx, DETAILS_PORT
    mov al, 0x05
    out dx, al
    
    mov al, REG_DAY
    out CMOS_ADDR_PORT, al
    in al, CMOS_DATA_PORT
    call validate_bcd
    jc test_failed
    
    ; Test 6: Read month register
    mov dx, DETAILS_PORT
    mov al, 0x06
    out dx, al
    
    mov al, REG_MONTH
    out CMOS_ADDR_PORT, al
    in al, CMOS_DATA_PORT
    call validate_bcd
    jc test_failed
    
    ; Test 7: Read year register
    mov dx, DETAILS_PORT
    mov al, 0x07
    out dx, al
    
    mov al, REG_YEAR
    out CMOS_ADDR_PORT, al
    in al, CMOS_DATA_PORT
    call validate_bcd
    jc test_failed
    
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
