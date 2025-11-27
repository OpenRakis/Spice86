;00: 01 00
; Test PIT Channel 2 RateGenerator mode (Mode 2)
; This test verifies that the PC speaker pathway properly handles
; PIT control mode 2 (Rate Generator) without warnings.
;
; compile it with fasm
use16
start:
mov ax,0
mov ss,ax
mov sp,4

; ========================================
; Configure PIT Channel 2 in Mode 2 (Rate Generator)
; Control word format: CC MM AAA B
;   CC  = Channel (10 = Channel 2)
;   MM  = Access Mode (11 = lobyte/hibyte)
;   AAA = Operating Mode (010 = Rate Generator)
;   B   = BCD (0 = binary)
; Control byte: 10 11 010 0 = 0xB4
; ========================================
mov al, 0B4h            ; Channel 2, lobyte/hibyte, Mode 2, binary
out 43h, al             ; Write control word to PIT

; Set counter value for channel 2 (1000 = 0x03E8)
mov al, 0E8h            ; Low byte
out 42h, al
mov al, 03h             ; High byte
out 42h, al

; ========================================
; Enable speaker output (port 0x61)
; Bit 0: Timer 2 gate enable
; Bit 1: Speaker data enable
; ========================================
in al, 61h              ; Read current port B state
or al, 03h              ; Set bits 0 and 1
out 61h, al             ; Enable timer 2 and speaker

; Disable speaker
in al, 61h
and al, 0FCh            ; Clear bits 0 and 1
out 61h, al

; ========================================
; Test success - store 1 in memory
; ========================================
mov word[0], 1

; Halt the CPU
hlt

; bios entry point at offset fff0
rb 65520-$
jmp start
rb 65535-$
db 0ffh
