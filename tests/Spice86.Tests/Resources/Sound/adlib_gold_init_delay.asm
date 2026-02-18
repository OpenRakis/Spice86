; adlib_gold_init_delay.asm - AdLib Gold initial audio delay test
;
; Tests that OPL3Gold mode produces audio output immediately after
; programming registers. Exercises the AdLib Gold control interface
; and then programs a simple OPL note, verifying audio starts without
; excessive delay.
;
; The test:
;   1. Activates AdLib Gold control (port 0x38A = 0xFF)
;   2. Sets stereo volume via control interface
;   3. Resets OPL timers
;   4. Programs operator envelopes + frequency for channel 0
;   5. Key-on channel 0
;   6. Uses Timer 1 to measure time from key-on to timer overflow
;   7. Reports elapsed poll count - verifies timing is not delayed
;
; Results (reported via ports 0x999/0x998):
;   Port 0x998: iteration count (low byte, then high byte)
;   Port 0x999: exit code (0x00=ok, 0x01=gold_ctrl_fail, 0x02=timeout)
;
; Assemble:  nasm -f bin -o adlib_gold_init_delay.com adlib_gold_init_delay.asm

[bits 16]
[org 0x100]

OPL_ADDR     equ 0x388
OPL_DATA     equ 0x389
OPL_ADDR2    equ 0x38A    ; Secondary address / AdLib Gold control port
OPL_DATA2    equ 0x38B    ; Secondary data / AdLib Gold control data port
RESULT_PORT  equ 0x999
DETAIL_PORT  equ 0x998
TIMEOUT      equ 5000

start:
    ; ========================================
    ; Step 1: Activate AdLib Gold control
    ; ========================================
    ; Write 0xFF to port 0x38A to activate control interface
    mov dx, OPL_ADDR2
    mov al, 0xFF
    out dx, al

    ; Read board options (index 0x00) to verify AdLib Gold is present
    ; First set index register
    mov dx, OPL_ADDR2
    mov al, 0x00       ; index 0x00 = Board Options
    out dx, al

    ; Read the data
    mov dx, OPL_DATA2
    in al, dx

    ; Should return 0x50 (16-bit ISA, surround module)
    cmp al, 0x50
    je .gold_ok

    ; Report failure: AdLib Gold control not responding correctly
    mov dx, DETAIL_PORT
    out dx, al         ; report actual value read
    mov al, 0x00
    out dx, al
    mov dx, RESULT_PORT
    mov al, 0x01       ; gold_ctrl_fail
    out dx, al
    jmp .exit

.gold_ok:
    ; ========================================
    ; Step 2: Set AdLib Gold stereo volume
    ; ========================================
    ; Set left FM volume (index 0x09) to max (0x1F)
    mov dx, OPL_ADDR2
    mov al, 0x09       ; index: Left FM Volume
    out dx, al
    mov dx, OPL_DATA2
    mov al, 0x1F       ; max volume
    out dx, al

    ; Set right FM volume (index 0x0A) to max (0x1F)
    mov dx, OPL_ADDR2
    mov al, 0x0A       ; index: Right FM Volume
    out dx, al
    mov dx, OPL_DATA2
    mov al, 0x1F       ; max volume
    out dx, al

    ; ========================================
    ; Step 3: Reset OPL timers
    ; ========================================
    call opl_write_0x04_0x60
    call opl_write_0x04_0x80

    ; ========================================
    ; Step 4: Program OPL channel 0 for a note
    ; ========================================
    ; Enable OPL3 mode (register 0x105 = 1) via high bank
    ; Write to addr port 0x38A with value 0x05
    mov dx, OPL_ADDR2
    mov al, 0xFE       ; Deactivate gold control first
    out dx, al
    mov dx, OPL_ADDR2
    mov al, 0x05       ; register 0x05 in high bank
    out dx, al
    call opl_addr_delay
    mov dx, OPL_DATA2
    mov al, 0x01       ; enable OPL3 mode
    out dx, al
    call opl_data_delay

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
    ; Step 5: Set frequency and Key-on
    ; ========================================
    ; Set frequency low (reg 0xA0) - A4 = 440 Hz
    ; F-Number for 440Hz at block 4: F = 440 * 2^(20-4-1) / 49716 ≈ 0x1A5
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
    ; Step 6: Start Timer 1 and poll
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

    ; Deactivate AdLib Gold control
    mov dx, OPL_ADDR2
    mov al, 0xFE
    out dx, al

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
