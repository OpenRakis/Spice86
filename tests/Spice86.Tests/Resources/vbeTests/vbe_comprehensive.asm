; Comprehensive VBE 1.0 Test Program
; Tests all VBE functions and displays results in text mode
; Can be run on real hardware for validation

org 0x100

ResultPort equ 0x999

section .data
    ; String constants
    msgTitle        db 'VESA VBE 1.0 Comprehensive Test', 13, 10, 0
    msgTest1        db '1. VBE Detection (4F00h): ', 0
    msgTest2        db '2. VBE Signature Check: ', 0
    msgTest3        db '3. VBE Version Check: ', 0
    msgTest4        db '4. Mode Info 0x100 (4F01h): ', 0
    msgTest5        db '5. Mode Info 0x101 (4F01h): ', 0
    msgTest6        db '6. Unsupported Mode (4F01h): ', 0
    msgTest7        db '7. Get Buffer Size (4F04h/00h): ', 0
    msgTest8        db '8. Display Window Control (4F05h): ', 0
    msgTest9        db '9. Controller Memory Check: ', 0
    msgTest10       db '10. Mode 0x100 Resolution: ', 0
    msgTest11       db '11. Mode 0x101 Resolution: ', 0
    msgTest12       db '12. Banking Info Mode 0x100: ', 0
    msgTest13       db '13. Banking Info Mode 0x101: ', 0
    
    msgPass         db 'PASS', 13, 10, 0
    msgFail         db 'FAIL', 13, 10, 0
    msgSummary      db 13, 10, 'Test Summary: ', 0
    msgTotal        db ' total, ', 0
    msgPassed       db ' passed, ', 0
    msgFailed       db ' failed', 13, 10, 0
    
section .bss
    testCount       resb 1
    passCount       resb 1
    failCount       resb 1
    vbeBuffer       resb 512

section .text
start:
    ; Initialize counters
    mov byte [testCount], 0
    mov byte [passCount], 0
    mov byte [failCount], 0
    
    ; Set video mode to text mode (80x25)
    mov ax, 0x0003
    int 0x10
    
    ; Print title
    mov si, msgTitle
    call print_string
    
    ; Test 1: VBE Detection (Function 00h)
    call test_vbe_detection
    
    ; Test 2: VBE Signature
    call test_vbe_signature
    
    ; Test 3: VBE Version
    call test_vbe_version
    
    ; Test 4: Mode Info for 0x100
    call test_mode_info_100
    
    ; Test 5: Mode Info for 0x101
    call test_mode_info_101
    
    ; Test 6: Unsupported mode
    call test_unsupported_mode
    
    ; Test 7: Get buffer size
    call test_get_buffer_size
    
    ; Test 8: Display window control
    call test_display_window
    
    ; Test 9: Controller memory
    call test_controller_memory
    
    ; Test 10: Mode 0x100 resolution
    call test_mode_100_resolution
    
    ; Test 11: Mode 0x101 resolution
    call test_mode_101_resolution
    
    ; Test 12: Banking info mode 0x100
    call test_banking_100
    
    ; Test 13: Banking info mode 0x101
    call test_banking_101
    
    ; Print summary
    call print_summary
    
    ; Exit program
    mov ax, 0x4C00
    int 0x21

;-----------------------------------------------------------
; Test Functions
;-----------------------------------------------------------

