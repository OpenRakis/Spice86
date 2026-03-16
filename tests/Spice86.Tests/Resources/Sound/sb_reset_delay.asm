; sb_reset_delay.asm - Sound Blaster DSP reset delay test
; Tests that the DSP reset takes a measurable number of iterations
; to complete (20 microseconds hardware delay from DOSBox Staging).
;
; At 3000 cycles/ms, 20us = 0.020ms = 60 cycles.
; Each poll loop iteration is several instructions (~4-5 cycles),
; so we expect roughly 12-15 iterations before 0xAA appears.
;
; The test:
;   1. Writes 1 to DSP Reset port (0x226) to start reset
;   2. Writes 0 to DSP Reset port (0x226) to trigger the 20us timer
;   3. Polls DSP Read Status (0x22E) bit 7 + DSP Read Data (0x22A)
;      counting iterations until 0xAA appears or timeout
;   4. Reports iteration count via INT 10h (for DOSBox validation)
;      and via I/O ports 0x999/0x998 (for Spice86 test assertion)
;
; To assemble: nasm -f bin -o sb_reset_delay.com sb_reset_delay.asm
; To run in DOSBox: just run sb_reset_delay.com
;
; Expected: iteration count > 0 (delay is non-zero)
;           data byte = 0xAA (reset succeeded)

[bits 16]
[org 0x100]

SB_BASE     equ 0x220
DSP_RESET   equ SB_BASE + 0x06   ; 0x226
DSP_READ    equ SB_BASE + 0x0A   ; 0x22A
DSP_WRITE   equ SB_BASE + 0x0C   ; 0x22C
DSP_RSTATUS equ SB_BASE + 0x0E   ; 0x22E

RESULT_PORT equ 0x999
DETAIL_PORT equ 0x998
TIMEOUT     equ 1000

start:
    ; --- Print banner ---
    mov si, msg_banner
    call print_string

    ; === Step 1: Write 1 to DSP Reset (start reset) ===
    mov dx, DSP_RESET
    mov al, 1
    out dx, al

    ; Small delay between write 1 and write 0 (a few NOPs like real programs)
    nop
    nop
    nop

    ; === Step 2: Write 0 to DSP Reset (trigger 20us timer) ===
    mov dx, DSP_RESET
    mov al, 0
    out dx, al

    ; === Step 3: Poll for 0xAA with iteration counter ===
    xor cx, cx          ; CX = iteration counter

.poll_loop:
    cmp cx, TIMEOUT
    jae .timeout

    ; Check if data is available (bit 7 of read status)
    mov dx, DSP_RSTATUS
    in al, dx
    test al, 0x80
    jz .no_data

    ; Data available - read it
    mov dx, DSP_READ
    in al, dx
    cmp al, 0xAA
    je .got_aa

.no_data:
    inc cx
    jmp .poll_loop

.timeout:
    ; --- Timeout: report failure ---
    mov si, msg_timeout
    call print_string

    ; Print iteration count
    mov ax, cx
    call print_dec

    mov si, msg_newline
    call print_string

    ; Report to Spice86: failure
    mov dx, DETAIL_PORT
    mov al, cl          ; low byte of count
    out dx, al
    mov al, ch          ; high byte of count
    out dx, al

    mov dx, RESULT_PORT
    mov al, 0xFF        ; FAIL
    out dx, al

    int 0x20            ; DOS terminate

.got_aa:
    ; --- Success: 0xAA received after CX iterations ---
    mov si, msg_ok
    call print_string

    ; Print iteration count
    mov ax, cx
    call print_dec

    mov si, msg_iters
    call print_string

    ; Report to Spice86: iteration count (low, high) then success
    mov dx, DETAIL_PORT
    mov al, cl          ; low byte of iteration count
    out dx, al
    mov al, ch          ; high byte of iteration count
    out dx, al

    mov dx, RESULT_PORT
    mov al, 0x00        ; SUCCESS
    out dx, al

    int 0x20            ; DOS terminate

; ============================================================
; Subroutines
; ============================================================

; print_string: Print null-terminated string at DS:SI via INT 10h
print_string:
    lodsb
    or al, al
    jz .done
    mov ah, 0x0E        ; INT 10h teletype output
    mov bx, 0x0007      ; page 0, light grey
    int 0x10
    jmp print_string
.done:
    ret

; print_dec: Print AX as unsigned decimal via INT 10h
print_dec:
    push bx
    push cx
    push dx
    xor cx, cx          ; digit count
    mov bx, 10
.div_loop:
    xor dx, dx
    div bx              ; AX = AX/10, DX = remainder
    push dx             ; save digit
    inc cx
    or ax, ax
    jnz .div_loop
.print_loop:
    pop ax
    add al, '0'
    mov ah, 0x0E
    mov bx, 0x0007
    int 0x10
    loop .print_loop
    pop dx
    pop cx
    pop bx
    ret

; ============================================================
; Data
; ============================================================

msg_banner:  db 'SB Reset Delay Test', 13, 10, 0
msg_ok:      db 'OK: 0xAA after ', 0
msg_iters:   db ' iterations', 13, 10, 0
msg_timeout: db 'FAIL: Timeout after ', 0
msg_newline: db 13, 10, 0
