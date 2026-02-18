; opl3_note_playback.asm - OPL3 note playback test (no AdLib Gold control)
;
; Programs OPL channel 0 with a fast-attack note and verifies audio
; starts via Timer 1. This is the baseline for comparison with the
; AdLib Gold variant.
;
; The test:
;   1. Resets OPL timers
;   2. Programs operator envelopes + frequency for channel 0
;   3. Key-on channel 0
;   4. Uses Timer 1 to measure timing
;   5. Reports elapsed poll count
;
; Results (reported via ports 0x999/0x998):
;   Port 0x998: iteration count (low byte, then high byte)
;   Port 0x999: exit code (0x00=ok, 0x02=timeout)
;
; Assemble:  nasm -f bin -o opl3_note_playback.com opl3_note_playback.asm

[bits 16]
[org 0x100]

OPL_ADDR     equ 0x388
OPL_DATA     equ 0x389
OPL_ADDR2    equ 0x38A
OPL_DATA2    equ 0x38B
RESULT_PORT  equ 0x999
DETAIL_PORT  equ 0x998
TIMEOUT      equ 5000

start:
    ; ========================================
    ; Step 1: Reset OPL timers
    ; ========================================
    call opl_write_0x04_0x60
    call opl_write_0x04_0x80

    ; ========================================
    ; Step 2: Program OPL channel 0 for a note
    ; ========================================
    ; Set waveform for operator 0 (reg 0x20) - multiple = 1
    mov dx, OPL_ADDR
    mov al, 0x20
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x01       ; multiplier = 1
    out dx, al
    call opl_data_delay

    ; Set operator 0 total level (reg 0x40) - full volume
    mov dx, OPL_ADDR
    mov al, 0x40
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x00       ; 0 = maximum volume
    out dx, al
    call opl_data_delay

    ; Set operator 0 attack/decay (reg 0x60) - fast attack
    mov dx, OPL_ADDR
    mov al, 0x60
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0xF0       ; attack=15 (fastest), decay=0
    out dx, al
    call opl_data_delay

    ; Set operator 0 sustain/release (reg 0x80) - sustain=0 (max), release=0
    mov dx, OPL_ADDR
    mov al, 0x80
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x00       ; sustain=0 (max level), release=0 (no release)
    out dx, al
    call opl_data_delay

    ; Set modulator (operator 3) - same as above but for carrier
    mov dx, OPL_ADDR
    mov al, 0x23
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x01
    out dx, al
    call opl_data_delay

    mov dx, OPL_ADDR
    mov al, 0x43
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x00
    out dx, al
    call opl_data_delay

    mov dx, OPL_ADDR
    mov al, 0x63
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0xF0
    out dx, al
    call opl_data_delay

    mov dx, OPL_ADDR
    mov al, 0x83
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x00
    out dx, al
    call opl_data_delay

    ; Set channel 0 feedback/connection (reg 0xC0) - stereo output
    mov dx, OPL_ADDR
    mov al, 0xC0
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x31       ; left + right output, feedback=1, additive
    out dx, al
    call opl_data_delay

    ; ========================================
    ; Step 3: Set frequency and Key-on
    ; ========================================
    ; Set frequency low (reg 0xA0) - A4 = 440 Hz
    ; F-Number for 440Hz at block 4: F = 440 * 2^(20-4-1) / 49716 ~ 0x1A5
    mov dx, OPL_ADDR
    mov al, 0xA0
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0xA5       ; F-Number low byte
    out dx, al
    call opl_data_delay

    ; Set frequency high + key-on (reg 0xB0)
    ; Block=4, Key-on=1, F-Number high bits=0x01
    mov dx, OPL_ADDR
    mov al, 0xB0
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x31       ; key-on=1, block=4, fnum-high=1
    out dx, al
    call opl_data_delay

    ; ========================================
    ; Step 4: Start Timer 1 and poll
    ; ========================================
    ; Set Timer 1 counter = 0xFF (period = 80us)
    mov dx, OPL_ADDR
    mov al, 0x02
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0xFF
    out dx, al
    call opl_data_delay

    ; Start Timer 1 (reg 0x04 = 0x21)
    mov dx, OPL_ADDR
    mov al, 0x04
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x21
    out dx, al
    call opl_data_delay

    ; Poll status until overflow
    xor cx, cx
    mov dx, OPL_ADDR
.poll_loop:
    cmp cx, TIMEOUT
    jae .timeout_reached
    in al, dx
    inc cx
    test al, 0xC0
    jnz .timer_fired
    jmp .poll_loop

.timeout_reached:
    ; Report timeout
    mov dx, DETAIL_PORT
    mov al, cl
    out dx, al
    mov al, ch
    out dx, al
    mov dx, RESULT_PORT
    mov al, 0x02       ; timeout
    out dx, al
    jmp .cleanup

.timer_fired:
    ; Report success with iteration count
    mov dx, DETAIL_PORT
    mov al, cl
    out dx, al
    mov al, ch
    out dx, al
    mov dx, RESULT_PORT
    mov al, 0x00       ; success
    out dx, al

.cleanup:
    ; Key-off channel 0
    mov dx, OPL_ADDR
    mov al, 0xB0
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x11       ; key-on=0, same freq
    out dx, al
    call opl_data_delay

    ; Reset timers
    call opl_write_0x04_0x60
    call opl_write_0x04_0x80

.exit:
    int 0x20

; ============================================================
; OPL Helper Subroutines
; ============================================================

opl_write_0x04_0x60:
    mov dx, OPL_ADDR
    mov al, 0x04
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x60
    out dx, al
    call opl_data_delay
    ret

opl_write_0x04_0x80:
    mov dx, OPL_ADDR
    mov al, 0x04
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA
    mov al, 0x80
    out dx, al
    call opl_data_delay
    ret

opl_addr_delay:
    push cx
    push dx
    mov cx, 6
    mov dx, OPL_ADDR
.loop:
    in al, dx
    loop .loop
    pop dx
    pop cx
    ret

opl_data_delay:
    push cx
    push dx
    mov cx, 35
    mov dx, OPL_ADDR
.loop:
    in al, dx
    loop .loop
    pop dx
    pop cx
    ret
