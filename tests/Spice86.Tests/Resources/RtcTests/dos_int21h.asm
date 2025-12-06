; DOS INT 21H Date/Time Services Test
; Tests DOS INT 21H functions 2Ah-2Dh
;
; Test results are written to port 0x999 (0x00 = success, 0xFF = failure)
; Test progress is written to port 0x998 (test number)
;
; This test verifies DOS date/time services including:
; - Get/Set system date
; - Get/Set system time
; - Error handling for invalid values

    ORG 0x100
    BITS 16

    ; Constants
    RESULT_PORT     equ 0x999
    DETAILS_PORT    equ 0x998
    SUCCESS         equ 0x00
    FAILURE         equ 0xFF

start:
    ; Test 1: INT 21H, AH=2Ah - Get System Date
    mov dx, DETAILS_PORT
    mov al, 0x01
    out dx, al
    
    mov ah, 0x2A
    int 0x21
    ; CX = year (1980-2099), DH = month (1-12), DL = day (1-31), AL = day of week (0-6)
    ; Validate year >= 1980
    cmp cx, 1980
    jb test_failed
    ; Validate month 1-12
    cmp dh, 1
    jb test_failed
    cmp dh, 12
    ja test_failed
    ; Validate day 1-31
    cmp dl, 1
    jb test_failed
    cmp dl, 31
    ja test_failed
    
    ; Test 2: INT 21H, AH=2Bh - Set System Date (valid date)
    mov dx, DETAILS_PORT
    mov al, 0x02
    out dx, al
    
    mov ah, 0x2B
    mov cx, 2024        ; Year
    mov dh, 11          ; Month
    mov dl, 14          ; Day
    int 0x21
    ; AL = 0x00 on success, 0xFF on failure
    cmp al, 0x00
    jne test_failed
    
    ; Test 3: INT 21H, AH=2Bh - Set System Date (invalid year - too early)
    mov dx, DETAILS_PORT
    mov al, 0x03
    out dx, al
    
    mov ah, 0x2B
    mov cx, 1979        ; Year before 1980 (invalid)
    mov dh, 1           ; Month
    mov dl, 1           ; Day
    int 0x21
    ; AL should be 0xFF (failure) for invalid date
    cmp al, 0xFF
    jne test_failed
    
    ; Test 4: INT 21H, AH=2Bh - Set System Date (invalid month)
    mov dx, DETAILS_PORT
    mov al, 0x04
    out dx, al
    
    mov ah, 0x2B
    mov cx, 2024        ; Year
    mov dh, 13          ; Month (invalid)
    mov dl, 1           ; Day
    int 0x21
    ; AL should be 0xFF (failure) for invalid date
    cmp al, 0xFF
    jne test_failed
    
    ; Test 5: INT 21H, AH=2Bh - Set System Date (invalid day)
    mov dx, DETAILS_PORT
    mov al, 0x05
    out dx, al
    
    mov ah, 0x2B
    mov cx, 2024        ; Year
    mov dh, 1           ; Month
    mov dl, 32          ; Day (invalid)
    int 0x21
    ; AL should be 0xFF (failure) for invalid date
    cmp al, 0xFF
    jne test_failed
    
    ; Test 6: INT 21H, AH=2Ch - Get System Time
    mov dx, DETAILS_PORT
    mov al, 0x06
    out dx, al
    
    mov ah, 0x2C
    int 0x21
    ; CH = hour (0-23), CL = minutes (0-59), DH = seconds (0-59), DL = hundredths (0-99)
    ; Validate hour 0-23
    cmp ch, 23
    ja test_failed
    ; Validate minutes 0-59
    cmp cl, 59
    ja test_failed
    ; Validate seconds 0-59
    cmp dh, 59
    ja test_failed
    ; Validate hundredths 0-99
    cmp dl, 99
    ja test_failed
    
    ; Test 7: INT 21H, AH=2Dh - Set System Time (valid time)
    mov dx, DETAILS_PORT
    mov al, 0x07
    out dx, al
    
    mov ah, 0x2D
    mov ch, 12          ; Hour
    mov cl, 34          ; Minutes
    mov dh, 56          ; Seconds
    mov dl, 78          ; Hundredths
    int 0x21
    ; AL = 0x00 on success, 0xFF on failure
    cmp al, 0x00
    jne test_failed
    
    ; Test 8: INT 21H, AH=2Dh - Set System Time (invalid hour)
    mov dx, DETAILS_PORT
    mov al, 0x08
    out dx, al
    
    mov ah, 0x2D
    mov ch, 24          ; Hour (invalid)
    mov cl, 0           ; Minutes
    mov dh, 0           ; Seconds
    mov dl, 0           ; Hundredths
    int 0x21
    ; AL should be 0xFF (failure) for invalid time
    cmp al, 0xFF
    jne test_failed
    
    ; Test 9: INT 21H, AH=2Dh - Set System Time (invalid minutes)
    mov dx, DETAILS_PORT
    mov al, 0x09
    out dx, al
    
    mov ah, 0x2D
    mov ch, 0           ; Hour
    mov cl, 60          ; Minutes (invalid)
    mov dh, 0           ; Seconds
    mov dl, 0           ; Hundredths
    int 0x21
    ; AL should be 0xFF (failure) for invalid time
    cmp al, 0xFF
    jne test_failed
    
    ; Test 10: INT 21H, AH=2Dh - Set System Time (invalid seconds)
    mov dx, DETAILS_PORT
    mov al, 0x0A
    out dx, al
    
    mov ah, 0x2D
    mov ch, 0           ; Hour
    mov cl, 0           ; Minutes
    mov dh, 60          ; Seconds (invalid)
    mov dl, 0           ; Hundredths
    int 0x21
    ; AL should be 0xFF (failure) for invalid time
    cmp al, 0xFF
    jne test_failed
    
    ; Test 11: INT 21H, AH=2Dh - Set System Time (invalid hundredths)
    mov dx, DETAILS_PORT
    mov al, 0x0B
    out dx, al
    
    mov ah, 0x2D
    mov ch, 0           ; Hour
    mov cl, 0           ; Minutes
    mov dh, 0           ; Seconds
    mov dl, 100         ; Hundredths (invalid)
    int 0x21
    ; AL should be 0xFF (failure) for invalid time
    cmp al, 0xFF
    jne test_failed
    
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