test_vbe_detection:
    mov si, msgTest1
    call print_string
    inc byte [testCount]
    
    ; Set up buffer
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0000
    
    ; Call VBE function 00h
    mov ax, 0x4F00
    int 0x10
    
    ; Check result
    cmp ax, 0x004F
    je .pass
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    jmp .done
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_vbe_signature:
    mov si, msgTest2
    call print_string
    inc byte [testCount]
    
    ; Check signature at ES:DI (still set from previous test)
    mov ax, es
    cmp ax, 0x2000
    jne .fail
    
    ; Check for "VESA" signature (56h 45h 53h 41h)
    mov al, [es:di]
    cmp al, 0x56  ; 'V'
    jne .fail
    mov al, [es:di+1]
    cmp al, 0x45  ; 'E'
    jne .fail
    mov al, [es:di+2]
    cmp al, 0x53  ; 'S'
    jne .fail
    mov al, [es:di+3]
    cmp al, 0x41  ; 'A'
    jne .fail
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    jmp .done
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_vbe_version:
    mov si, msgTest3
    call print_string
    inc byte [testCount]
    
    ; Check version at offset 4 (should be 0x0100 for VBE 1.0)
    mov ax, [es:di+4]
    cmp ax, 0x0100
    je .pass
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    jmp .done
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_mode_info_100:
    mov si, msgTest4
    call print_string
    inc byte [testCount]
    
    ; Set up buffer for mode info
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0200  ; Use offset to avoid overwriting controller info
    
    ; Get mode info for 0x100
    mov cx, 0x0100
    mov ax, 0x4F01
    int 0x10
    
    ; Check result
    cmp ax, 0x004F
    je .pass
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    jmp .done
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_mode_info_101:
    mov si, msgTest5
    call print_string
    inc byte [testCount]
    
    ; Set up buffer for mode info
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0200
    
    ; Get mode info for 0x101
    mov cx, 0x0101
    mov ax, 0x4F01
    int 0x10
    
    ; Check result
    cmp ax, 0x004F
    je .pass
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    jmp .done
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_unsupported_mode:
    mov si, msgTest6
    call print_string
    inc byte [testCount]
    
    ; Try to get info for unsupported mode 0x107
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0200
    mov cx, 0x0107
    mov ax, 0x4F01
    int 0x10
    
    ; Should return failure (AH != 0)
    cmp ah, 0x00
    je .fail  ; If success, test fails
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    jmp .done
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_get_buffer_size:
    mov si, msgTest7
    call print_string
    inc byte [testCount]
    
    ; Get buffer size for save/restore
    mov cx, 0x000F
    mov dl, 0x00
    mov ax, 0x4F04
    int 0x10
    
    ; Check result
    cmp ax, 0x004F
    jne .fail
    
    ; BX should contain number of 64-byte blocks
    cmp bx, 0
    je .fail
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    jmp .done
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_display_window:
    mov si, msgTest8
    call print_string
    inc byte [testCount]
    
    ; Test get window position (function 05h, subfunction 01h)
    mov bh, 0x01  ; Get window position
    mov bl, 0x00  ; Window A
    mov ax, 0x4F05
    int 0x10
    
    ; Check result
    cmp ax, 0x004F
    je .pass
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    jmp .done
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_controller_memory:
    mov si, msgTest9
    call print_string
    inc byte [testCount]
    
    ; Check total memory at offset 0x12 (should be >= 4 for 256KB)
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0000
    
    ; Get controller info again to be sure
    mov ax, 0x4F00
    int 0x10
    
    ; Check memory
    mov ax, [es:di+0x12]
    cmp ax, 4  ; At least 4 * 64KB = 256KB
    jge .pass
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    jmp .done
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_mode_100_resolution:
    mov si, msgTest10
    call print_string
    inc byte [testCount]
    
    ; Get mode info for 0x100
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0200
    mov cx, 0x0100
    mov ax, 0x4F01
    int 0x10
    
    ; Check width (offset 0x12) = 640
    mov ax, [es:di+0x12]
    cmp ax, 640
    jne .fail
    
    ; Check height (offset 0x14) = 400
    mov ax, [es:di+0x14]
    cmp ax, 400
    jne .fail
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    jmp .done
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_mode_101_resolution:
    mov si, msgTest11
    call print_string
    inc byte [testCount]
    
    ; Get mode info for 0x101
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0200
    mov cx, 0x0101
    mov ax, 0x4F01
    int 0x10
    
    ; Check width (offset 0x12) = 640
    mov ax, [es:di+0x12]
    cmp ax, 640
    jne .fail
    
    ; Check height (offset 0x14) = 480
    mov ax, [es:di+0x14]
    cmp ax, 480
    jne .fail
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    jmp .done
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_banking_100:
    mov si, msgTest12
    call print_string
    inc byte [testCount]
    
    ; Get mode info for 0x100
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0200
    mov cx, 0x0100
    mov ax, 0x4F01
    int 0x10
    
    ; Check window granularity (offset 0x04) = 64
    mov ax, [es:di+0x04]
    cmp ax, 64
    jne .fail
    
    ; Check window size (offset 0x06) = 64
    mov ax, [es:di+0x06]
    cmp ax, 64
    jne .fail
    
    ; Check number of banks (offset 0x1A) = 4 for 640x400
    mov al, [es:di+0x1A]
    cmp al, 4
    jne .fail
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    jmp .done
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

test_banking_101:
    mov si, msgTest13
    call print_string
    inc byte [testCount]
    
    ; Get mode info for 0x101
    mov ax, 0x2000
    mov es, ax
    mov di, 0x0200
    mov cx, 0x0101
    mov ax, 0x4F01
    int 0x10
    
    ; Check window granularity (offset 0x04) = 64
    mov ax, [es:di+0x04]
    cmp ax, 64
    jne .fail
    
    ; Check window size (offset 0x06) = 64
    mov ax, [es:di+0x06]
    cmp ax, 64
    jne .fail
    
    ; Check number of banks (offset 0x1A) = 5 for 640x480
    mov al, [es:di+0x1A]
    cmp al, 5
    jne .fail
    
.pass:
    inc byte [passCount]
    mov si, msgPass
    call print_string
    mov al, 0x00
    jmp .done
    
.fail:
    inc byte [failCount]
    mov si, msgFail
    call print_string
    mov al, 0xFF
    
.done:
    mov dx, ResultPort
    out dx, al
    ret

;-----------------------------------------------------------
; Helper Functions
;-----------------------------------------------------------

print_string:
    ; Print null-terminated string pointed to by SI
    push ax
    push bx
.loop:
    lodsb
    cmp al, 0
    je .done
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    jmp .loop
.done:
    pop bx
    pop ax
    ret

print_summary:
    ; Print test summary
    mov si, msgSummary
    call print_string
    
    ; Print total count
    mov al, [testCount]
    call print_number
    mov si, msgTotal
    call print_string
    
    ; Print passed count
    mov al, [passCount]
    call print_number
    mov si, msgPassed
    call print_string
    
    ; Print failed count
    mov al, [failCount]
    call print_number
    mov si, msgFailed
    call print_string
    
    ; Output overall result
    mov al, [failCount]
    cmp al, 0
    je .all_pass
    mov al, 0xFF  ; At least one failure
    jmp .done
.all_pass:
    mov al, 0x00  ; All tests passed
.done:
    mov dx, ResultPort
    out dx, al
    ret

print_number:
    ; Print number in AL as decimal
    push ax
    push bx
    push cx
    push dx
    
    xor ah, ah
    mov bl, 10
    xor cx, cx
    
.divide_loop:
    xor dx, dx
    div bl
    push dx
    inc cx
    cmp al, 0
    jne .divide_loop
    
.print_loop:
    pop dx
    add dl, '0'
    mov al, dl
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    loop .print_loop
    
    pop dx
    pop cx
    pop bx
    pop ax
    ret
