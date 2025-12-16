; Sound Blaster DSP Basic Command Test
; Tests DSP initialization and command acceptance
; Simpler test that doesn't require full DMA transfers

use16
org 0x100

start:
    ; Setup test environment
    mov ax, cs
    mov ds, ax
    mov es, ax
    
    ; Initialize test result
    mov word [test_result], 0x0000
    mov byte [tests_passed], 0
    
    ; Test 1: DSP Reset
    mov dx, 0x226           ; Reset port (base + 6)
    mov al, 1
    out dx, al
    mov cx, 10
.reset_wait1:
    loop .reset_wait1
    xor al, al
    out dx, al
    
    ; Wait for DSP ready (0xAA on read data port)
    mov dx, 0x22A           ; Read data port (base + 0xA)
    mov cx, 100
.wait_ready:
    in al, dx
    cmp al, 0xAA
    je .test1_pass
    loop .wait_ready
    jmp test_failed
    
.test1_pass:
    inc byte [tests_passed]
    
    ; Test 2: DSP Write Buffer Ready
    mov dx, 0x22C           ; Write status port
    mov cx, 100
.wait_write_ready:
    in al, dx
    test al, 0x80           ; Check if busy bit is clear
    jz .test2_pass
    loop .wait_write_ready
    jmp test_failed
    
.test2_pass:
    inc byte [tests_passed]
    
    ; Test 3: Send Get Version Command (0xE1)
    mov al, 0xE1
    out dx, al
    
    ; Wait for response on read data port
    mov dx, 0x22E           ; Read buffer status
    mov cx, 1000
.wait_version_status:
    in al, dx
    test al, 0x80           ; Data available?
    jnz .read_version
    loop .wait_version_status
    jmp test_failed
    
.read_version:
    mov dx, 0x22A           ; Read data port
    in al, dx               ; Major version
    mov [sb_version_major], al
    
    ; Read minor version
    mov dx, 0x22E           ; Check status again
    mov cx, 100
.wait_minor:
    in al, dx
    test al, 0x80
    jnz .read_minor
    loop .wait_minor
    jmp test_failed
    
.read_minor:
    mov dx, 0x22A
    in al, dx               ; Minor version
    mov [sb_version_minor], al
    
    inc byte [tests_passed]
    
    ; Test 4: Speaker Control
    mov dx, 0x22C           ; Write command port
    mov al, 0xD1            ; Speaker on command
    out dx, al
    
    ; Small delay
    mov cx, 10
.speaker_delay:
    loop .speaker_delay
    
    mov dx, 0x22C
    mov al, 0xD3            ; Speaker off command
    out dx, al
    
    inc byte [tests_passed]
    
    ; All tests passed if we get here
    cmp byte [tests_passed], 4
    jl test_failed
    
    ; Success!
    mov word [test_result], 0x0001
    jmp test_end
    
test_failed:
    mov word [test_result], 0xFFFF
    
test_end:
    ; Exit
    mov ax, 0x4C00
    int 0x21

; Data section
test_result:        dw 0x0000
tests_passed:       db 0
sb_version_major:   db 0
sb_version_minor:   db 0
