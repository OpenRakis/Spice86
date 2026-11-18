; midi_ack.asm - MPU-401 handshake test
;
; Every byte written to the command port 0x331 (reset, UART mode and
; MIDI data alike) must be acknowledged with 0xFE on the data port
; 0x330, or the host (A-TRAIN) blocks forever on the first note of its
; startup jingle. Each acknowledgement is consumed by one data-port
; read: a second read on an empty queue must return 0.
;
; Handshake of one command byte:
;   out 0x331, byte   ; transmit
;   in  0x331         ; status, bit 7 active low: 0x00 when the
;                     ;   acknowledgement is pending, 0x80 when it
;                     ;   is not
;   in  0x330         ; read the 0xFE acknowledgement
;
; Results (reported via ports 0x999/0x998 for Spice86):
;   Port 0x998: number of the failing test, or the total test count on success
;   Port 0x999: 0x00 on success, 0xFF on failure
;
; Assemble:  nasm -f bin -o midi_ack.com midi_ack.asm

[bits 16]
[org 0x100]

MIDI_CMD    equ 0x331
MIDI_DATA   equ 0x330
RESULT_PORT equ 0x999
DETAIL_PORT equ 0x998

start:
    ; --- Test 1: the reset command is acknowledged ---
    mov dx, MIDI_CMD
    mov al, 0xFF
    out dx, al
    in al, dx
    cmp al, 0x00
    jne .fail1
    mov dx, MIDI_DATA
    in al, dx
    cmp al, 0xFE
    jne .fail1
    jmp .t2

.fail1:
    mov al, 1
    jmp .fail

    ; --- Test 2: the UART-mode command is acknowledged ---
.t2:
    mov dx, MIDI_CMD
    mov al, 0x3F
    out dx, al
    in al, dx
    cmp al, 0x00
    jne .fail2
    mov dx, MIDI_DATA
    in al, dx
    cmp al, 0xFE
    jne .fail2
    jmp .t3

.fail2:
    mov al, 2
    jmp .fail

    ; --- Test 3: a MIDI data byte (note-off A) is acknowledged ---
.t3:
    mov dx, MIDI_CMD
    mov al, 0x8A
    out dx, al
    in al, dx
    cmp al, 0x00
    jne .fail3
    mov dx, MIDI_DATA
    in al, dx
    cmp al, 0xFE
    jne .fail3
    jmp .t4

.fail3:
    mov al, 3
    jmp .fail

    ; --- Test 4: each acknowledgement is consumed by one read ---
.t4:
    mov dx, MIDI_CMD
    mov al, 0xFF
    out dx, al
    mov dx, MIDI_DATA
    in al, dx                ; consume the acknowledgement
    mov dx, MIDI_CMD
    in al, dx
    cmp al, 0x80
    jne .fail4
    mov dx, MIDI_DATA
    in al, dx
    cmp al, 0x00
    jne .fail4

    ; --- Success: all four checks completed ---
    mov dx, DETAIL_PORT
    mov al, 4
    out dx, al
    mov dx, RESULT_PORT
    mov al, 0x00
    out dx, al
    jmp .exit

.fail4:
    mov al, 4

.fail:
    mov dx, DETAIL_PORT
    out dx, al
    mov dx, RESULT_PORT
    mov al, 0xFF
    out dx, al

.exit:
    int 0x20
