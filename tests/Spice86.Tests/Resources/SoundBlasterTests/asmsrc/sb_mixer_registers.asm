; Sound Blaster hardware mixer register test
; Mirrors DOSBox Staging mixer.cpp register expectations:
; - Mixer reset sets volumes to 0xFF and mono output with filter enabled
; - Volume writes echo through the data port with reserved bits set
; - Stereo select bit (0x02) is reflected on readback

use16
org 0x100

RESULT_PORT equ 0x0999

start:
    mov ax, cs
    mov ds, ax
    mov es, ax
    mov ax, 0x0003
    int 0x10
    mov si, heading
    call print_string

    mov byte [tests_passed], 0

    ; Reset mixer (address 0x00, data 0x00)
    mov dx, 0x0224
    mov al, 0x00
    out dx, al
    mov dx, 0x0225
    mov al, 0x00
    out dx, al

    ; Test 1: Default master volume should read 0xFF after reset
    mov dx, 0x0224
    mov al, 0x22
    out dx, al
    mov dx, 0x0225
    in al, dx
    cmp al, 0xFF
    jne test_failed
    inc byte [tests_passed]
    mov si, test1_ok
    call print_string

    ; Test 2: Writing 0x00 to master volume should read back 0x11 (reserved bits set)
    mov dx, 0x0224
    mov al, 0x22
    out dx, al
    mov dx, 0x0225
    mov al, 0x00
    out dx, al
    in al, dx
    cmp al, 0x11
    jne test_failed
    inc byte [tests_passed]
    mov si, test2_ok
    call print_string

; Test 3: Enable stereo (bit1=1) and disable filter (bit5=1) and confirm readback is 0x33
    mov dx, 0x0224
    mov al, 0x0E
    out dx, al
    mov dx, 0x0225
    mov al, 0x22            ; bit1 stereo, filter disabled (bit5 set)
    out dx, al
    in al, dx
    cmp al, 0x33
    jne test_failed
    inc byte [tests_passed]
    mov si, test3_ok
    call print_string

    ; All tests passed
    mov si, success_msg
    call print_string
    mov dx, RESULT_PORT
    mov al, 0x00
    out dx, al
    hlt

test_failed:
    mov si, failure_msg
    call print_string
    mov dx, RESULT_PORT
    mov al, 0xFF
    out dx, al
    hlt

tests_passed: db 0
heading:      db 'SB MIXER REG TEST',13,10,0
test1_ok:     db 'T1 DEFAULT VOL OK',13,10,0
test2_ok:     db 'T2 WRITEBACK OK',13,10,0
test3_ok:     db 'T3 STEREO OK',13,10,0
success_msg:  db 'MIXER PASS',13,10,0
failure_msg:  db 'MIXER FAIL',13,10,0

print_string:
    push ax
    push si
.print_loop:
    lodsb
    or al, al
    jz .done
    mov ah, 0x0E
    mov bh, 0x00
    mov bl, 0x07
    int 0x10
    jmp .print_loop
.done:
    pop si
    pop ax
    ret
